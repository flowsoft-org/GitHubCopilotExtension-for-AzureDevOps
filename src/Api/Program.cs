var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.AddServiceDefaults();

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
        return Results.Text(GitHubService.SimpleResponseMessage("Please reauthenticate the GitHub Copilot Extension to access Azure DevOps by visiting https://"+ app.Configuration["AppAuthDomain"] +"/preauth"), "application/json", System.Text.Encoding.UTF8, statusCode: 200);
    }

    // All good
    return Results.Text(GitHubService.SimpleResponseMessage("Welcome!"), "application/json", System.Text.Encoding.UTF8, statusCode: 200);
})
.WithName("PostCopilotMessage");

app.Run();