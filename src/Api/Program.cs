var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// // Add OIDC authentication
// builder.Services.AddAuthentication(options =>
// {
//     options.DefaultAuthenticateScheme = "Bearer";
//     options.DefaultChallengeScheme = "Bearer";
// })
// .AddJwtBearer("Bearer", options =>
// {
//     options.Authority = "https://your-oidc-provider.com"; // Replace with your OIDC provider
//     options.Audience = "your-audience"; // Replace with your API audience
// });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Enable authentication and authorization
// app.UseAuthentication();
// app.UseAuthorization();

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

    // Log the request headers and body
    app.Logger.LogDebug("Received headers: {Headers}", context.Request.Headers);
    // Log the received request body
    app.Logger.LogDebug("Received request: {RequestBody}", requestBody);

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