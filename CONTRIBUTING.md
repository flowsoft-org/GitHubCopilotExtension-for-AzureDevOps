
Developer certificates:
```bash
dotnet dev-certs https --trust
```

Setup Secrets:

```bash
dotnet user-secrets init
dotnet user-secrets set "EntraIdApp:ClientSecret" "your-client-secret"
dotnet user-secrets set "GitHubApp:ClientSecret" "your-github-client-secret"
```

Local execution:
```bash
dotnet run --project src/AppHost/
```

Set up GitHub App
