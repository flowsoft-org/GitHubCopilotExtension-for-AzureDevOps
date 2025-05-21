using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults
builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddHttpClient();

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Register Semantic Kernel and Agent services
builder.Services.AddSingleton<Kernel>(sp =>
{
    var kernel = Kernel.CreateBuilder().Build();
    return kernel;
});

// Register IChatCompletionService as a factory that requires the GitHub token
builder.Services.AddScoped<IChatCompletionService>(sp => 
{
    // This will be replaced with the actual token during request processing
    // The service will be properly initialized in the request handler
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<GitHubCopilotChatCompletionService>();
    return new GitHubCopilotChatCompletionService(
        // Default empty token will be replaced during request
        "", 
        logger
    );
});

// Add AgentService as a scoped service
builder.Services.AddScoped<AgentService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseDefaultMiddleware();

// Add a new endpoint for GitHub Copilot Extension
app.MapPost("/copilot", async (HttpContext context, AgentService agentService) =>
{
    try
    {
        // Read the request body/payload
        using var reader = new StreamReader(context.Request.Body);
        var requestBody = await reader.ReadToEndAsync();
        
        app.Logger.LogDebug("Received request body: {RequestBody}", requestBody);

        // Verify validity of call
        // https://docs.github.com/en/copilot/building-copilot-extensions/building-a-copilot-agent-for-your-copilot-extension/configuring-your-copilot-agent-to-communicate-with-github#verifying-that-payloads-are-coming-from-github
        // https://github.com/github-technology-partners/signature-verification
        if (!await GitHubService.IsValidGitHubRequest(
            requestBody,
            context.Request.Headers["X-GitHub-Public-Key-Identifier"]!,
            context.Request.Headers["X-GitHub-Public-Key-Signature"]!,
            app.Logger,
            context.Request.Headers["X-GitHub-Token"]!
        )) {
            app.Logger.LogError("Invalid GitHub request.");
            return Results.Text(GitHubService.SimpleResponseMessage("You are not a valid sender. Request unauthorized. Go away!"), "application/json", System.Text.Encoding.UTF8, statusCode: 401);
        }

        // Check if the account is already mapped to Azure DevOps if not the x-azure-devops-token header is empty
        if (context.Request.Headers["x-azure-devops-token"].FirstOrDefault(string.Empty) == string.Empty){
            app.Logger.LogError("Azure DevOps token is missing. User needs to reauthenticate.");
            return Results.Text(GitHubService.SimpleResponseMessage("Please reauthenticate the GitHub Copilot Extension to access Azure DevOps by visiting "+ app.Configuration["services:authservice:https:0"] +"/preauth"), "application/json", System.Text.Encoding.UTF8, statusCode: 200);
        }

        // Parse request body as JSON
        JsonDocument? jsonDocument = null;
        try {
            jsonDocument = JsonSerializer.Deserialize<JsonDocument>(requestBody);
        }
        catch (JsonException ex) {
            app.Logger.LogError(ex, "Failed to parse request body as JSON");
            return Results.Text(GitHubService.SimpleResponseMessage("Failed to parse your request. Please try again."), "application/json", System.Text.Encoding.UTF8, statusCode: 200);
        }

        // All good
        var prompt = new { role = "system", content = "Did the user already give a Azure DevOps Organization URL? If no then answer with 'NO', otherwise answer with the URL e.g. 'https://dev.azure.com/org123'." };
        var httpAnswer = await GitHubService.GHCopilotChatCompletion(context.Request.Headers["X-GitHub-Token"]!, jsonDocument!, prompt);

        var (role, answer) = await GitHubService.ExtractLastMessageFromCompletionResponse(httpAnswer);
        app.Logger.LogDebug($"Role: {role} Answer: {answer}");

        if (answer == "NO") {
            app.Logger.LogError("User did not provide a Azure DevOps Organization URL.");
            return Results.Text(GitHubService.SimpleResponseMessage("Please provide a Azure DevOps Organization URL."), "application/json", System.Text.Encoding.UTF8, statusCode: 200);
        } 
        else {
            try {
                // Get the GitHub token and Azure DevOps token from headers
                var githubToken = context.Request.Headers["X-GitHub-Token"]!;
                var azureDevOpsToken = context.Request.Headers["x-azure-devops-token"]!;
                
                // Update the GitHub token in the chat completion service
                if (app.Services.GetRequiredService<IChatCompletionService>() is GitHubCopilotChatCompletionService chatService)
                {
                    chatService.UpdateToken(githubToken);
                }
                
                // Create the kernel and register plugins
                var kernel = app.Services.GetRequiredService<Kernel>();
                
                // Register Azure DevOps plugin
                var azureDevOpsPlugin = new AzureDevOpsPlugin(answer, azureDevOpsToken, app.Logger);
                kernel.Plugins.AddFromObject(azureDevOpsPlugin, "AzureDevOps");
                               
                // Process the request with the agent
                return Results.Text(
                    await agentService.ProcessGitHubCopilotRequestAsync(jsonDocument!, githubToken, azureDevOpsToken, answer),
                    "application/json", 
                    System.Text.Encoding.UTF8, 
                    statusCode: 200
                );
            }
            catch (Exception ex) {
                app.Logger.LogError(ex, "Error processing request with agent");
                return Results.Text(
                    GitHubService.SimpleResponseMessage($"Sorry, I encountered an error: {ex.Message}"), 
                    "application/json", 
                    System.Text.Encoding.UTF8, 
                    statusCode: 200
                );
            }
        }
    }
    catch (Exception ex) {
        app.Logger.LogError(ex, "Unhandled exception in copilot endpoint");
        return Results.Text(
            GitHubService.SimpleResponseMessage("Sorry, something went wrong processing your request."), 
            "application/json", 
            System.Text.Encoding.UTF8, 
            statusCode: 200
        );
    }
})
.WithName("PostCopilotMessage");

app.Run();