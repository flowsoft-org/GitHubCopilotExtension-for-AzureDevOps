# Contributing

## Local Development Workflow

Follow these steps to set up your environment for local development (including Codespaces):

1. **Deploy infrastructure to Azure (optional for local, required for cloud):**
   - If you want to test with real Azure resources and Key Vault, deploy with `azd up` first. This will give you the public domain names and Key Vault name.
   - For pure local development, you can use your Codespace domain or `localhost` for app registration.
2. **Register a GitHub App for local development:**
   - Go to [GitHub Developer Settings > GitHub Apps](https://github.com/settings/apps) and create a new app.
   - Set the **Homepage URL** and **Callback URL** to your Codespace or localhost domain, e.g.:
     - `https://<your-codespace-id>.<org>.github.dev` or `https://localhost:5001`
     - Callback: `https://<your-codespace-id>.<org>.github.dev/postauth-github`
   - Set permissions as needed (see README).
   - Save the **Client ID** and **Client Secret** for local use.
3. **Register a Microsoft Entra ID (Azure AD) App for local development:**
   - Go to [Azure Portal > Microsoft Entra ID > App registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade) and create a new registration.
   - Set the **Redirect URI** to your Codespace or localhost domain, e.g.:
     - `https://<your-codespace-id>.<org>.github.dev/postauth-entra` or `https://localhost:5001/postauth-entra`
   - Add required API permissions (see README).
   - Save the **Client ID**, **Tenant ID**, and **Client Secret** for local use.
4. **Trust developer certificates:**
   ```bash
   dotnet dev-certs https --trust
   ```
5. **Set up secrets for local development:**
   - Use `dotnet user-secrets` to store secrets for local runs (these are not used in cloud):
   ```bash
   cd ./src/AuthService/
   dotnet user-secrets init
   dotnet user-secrets set "EntraIdApp:ClientSecret" "your-client-secret"
   dotnet user-secrets set "GitHubApp:ClientSecret" "your-github-client-secret"
6. **Run the app locally:**
   ```bash
   dotnet run --project ./src/AppHost/
   # or
   dotnet watch run --project ./src/AppHost/
   ```
7. **Codespaces only:**
   - Allow public access to API and AuthService endpoints in your Codespace.
   - Ensure your app registrations (GitHub and Entra ID) include your Codespace domain in their redirect URIs.

## Cloud Deployment and Key Vault

- For cloud deployment, follow the workflow in the main [README.md](./README.md): deploy infra, register apps with deployed domains, and add secrets to Azure Key Vault.
- Your local `user-secrets` are not used in the cloud; secrets must be set in Key Vault for production.

## Azure infrastructure

Recreate azd infrastructure from Aspire project

```bash
azd infra synth
```

Deployment
```bash
azd up
```


