// ──────────────────────────────────────────────────────────────────────────
// Bicep template: Dlangezwa HS – Azure App Service + SQL + Key Vault
// Deploy: az deployment group create --resource-group <rg> --template-file azure-infra.bicep
// ──────────────────────────────────────────────────────────────────────────

@description('Base name prefix for all resources')
param appName string = 'dlangezwa-hs'

@description('Azure region')
param location string = resourceGroup().location

@description('App Service plan SKU')
@allowed(['F1','B1','B2','S1','P1v3'])
param appServiceSku string = 'B1'

@description('Azure SQL administrator login')
param sqlAdminLogin string = 'sqladmin'

@secure()
@description('Azure SQL administrator password')
param sqlAdminPassword string

// ── App Service Plan ──────────────────────────────────────────────────────
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name:     '${appName}-plan'
  location: location
  sku: {
    name:     appServiceSku
    capacity: 1
  }
  kind: 'app'
  properties: {
    reserved: false  // Windows
  }
}

// ── App Service ───────────────────────────────────────────────────────────
resource webApp 'Microsoft.Web/sites@2023-01-01' = {
  name:     appName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly:    true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      alwaysOn:            appServiceSku != 'F1'
      minTlsVersion:       '1.2'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'KeyVaultUri',            value: keyVault.properties.vaultUri }
      ]
      connectionStrings: [
        {
          name:             'DefaultConnection'
          connectionString: 'Server=${sqlServer.properties.fullyQualifiedDomainName};Database=${sqlDatabase.name};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;'
          type:             'SQLAzure'
        }
      ]
    }
  }
}

// ── Azure SQL Server ──────────────────────────────────────────────────────
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name:     '${appName}-sql'
  location: location
  properties: {
    administratorLogin:         sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion:          '1.2'
  }
}

resource sqlFirewallAzureServices 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name:   'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress:   '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name:   '${appName}-db'
  location: location
  sku: {
    name:     'Basic'
    tier:     'Basic'
    capacity: 5
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

// ── Key Vault ─────────────────────────────────────────────────────────────
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name:     '${appName}-kv'
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId:              tenant().tenantId
    enableRbacAuthorization: true
    enableSoftDelete:      true
    softDeleteRetentionInDays: 7
  }
}

// Grant App Service identity access to Key Vault secrets (read)
resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name:  guid(keyVault.id, webApp.id, 'KeyVaultSecretsUser')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId:      webApp.identity.principalId
    principalType:    'ServicePrincipal'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────
output webAppUrl      string = 'https://${webApp.properties.defaultHostName}'
output keyVaultUri    string = keyVault.properties.vaultUri
output sqlServerFqdn  string = sqlServer.properties.fullyQualifiedDomainName
