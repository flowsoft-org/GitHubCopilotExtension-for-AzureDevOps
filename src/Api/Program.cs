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
        app.Logger
    )) {
        app.Logger.LogError("Invalid GitHub request.");
        return Results.Json(new
        {
            error = "unauthorized_request"
        }, statusCode: 401);
    }

    if (context.Request.Headers["x-azure-devops-token"].FirstOrDefault() == string.Empty){
        app.Logger.LogError("Azure DevOps token is missing. User needs to reauthorize.");
        return Results.Json(new
        {
            error = "missing_azure_devops_token"
        }, statusCode: 200);
    }


    // Check if the account is already mapped to Azure DevOps
    var (isMapped, gitHubUserId) = await AccountMapping.CheckAccountMapping(context.Request.Headers, app.Logger);
    if (isMapped)
    {
        // Proceed
        app.Logger.LogDebug("Account is mapped to Azure DevOps.");
        return Results.Json(new
        {
            Message = "Azure DevOps account mapping is not implemented.",
        }, statusCode: 200);
    } 
    else 
    {
        // Start Mapping Procedure
        return Results.Json(new
        {
            Message = "GitHub Copilot Extension endpoint is active.",
            UserId = gitHubUserId
        }, statusCode: 200);
    }

})
.WithName("PostCopilotMessage");

app.Run();