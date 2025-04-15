using Aspire.Hosting.Azure;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Collections.Generic;
using System.Linq;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddAzureRedis("tokenCache");

if (builder.Environment.IsDevelopment())
{
    redis.RunAsContainer();
}

/* Environment variables and settings */
// Load environment variables from .env file in development
if (builder.Environment.IsDevelopment())
{
    LoadEnvironmentVariables(builder);
}

// Add Azure Key Vault for secrets
var keyVault = builder.AddAzureKeyVault("secrets");

// Configure environment variables for all services
var authService = builder.AddProject<Projects.AuthService>("authservice")
       .WithReference(redis)
       .WithReference(keyVault);

var api = builder.AddProject<Projects.Api>("api")
       .WithReference(keyVault);

// Apply environment variables to all services
ApplyEnvironmentVariables(authService);
ApplyEnvironmentVariables(api);

builder.Build().Run();

// Helper method to load environment variables from .env file
void LoadEnvironmentVariables(IDistributedApplicationBuilder appBuilder)
{
    string envFilePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env");
    if (File.Exists(envFilePath))
    {
        foreach (var line in File.ReadAllLines(envFilePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;

            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}

// Helper method to apply environment variables to a service
void ApplyEnvironmentVariables(IResourceBuilder<ProjectResource> service)
{
    // Get all environment variables 
    var envVars = Environment.GetEnvironmentVariables();
    
    foreach (var key in envVars.Keys)
    {
        string keyString = key.ToString();
        // Skip system and runtime variables
        if (!keyString.StartsWith("SYSTEM") && 
            !keyString.StartsWith("PATH") && 
            !keyString.StartsWith("DOTNET"))
        {
            service.WithEnvironment(keyString, envVars[key].ToString());
        }
    }
}
