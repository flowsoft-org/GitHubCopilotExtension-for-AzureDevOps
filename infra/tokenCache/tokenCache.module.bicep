@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param principalId string

param principalName string

resource tokenCache 'Microsoft.Cache/redis@2024-03-01' = {
  name: take('tokenCache-${uniqueString(resourceGroup().id)}', 63)
  location: location
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 1
    }
    enableNonSslPort: false
    disableAccessKeyAuthentication: true
    minimumTlsVersion: '1.2'
    redisConfiguration: {
      'aad-enabled': 'true'
    }
  }
  tags: {
    'aspire-resource-name': 'tokenCache'
  }
}

resource tokenCache_contributor 'Microsoft.Cache/redis/accessPolicyAssignments@2024-03-01' = {
  name: take('tokencachecontributor${uniqueString(resourceGroup().id)}', 24)
  properties: {
    accessPolicyName: 'Data Contributor'
    objectId: principalId
    objectIdAlias: principalName
  }
  parent: tokenCache
}

output connectionString string = '${tokenCache.properties.hostName},ssl=true'