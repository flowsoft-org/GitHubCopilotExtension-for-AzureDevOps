using System.Text.Json;

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseDefaultMiddleware();

// Add a new endpoint for GitHub Copilot Extension
app.MapPost("/copilot", async (HttpContext context) =>
{
    // Read the request body/payload
    using var reader = new StreamReader(context.Request.Body);
    var requestBody = await reader.ReadToEndAsync();

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
        return Results.Text(GitHubService.SimpleResponseMessage("Please reauthenticate the GitHub Copilot Extension to access Azure DevOps by visiting "+ builder.Configuration["services:authservice:https:0"] +"/preauth"), "application/json", System.Text.Encoding.UTF8, statusCode: 200);
    }

    // All good
    var prompt = new { role = "system", content = "Did the user already give a Azure DevOps Organization URL? If no then answer with 'NO', otherwise answer with the URL e.g. 'https://dev.azure.com/org123'." };
    var httpAnswer = await GitHubService.GHCopilotChatCompletion(context.Request.Headers["X-GitHub-Token"]!, JsonSerializer.Deserialize<JsonDocument>(requestBody)!, prompt);

    var (role, answer) = await GitHubService.ExtractLastMessageFromCompletionResponse(httpAnswer);
    app.Logger.LogDebug($"Role: {role} Answer: {answer}");

    if (answer == "NO") {
        app.Logger.LogError("User did not provide a Azure DevOps Organization URL.");
        return Results.Text(GitHubService.SimpleResponseMessage("Please provide a Azure DevOps Organization URL."), "application/json", System.Text.Encoding.UTF8, statusCode: 200);
    } 
    else {
        var azdoClient = new AzureDevOpsClient(answer, context.Request.Headers["x-azure-devops-token"]!);
         return Results.Text(GitHubService.SimpleResponseMessage($"Welcome you have {await azdoClient.GetOpenWorkItemsCountAsync()} open Workitems!"), "application/json", System.Text.Encoding.UTF8, statusCode: 200);
    }
})
.WithName("PostCopilotMessage");

app.Run();