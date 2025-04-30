using Helpers;
using StackExchange.Redis;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Azure;
using StackExchange.Redis.Configuration;

const string STATE_COOKIE_GITHUB = "oauth_state_github";
const string STATE_COOKIE_ENTRA = "oauth_state_entra";

var builder = WebApplication.CreateBuilder(args);

// Add service defaults
builder.AddServiceDefaults();

// Add Azure Key Vault configuration if not local development
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddAzureKeyVaultSecrets(connectionName: "secrets");
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Redis
var azureOptionsProvider = new AzureOptionsProvider();

var configurationOptions = ConfigurationOptions.Parse(
    builder.Configuration.GetConnectionString("tokenCache") ?? 
    throw new InvalidOperationException("Could not find a 'tokenCache' connection string."));

if (configurationOptions.EndPoints.Any(azureOptionsProvider.IsMatch))
{
    await configurationOptions.ConfigureForAzureWithTokenCredentialAsync(
        new DefaultAzureCredential());
}

builder.AddRedisClient(connectionName: "tokenCache", configureOptions: options =>
{
    options.Defaults = configurationOptions.Defaults;
});


var app = builder.Build();

// Use default middleware
app.UseDefaultMiddleware();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

/* Authentication flow:
- User clicks on the "Sign in with GitHub" button in the Copilot extension.
- The extension sends a request to the /preauth endpoint.
- The /preauth endpoint generates a state value and constructs the authorization URL for GitHub
- The /preauth endpoint sets the state value in a secure cookie and redirects the user to the authorization URL.
- The user is redirected to the GitHub authorization page.
- The user authorizes the application and is redirected back to the /postauth-github endpoint with the authorization code and state.
- The /postauth-github endpoint verifies the state value from the cookie and the query string.
- The /postauth-github endpoint exchanges the authorization code for an access token.
- The /postauth-github endpoint retrieves the user's information using the access token.
- The /postauth-github endpoint creates the authorization URL for Azure DevOps/Entra ID Application and puts the GitHubUserId in the state value.
- The /postauth-github endpoint sets the state value in a secure cookie and redirects the user to the Azure DevOps/Entra ID authorization URL.
- The user is redirected to the Azure DevOps/Entra ID authorization page.
- The user authorizes the application and is redirected back to the /postauth-azure endpoint with the authorization code and state.
- The /postauth-entra endpoint verifies the state value from the cookie and the query string.
- The /postauth-entra endpoint exchanges the authorization code for an access token.
- The Azure access token is put into a token store making it searchable for the GitHub UserID

- The /token endpoint is called by the Copilot extension with the GitHub UserID to retrieve the Azure access token.
- The /token endpoint retrieves the Azure access token from the token store using the GitHub UserID.
- The /token endpoint returns the Azure access token to the Copilot extension.
*/

// Endpoint for GitHub Copilot Extension preauthentication
app.MapGet("/preauth", (HttpContext context) =>
{
    // Generate state value and authUrl
    // https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app
    // limited options available for GitHub
    var state = Guid.NewGuid().ToString();
    var redirectDomain = builder.Environment.IsDevelopment() ? builder.Configuration["GitHubApp:AppAuthDomain"] : context.Request.Host.Host;
    var authUrl = $"{builder.Configuration["GitHubApp:Instance"]}/authorize" +
                $"?client_id={builder.Configuration["GitHubApp:ClientId"]}" +
                $"&redirect_uri={Uri.EscapeDataString($"{context.Request.Scheme}://{redirectDomain}{builder.Configuration["GitHubApp:CallbackPath"]}")}" +
                $"&state={state}";

    context.Response.Cookies.Append(
        STATE_COOKIE_GITHUB, // Cookie name
        state, // Cookie value
        new CookieOptions
        {
            HttpOnly = true, // Prevent access from JavaScript
            Secure = true, // Ensure the cookie is sent over HTTPS
            SameSite = SameSiteMode.Lax,
            IsEssential = true // Ensure the cookie is always sent
        }
    );

    app.Logger.LogDebug("Redirecting to authorization URL: {AuthUrl}", authUrl);

    // Return URL rewrite
    context.Response.Redirect(authUrl);
}).WithName("PreAuthorization");


// Retrieve the access token from GitHub
app.MapGet("/postauth-github", async (HttpContext context) =>
{
    var state = context.Request.Query["state"].ToString();
    var code = context.Request.Query["code"].ToString();
    var cookieState = context.Request.Cookies[STATE_COOKIE_GITHUB];
    app.Logger.LogDebug("State: {State}, Code: {Code}, Cookie State: {CookieState}", state, code, cookieState);
    if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(cookieState) || string.IsNullOrWhiteSpace(code))
    {
        app.Logger.LogError("State, cookie, or code is null or empty.");
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 400);
    }

    // Validate the state value
    if (state != cookieState)
    {
        app.Logger.LogError("State value mismatch.");
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 400);
    }

    try
    {
        // Exchange the authorization code for an access token
        var tokenUrl = $"{builder.Configuration["GitHubApp:Instance"]}/access_token";
        var redirectDomain = builder.Environment.IsDevelopment() ? builder.Configuration["GitHubApp:AppAuthDomain"] : context.Request.Host.Host;
        var tokenRequestBody = new FormUrlEncodedContent(new[] {
            new KeyValuePair<string, string>("client_id", builder.Environment.IsDevelopment() ? builder.Configuration["GitHubApp:ClientId:Dev"] : builder.Configuration["GitHubApp:ClientId"]),
            new KeyValuePair<string, string>("client_secret", builder.Configuration["GitHubApp:ClientSecret"]),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", $"{context.Request.Scheme}://{redirectDomain}{builder.Configuration["GitHubApp:CallbackPath"]}"),
        });

        var tokenResponse = await new HttpClient().PostAsync(tokenUrl, tokenRequestBody);
        tokenResponse.EnsureSuccessStatusCode();

        if (app.Logger.IsEnabled(LogLevel.Debug))
        {
            var tokenResponseBody = await tokenResponse.Content.ReadAsStringAsync();
            app.Logger.LogDebug("Token response: {TokenResponse}", tokenResponseBody);
        }

        var tokenResponseData = await Helpers.OAuth2.ConvertToTokenResponse(tokenResponse.Content);
        var accessToken = tokenResponseData.AccessToken;

        var gitHubUserId = await new GitHubService(accessToken, app.Logger).GetUserIdAsync();
        app.Logger.LogDebug("GitHub User ID: {GitHubUserId}", gitHubUserId);

        var authUrl = GenerateEntraIdAuthUrlAndSetStateCookie(context, gitHubUserId);
        app.Logger.LogDebug("Redirecting to authorization URL: {AuthUrl}", authUrl);

        // Return URL rewrite
        return Results.Redirect(authUrl);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to exchange authorization code for access token.");
        return Results.Json(new
        {
            error = "invalid_token"
        }, statusCode: 400);
    }
})
.WithName("PostAuthGitHub");


string GenerateEntraIdAuthUrlAndSetStateCookie(HttpContext context, long gitHubUserId)
{
    // Generate the authorization URL for Azure DevOps/Entra ID Application
    var state = Guid.NewGuid().ToString();
    var redirectDomain = builder.Environment.IsDevelopment() ? builder.Configuration["EntraIdApp:AppAuthDomain"] : context.Request.Host.Host;
    var authUrl = $"{builder.Configuration["EntraIdApp:Instance"]}{builder.Configuration["EntraIdApp:TenantId"]}/oauth2/v2.0/authorize" +
                $"?client_id={builder.Configuration["EntraIdApp:ClientId"]}" +
                $"&response_type=code" +
                $"&redirect_uri={Uri.EscapeDataString($"{context.Request.Scheme}://{redirectDomain}{builder.Configuration["EntraIdApp:CallbackPath"]}")}" +
                $"&response_mode=query" +
                $"&scope=openid profile email {Uri.EscapeDataString("https://app.vssps.visualstudio.com/user_impersonation")}" +
                $"&state={state}_{Uri.EscapeDataString(gitHubUserId.ToString())}" + // Add your state value here
                $"&nonce={state}";

    context.Response.Cookies.Append(
        STATE_COOKIE_ENTRA, // Cookie name
        state, // Cookie value
        new CookieOptions
        {
            HttpOnly = true, // Prevent access from JavaScript
            Secure = true, // Ensure the cookie is sent over HTTPS
            SameSite = SameSiteMode.Lax,
            IsEssential = true // Ensure the cookie is always sent
        }
    );

    app.Logger.LogDebug("Generated Entra ID auth URL: {AuthUrl}", authUrl);
    return authUrl;
}


// Add a new endpoint for GitHub Copilot Extension
app.MapGet("/postauth-entra", async (HttpContext context, IConnectionMultiplexer connectionMux) =>
{
    var state = context.Request.Query["state"].ToString().Split('_')[0];
    var gitHubUserId = context.Request.Query["state"].ToString().Split('_')[1];
    var cookieState = context.Request.Cookies[STATE_COOKIE_ENTRA];
    app.Logger.LogDebug("State: {State}, Cookie State: {CookieState}", state, cookieState);
    if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(cookieState))
    {
        app.Logger.LogError("State or cookie is null or empty.");
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 400);
    }
    // Validate the state value
    if (state != cookieState)
    {
        app.Logger.LogError("State value mismatch.");
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 400);
    }

    // Extract code from the request body
    var code = context.Request.Query["code"].ToString();
    if (string.IsNullOrWhiteSpace(code))
    {
        app.Logger.LogError("Code is missing.");
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 400);
    }
    app.Logger.LogDebug("Received code: {Code}", code);
    // Exchange the authorization code for an access token
    var tokenUrl = $"{builder.Configuration["EntraIdApp:Instance"]}{builder.Configuration["EntraIdApp:TenantId"]}/oauth2/v2.0/token";
    var redirectDomain = builder.Environment.IsDevelopment() ? builder.Configuration["EntraIdApp:AppAuthDomain"] : context.Request.Host.Host;
    var tokenRequestBody = new FormUrlEncodedContent(new[]
    {
        new KeyValuePair<string, string>("client_id", builder.Configuration["EntraIdApp:ClientId"]),
        new KeyValuePair<string, string>("client_secret", builder.Configuration["EntraIdApp:ClientSecret"]),
        new KeyValuePair<string, string>("grant_type", "authorization_code"),
        new KeyValuePair<string, string>("code", code),
        new KeyValuePair<string, string>("redirect_uri", $"{context.Request.Scheme}://{redirectDomain}{builder.Configuration["EntraIdApp:CallbackPath"]}"),
    });
    var tokenResponse = await new HttpClient().PostAsync(tokenUrl, tokenRequestBody);
    if (!tokenResponse.IsSuccessStatusCode)
    {
        var reason = await tokenResponse.Content.ReadAsStringAsync();
        app.Logger.LogError($"Failed to exchange authorization code for access token. Status code: {tokenResponse.StatusCode}. Reason: {reason}");
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 400);
    }
    var token = await Helpers.OAuth2.ConvertToTokenResponse(tokenResponse.Content);

    // Put into token store
    // Set the token in Redis for 60 minutes
    connectionMux.GetDatabase().StringSet(gitHubUserId, JsonSerializer.Serialize(token));

    return Results.Text("Authorized and mapped accounts. You can now return to your Copilot Chat.", statusCode: 202);
})
.WithName("PostAuthEntraId");



// Add a new endpoint for GitHub Copilot Extension
app.MapPost("/token", async (HttpContext context, IConnectionMultiplexer connectionMux) =>
{
    // Log the received request body
    using var reader = new StreamReader(context.Request.Body);
    var requestBody = await reader.ReadToEndAsync();

    // Extract the id_token from the request body
    var formParams = System.Web.HttpUtility.ParseQueryString(requestBody);
    var idToken_string = formParams["subject_token"].ToString();
    app.Logger.LogDebug("IdToken: {IdToken}", idToken_string);

    // Extract the GitHub User ID from the request body
    var idToken = await Helpers.OAuth2.ParseAndValidateIdToken(idToken_string,
                                                               builder.Configuration["GitHubApp:ClientId"],
                                                               builder.Configuration["GitHubApp:Issuer"]);

    var gitHubUserId = idToken.Subject;
    if (string.IsNullOrWhiteSpace(gitHubUserId))
    {
        app.Logger.LogError("GitHub User ID is missing.");
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 400);
    }

    app.Logger.LogDebug("Received GitHub User ID: {GitHubUserId}", gitHubUserId);
    // Retrieve the access token from the token store
    var tokenString = await connectionMux.GetDatabase().StringGetAsync(gitHubUserId);
    if (!tokenString.HasValue)
    {
        app.Logger.LogError("Access token not found for GitHub User ID: {GitHubUserId}", gitHubUserId);

        // Return Successful HTTP Status, otherwise user will be stuck and can't initiate re-authentication by themselves
        // chat message endpoint will check for valid entra token and if non provided asks user to go through authentication process again.
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 200);
    }
    var retrievedObject = tokenString.ToString() ?? throw new InvalidOperationException("Token is null");
    var token = JsonSerializer.Deserialize<OAuth2TokenResponse>(retrievedObject) ?? throw new InvalidOperationException("Deserialized token is null");
    
    // If token expired initiate re-authentication 
    // Improvement use Refresh Token to get new access token
    if (Helpers.OAuth2.IsTokenExpired(token))
    {
        app.Logger.LogError("Access token expired for GitHub User ID: {GitHubUserId}", gitHubUserId);
        // Return Successful HTTP Status, otherwise user will be stuck and can't initiate re-authentication by themselves
        // chat message endpoint will check for valid entra token and if non provided asks user to go through authentication process again.
        return Results.Json(new
        {
            error = "invalid_request"
        }, statusCode: 200);
    }

    //Return a response with the received data
    return Results.Json(new
    {
        access_token = token.AccessToken,
        token_type = "Bearer",
        issued_token_type = "urn:ietf:params:oauth:token-type:access_token",
        expires_in = 60, // for testing purposes, set to 60 seconds
    }, statusCode: 200);
})
.WithName("PostTokenExchange");

app.Run();