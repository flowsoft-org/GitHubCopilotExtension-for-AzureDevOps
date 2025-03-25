using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddAzureRedis("tokenCache");

if (builder.Environment.IsDevelopment())
{
    redis.RunAsContainer();
}

builder.AddProject<Projects.AuthService>("authservice")
       .WithReference(redis);

builder.AddProject<Projects.Api>("api");

builder.Build().Run();
