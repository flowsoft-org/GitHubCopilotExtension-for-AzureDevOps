using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddAzureRedis("tokenCache");

if (builder.Environment.IsDevelopment())
{
    redis.RunAsContainer();
} 

// Add Azure Key Vault for secrets
var keyVault = builder.AddAzureKeyVault("secrets");

// Auth Service
var authService = builder.AddProject<Projects.AuthService>("authservice")
       .WithReference(redis)
       .WithReference(keyVault)
       .WithEnvironment("Logging__LogLevel__Default", "Debug")
       .WithExternalHttpEndpoints();

ApplyEnvironmentVariables(authService);

// Api Service
var api = builder.AddProject<Projects.Api>("api")
       .WithEnvironment("AppAuthDomain", "tobefilledlater")
       .WithEnvironment("Logging__LogLevel__Default", "Debug")
       .WithExternalHttpEndpoints();

builder.Build().Run();

// Helper method to apply environment variables to a service
void ApplyEnvironmentVariables(IResourceBuilder<ProjectResource> service)
{
    var EntraIdApp_Instance = builder.AddParameter("ENTRAIDAPP-INSTANCE", "https://login.microsoftonline.com/");
    var EntraIdApp_Domain = builder.AddParameter("ENTRAIDAPP-DOMAIN");
    var EntraIdApp_TenantId = builder.AddParameter("ENTRAIDAPP-TENANTID");
    var EntraIdApp_ClientId = builder.AddParameter("ENTRAIDAPP-CLIENTID");
    var EntraIdApp_CallbackPath = builder.AddParameter("ENTRAIDAPP-CALLBACKPATH", "/postauth-entra");
    var EntraIdApp_AppAuthDomain = builder.AddParameter("ENTRAIDAPP-APPAUTHDOMAIN");
    service.WithEnvironment("ENTRAIDAPP__INSTANCE", EntraIdApp_Instance);
    service.WithEnvironment("ENTRAIDAPP__DOMAIN", EntraIdApp_Domain);
    service.WithEnvironment("ENTRAIDAPP__TENANTID", EntraIdApp_TenantId);
    service.WithEnvironment("ENTRAIDAPP__CLIENTID", EntraIdApp_ClientId);
    service.WithEnvironment("ENTRAIDAPP__CALLBACKPATH", EntraIdApp_CallbackPath);
    service.WithEnvironment("ENTRAIDAPP__APPAUTHDOMAIN", EntraIdApp_AppAuthDomain);

    var GitHubApp_Instance = builder.AddParameter("GITHUBAPP-INSTANCE");
    var GitHubApp_Issuer = builder.AddParameter("GITHUBAPP-ISSUER");
    var GitHubApp_ClientId = builder.AddParameter("GITHUBAPP-CLIENTID");
    var GitHubApp_ClientId_Dev = builder.AddParameter("GITHUBAPP-CLIENTID-DEV");
    var GitHubApp_CallbackPath = builder.AddParameter("GITHUBAPP-CALLBACKPATH", "/postauth-github");
    var GitHubApp_AppAuthDomain = builder.AddParameter("GITHUBAPP-APPAUTHDOMAIN");
    service.WithEnvironment("GITHUBAPP__INSTANCE", GitHubApp_Instance);
    service.WithEnvironment("GITHUBAPP__ISSUER", GitHubApp_Issuer);
    service.WithEnvironment("GITHUBAPP__CLIENTID", GitHubApp_ClientId);
    service.WithEnvironment("GITHUBAPP__CLIENTID__DEV", GitHubApp_ClientId_Dev);
    service.WithEnvironment("GITHUBAPP__CALLBACKPATH", GitHubApp_CallbackPath);
    service.WithEnvironment("GITHUBAPP__APPAUTHDOMAIN", GitHubApp_AppAuthDomain);
}
