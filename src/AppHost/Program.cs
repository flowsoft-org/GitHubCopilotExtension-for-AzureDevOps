var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Api>("api");

builder.AddProject<Projects.AuthService>("authservice");

builder.Build().Run();
