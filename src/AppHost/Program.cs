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
       .WithExternalHttpEndpoints();
ApplyDotEnvVariables(authService);

// Api Service
var api = builder.AddProject<Projects.Api>("api")
       .WithExternalHttpEndpoints();

builder.Build().Run();

// Helper method to load environment variables from .env file
Dictionary<string, string> LoadEnvironmentVariables()
{
    string envFilePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env");
    if (File.Exists(envFilePath))
    {
        var environmentVariables = new Dictionary<string, string>();
        
        foreach (var line in File.ReadAllLines(envFilePath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                continue;

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                continue;

            var key = parts[0].Trim();
            var value = parts[1].Trim();

            // Store in dictionary to avoid duplicates
            environmentVariables[key] = value;
        }

        // Log loaded variables (excluding sensitive data)
        Console.WriteLine($"Loaded {environmentVariables.Count} environment variables from .env file");
        return environmentVariables;
    }
    else
    {
        Console.WriteLine("Warning: .env file not found at " + envFilePath);
        return new Dictionary<string, string>();
    }
}

// Helper method to apply environment variables to a service
void ApplyDotEnvVariables(IResourceBuilder<ProjectResource> service)
{
    // Get all environment variables
    var envVars = LoadEnvironmentVariables();
    
    foreach (var key in envVars.Keys)
    {
        string keyString = key.ToString();
        string valueString = envVars[key]?.ToString() ?? string.Empty;

        // Skip certain values
        if (!keyString.StartsWith("ENTRAIDAPP__APPAUTHDOMAIN") && 
            !keyString.StartsWith("GITHUBAPP__APPAUTHDOMAIN") && 
            !string.IsNullOrEmpty(valueString))
        {
            // Apply each environment variable to the service
            service.WithEnvironment(keyString, valueString);
        }
    }

    // Ensure configuration is applied for specific service types
    if (service.Resource.Name == "authservice")
    {
        // Ensure auth-specific variables are set
        EnsureEnvironmentVariable(service, "ENTRAIDAPP__INSTANCE");
        EnsureEnvironmentVariable(service, "ENTRAIDAPP__DOMAIN");
        EnsureEnvironmentVariable(service, "ENTRAIDAPP__TENANTID");
        EnsureEnvironmentVariable(service, "ENTRAIDAPP__CLIENTID");
        EnsureEnvironmentVariable(service, "ENTRAIDAPP__CALLBACKPATH");

        EnsureEnvironmentVariable(service, "GITHUBAPP__INSTANCE");
        EnsureEnvironmentVariable(service, "GITHUBAPP__ISSUER");
        EnsureEnvironmentVariable(service, "GITHUBAPP__CLIENTID");
        EnsureEnvironmentVariable(service, "GITHUBAPP__CALLBACKPATH");
    }
}

// Helper method to ensure critical environment variables are set
void EnsureEnvironmentVariable(IResourceBuilder<ProjectResource> service, string variableName)
{
    var value = Environment.GetEnvironmentVariable(variableName);
    if (string.IsNullOrEmpty(value))
    {
        Console.WriteLine($"Warning: Critical environment variable {variableName} is not set");
    }
}
