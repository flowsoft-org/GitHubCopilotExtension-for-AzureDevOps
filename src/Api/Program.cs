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
app.MapPost("/token", async (HttpContext context) =>
{
    // Log the request headers and body
    app.Logger.LogInformation("Received headers: {Headers}", context.Request.Headers);
    // Log the received request query string
    app.Logger.LogInformation("Received Query: {QueryString}", context.Request.QueryString);
    // Log the received request body
    using var reader = new StreamReader(context.Request.Body);
    var requestBody = await reader.ReadToEndAsync();
    app.Logger.LogInformation("Received request: {RequestBody}", requestBody);


    // Return a response with the received data
    // return Results.Json(new
    // {
    //     access_token
    //     = "your_access_token",
    //     token_type = "Bearer",
    //     issued_token_type = "urn:ietf:params:oauth:token-type:access_token",
    //     expires_in = 60, // for testing purposes, set to 60 seconds
    // }, statusCode: 200);

    return Results.Json(new
    {
        error
        = "invalid_request"
    }, statusCode: 200);
})
.WithName("PostTokeExchange");

// Add a new endpoint for GitHub Copilot Extension
app.MapPost("/copilot", async (HttpContext context) =>
{
    // Log the request headers and body
    app.Logger.LogInformation("Received headers: {Headers}", context.Request.Headers);
    // Log the received request body
    using var reader = new StreamReader(context.Request.Body);
    var requestBody = await reader.ReadToEndAsync();
    app.Logger.LogInformation("Received request: {RequestBody}", requestBody);

    // Return a response with the received data
    return Results.Json(new
    {
        Message = "GitHub Copilot Extension endpoint is active.",
    }, statusCode: 200);
})
.WithName("PostCopilotMessage");

app.Run();