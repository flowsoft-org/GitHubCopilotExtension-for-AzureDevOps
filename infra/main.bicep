targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention, the name of the resource group for your application will use this name, prefixed with rg-')
param environmentName string

@minLength(1)
@description('The location used for all deployed resources')
param location string

@description('Id of the user or app to assign application roles')
param principalId string = ''

param ENTRAIDAPP_APPAUTHDOMAIN string = ''
param ENTRAIDAPP_CALLBACKPATH string = '/postauth-entra'
param ENTRAIDAPP_CLIENTID string
param ENTRAIDAPP_DOMAIN string = ''
param ENTRAIDAPP_INSTANCE string = 'https://login.microsoftonline.com/'
param ENTRAIDAPP_TENANTID string
param GITHUBAPP_APPAUTHDOMAIN string = ''
param GITHUBAPP_CALLBACKPATH string = '/postauth-github'
param GITHUBAPP_CLIENTID string
param GITHUBAPP_CLIENTID_DEV string = ''
param GITHUBAPP_INSTANCE string = 'https://github.com/login/oauth/'
param GITHUBAPP_ISSUER string = 'https://github.com/login/oauth'

var tags = {
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}
module resources 'resources.bicep' = {
  scope: rg
  name: 'resources'
  params: {
    location: location
    tags: tags
    principalId: principalId
  }
}

module secrets 'secrets/secrets.module.bicep' = {
  name: 'secrets'
  scope: rg
  params: {
    location: location
    principalId: resources.outputs.MANAGED_IDENTITY_PRINCIPAL_ID
    principalType: 'ServicePrincipal'
  }
}
module tokenCache 'tokenCache/tokenCache.module.bicep' = {
  name: 'tokenCache'
  scope: rg
  params: {
    location: location
    principalId: resources.outputs.MANAGED_IDENTITY_PRINCIPAL_ID
    principalName: resources.outputs.MANAGED_IDENTITY_NAME
  }
}

output MANAGED_IDENTITY_CLIENT_ID string = resources.outputs.MANAGED_IDENTITY_CLIENT_ID
output MANAGED_IDENTITY_NAME string = resources.outputs.MANAGED_IDENTITY_NAME
output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = resources.outputs.AZURE_LOG_ANALYTICS_WORKSPACE_NAME
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = resources.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID
output AZURE_CONTAINER_REGISTRY_NAME string = resources.outputs.AZURE_CONTAINER_REGISTRY_NAME
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = resources.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_NAME
output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = resources.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID
output AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = resources.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN
output SECRETS_VAULTURI string = secrets.outputs.vaultUri
output TOKENCACHE_CONNECTIONSTRING string = tokenCache.outputs.connectionString
