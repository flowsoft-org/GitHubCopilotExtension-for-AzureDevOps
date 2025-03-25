var builder = DistributedApplication.CreateBuilder(args);

var authService = builder.AddProject<Projects.AuthService>("authservice");

builder.AddProject<Projects.Api>("api");

builder.Build().Run();
