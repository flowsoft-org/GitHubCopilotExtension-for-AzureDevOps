# GitHubCopilotExtension-for-AzureDevOps

A GitHub Copilot Extension for retrieving data from Azure DevOps.

---

## Deployment Workflow (IMPORTANT)

**Follow these steps in order for a successful setup:**

1. **Deploy the infrastructure and app first** to Azure. This will provision the required resources (including Azure Key Vault) and provide the public domain names for your container apps.
2. **Register your GitHub App and Microsoft Entra ID (Azure AD) App** using the public domain names obtained from the deployment. (You cannot complete app registration until you have the deployed domain names.)
3. **Add your client secrets to the deployed Azure Key Vault.** This ensures secrets are securely managed and available to your services.

---

## Overview
This project enables GitHub Copilot to interact with Azure DevOps, allowing users to retrieve work item data and perform other DevOps-related queries directly from Copilot chat. It is built using .NET Aspire, Azure Container Apps, and supports secure authentication via GitHub and Microsoft Entra ID (formerly Azure AD).

## Prerequisites
- Azure Subscription
- Azure CLI and azd (Azure Developer CLI)
- .NET 9 SDK
- GitHub account (to create a GitHub App)
- Microsoft Entra ID tenant (to create an Entra App Registration)

## Required Applications

### 0. Azure Deployment

> **Reminder:** Deploy the infra and services first, then register your apps, then add secrets to Key Vault as described above.

1. Synthesize infrastructure (optional):
   ```bash
   azd infra synth
   ```
2. Deploy to Azure:
   ```bash
   azd up
   ```

### 1. GitHub App
You must create a GitHub App to enable OAuth authentication for users. This app is used to authenticate users and obtain their GitHub identity.

**Steps:**
1. Go to [GitHub Developer Settings > GitHub Apps](https://github.com/settings/apps) and click "New GitHub App".
2. Set the following:
   - **App name:** (e.g. Copilot Azure DevOps Extension)
   - **Homepage URL:** Your deployed app's URL (e.g. `https://<your-app-domain>`)
   - **Callback URL:** `https://<your-app-domain>/postauth-github`
   - **Permissions:** Account permissions `Copilot Chat` and `Copilot Editor Context` as `read-only`
   - **OAuth Authorization callback URL:** `https://<your-app-domain>/postauth-github`
3. Save the app and generate a **Client ID** and **Client Secret**.
4. Copy these values into your configuration (see below).

### 2. Microsoft Entra ID (Azure AD) App Registration
You must create an Entra ID (Azure AD) App Registration to allow the extension to access Azure DevOps resources on behalf of the user.

**Steps:**
1. Go to [Azure Portal > Microsoft Entra ID > App registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade) and click "New registration".
2. Set the following:
   - **Name:** (e.g. Copilot Azure DevOps Extension)
   - **Supported account types:** Single tenant or multi-tenant as needed
   - **Redirect URI:** `https://<your-app-domain>/postauth-entra`
3. After creation, go to **Certificates & secrets** and create a new **Client Secret**.
4. Go to **API permissions** and add:
   - `User.Read`
   - `offline_access`
   - `https://app.vssps.visualstudio.com/user_impersonation` (Azure DevOps)
5. Copy the **Application (client) ID**, **Directory (tenant) ID**, and **Client Secret** into your configuration.

## Configuration

> **Note:** After deploying to Azure, you must add your GitHub and Entra ID client secrets to the deployed Azure Key Vault. See instructions below.

You must provide the following configuration values (via environment variables, `appsettings.Development.json`, or user-secrets):

- **GitHubApp:ClientId**: GitHub App Client ID
- **GitHubApp:ClientSecret**: GitHub App Client Secret
- **GitHubApp:Instance**: `https://github.com/login/oauth/`
- **GitHubApp:CallbackPath**: `/postauth-github`
- **GitHubApp:AppAuthDomain**: Your app's public domain
- **GitHubApp:Issuer**: `https://github.com/login/oauth`
- **EntraIdApp:ClientId**: Entra App Registration Client ID
- **EntraIdApp:ClientSecret**: Entra App Registration Client Secret
- **EntraIdApp:Instance**: `https://login.microsoftonline.com/`
- **EntraIdApp:TenantId**: Your Entra tenant ID
- **EntraIdApp:CallbackPath**: `/postauth-entra`
- **EntraIdApp:AppAuthDomain**: Your app's public domain
- **EntraIdApp:Domain**: Your Entra domain

You can set these using [dotnet user-secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for local development:

```bash
cd ./src/AuthService/
dotnet user-secrets init
dotnet user-secrets set "EntraIdApp:ClientSecret" "your-client-secret"
dotnet user-secrets set "GitHubApp:ClientSecret" "your-github-client-secret"
```

## Adding Secrets to Azure Key Vault (after deployment)

After running `azd up`, locate the name of your deployed Key Vault (check the Azure Portal or output from deployment). Add your secrets using the Azure CLI:

```bash
# Replace <keyvault-name> and <your-secret-value> accordingly
az keyvault secret set --vault-name <keyvault-name> --name "GitHubApp--ClientSecret" --value "<your-github-client-secret>"
az keyvault secret set --vault-name <keyvault-name> --name "EntraIdApp--ClientSecret" --value "<your-entra-client-secret>"
```

- The secret names use double dashes (`--`) to match configuration binding (e.g., `GitHubApp--ClientSecret`).
- Repeat for any other secrets required by your configuration.

Your services will automatically retrieve these secrets from Key Vault at runtime.

## Azure Deployment

> **Reminder:** This step is needed to update the configuration

2. Re-Deploy to Azure:
   ```bash
   azd up
   ```

## Local Development

1. Trust developer certificates:
   ```bash
   dotnet dev-certs https --trust
   ```
1. Run the app:
   ```bash
   dotnet run --project ./src/AppHost/
   # or
   dotnet watch run --project ./src/AppHost/
   ```
1. Allow **public access** to API and AuthService endpoints in your **Codespace**
1. Add another **GitHub App** for local development with authentication and api URLs pointing to your codespace
1. Add an additional `Redirect URI` to your `Entra ID Application` with the Codespaces Domain

More information on local development is in the [CONTRIBUTING Guide](./CONTRIBUTING.md)


## Authentication Flow
- User clicks "Sign in with GitHub" in the Copilot extension.
- User is redirected to GitHub for OAuth consent.
- After consent, user is redirected to `/postauth-github`.
- The app exchanges the code for a GitHub access token and retrieves the GitHub user ID.
- User is then redirected to Microsoft Entra ID for Azure DevOps consent.
- After consent, user is redirected to `/postauth-entra`.
- The app exchanges the code for an Azure DevOps access token and stores it for the GitHub user.
- Copilot extension can now access Azure DevOps data on behalf of the user.

## Contributing
See [CONTRIBUTING.md](CONTRIBUTING.md) for local setup and development instructions.

## License
MIT License
