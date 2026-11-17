// Production environment for DocumentProcessing on Azure Container Apps.
// Validate:  bicep build infra/main.bicep   /   az deployment group what-if -g <rg> -f infra/main.bicep -p infra/main.parameters.json
// Nothing here is deployed by CI; cd.yml only updates images of apps this template created.
targetScope = 'resourceGroup'

@description('Short prefix for resource names.')
param namePrefix string = 'documentprocessing'
param location string = resourceGroup().location

@description('Image tag published by cd.yml (sha-<commit> or latest).')
param imageTag string = 'latest'
param imageRegistry string = 'ghcr.io/sdormans/documentprocessing'

@description('PostgreSQL administrator login (used only for setup; the app uses appDbUser).')
param postgresAdminLogin string = 'pgadmin'
@secure()
param postgresAdminPassword string
param appDbUser string = 'documentprocessing_app'
@secure()
param appDbPassword string

param bootstrapAdminUserName string = 'admin'
param bootstrapAdminEmail string
@secure()
param bootstrapAdminPassword string

@allowed(['None', 'ClamAv'])
param malwareScanner string = 'ClamAv'
@description('AI provider for the worker: Fake or OpenAI.')
param aiProvider string = 'OpenAI'
param aiEndpoint string = 'https://api.openai.com/v1/'
param aiModel string = 'gpt-4o-mini'
@secure()
param aiApiKey string = ''

@description('Email for alerts. Empty skips the action group.')
param alertEmail string = ''

var suffix = uniqueString(resourceGroup().id)
var storageName = take('dp${suffix}', 24)
var dbName = 'documentprocessing'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id'
  location: location
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_ZRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 14 }
    containerDeleteRetentionPolicy: { enabled: true, days: 14 }
    isVersioningEnabled: false
  }
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'documents'
}

resource keysContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'dataprotection'
}

// Backstop for the application's retention purge (7 years + margin).
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-expired-documents'
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['documents/'] }
            actions: { baseBlob: { delete: { daysAfterModificationGreaterThan: 2600 } } }
          }
        }
      ]
    }
  }
}

var blobDataContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
resource blobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, identity.id, blobDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: blobDataContributor
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${namePrefix}-pg-${suffix}'
  location: location
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: 35, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: dbName
}

// Lets Azure services (the Container Apps environment) reach the server. Use VNet integration for stricter isolation.
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var connectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${dbName};Username=${appDbUser};Password=${appDbPassword};SSL Mode=Require'
var blobEnv = [
  { name: 'FileStorage__Provider', value: 'AzureBlob' }
  { name: 'FileStorage__AzureBlob__ServiceUri', value: storage.properties.primaryEndpoints.blob }
  { name: 'FileStorage__AzureBlob__ManagedIdentityClientId', value: identity.properties.clientId }
  { name: 'FileStorage__AzureBlob__ContainerName', value: 'documents' }
  { name: 'Logging__Console__FormatterName', value: 'json' }
]

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      secrets: [
        { name: 'db', value: connectionString }
        { name: 'admin-password', value: bootstrapAdminPassword }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${imageRegistry}/api:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(blobEnv, [
            { name: 'ConnectionStrings__DefaultConnection', secretRef: 'db' }
            { name: 'Authentication__Bootstrap__Username', value: bootstrapAdminUserName }
            { name: 'Authentication__Bootstrap__Role', value: 'Admin' }
            { name: 'Authentication__Bootstrap__Email', value: bootstrapAdminEmail }
            { name: 'Authentication__Bootstrap__Password', secretRef: 'admin-password' }
            { name: 'Authentication__RequireAdminMfa', value: 'true' }
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'DataProtection__BlobUri', value: '${storage.properties.primaryEndpoints.blob}dataprotection/keys.xml' }
          ])
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 } }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
  dependsOn: [blobRole]
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-worker'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      secrets: [
        { name: 'db', value: connectionString }
        { name: 'ai-key', value: empty(aiApiKey) ? 'unset' : aiApiKey }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${imageRegistry}/worker:${imageTag}'
          resources: { cpu: json('1.0'), memory: '2Gi' }
          env: concat(blobEnv, [
            { name: 'ConnectionStrings__DefaultConnection', secretRef: 'db' }
            { name: 'Ai__Provider', value: aiProvider }
            { name: 'Ai__Endpoint', value: aiEndpoint }
            { name: 'Ai__ModelName', value: aiModel }
            { name: 'Ai__ApiKey', secretRef: 'ai-key' }
            { name: 'MalwareScanning__Provider', value: malwareScanner }
            { name: 'MalwareScanning__ClamAvHost', value: 'localhost' }
          ])
        }
        // clamd sidecar; the worker retries until its signature database has loaded.
        {
          name: 'clamav'
          image: 'docker.io/clamav/clamav:1.4'
          resources: { cpu: json('0.5'), memory: '1Gi' }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
  dependsOn: [blobRole]
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-web'
  location: location
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${imageRegistry}/web:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'API_UPSTREAM', value: 'https://${api.properties.configuration.ingress.fqdn}' }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (!empty(alertEmail)) {
  name: '${namePrefix}-alerts'
  location: 'global'
  properties: {
    groupShortName: 'docproc'
    enabled: true
    emailReceivers: [{ name: 'ops', emailAddress: alertEmail, useCommonAlertSchema: true }]
  }
}

// Log-based alerts over the Container Apps console logs (JSON format).
var alertQueries = [
  {
    name: 'worker-failures'
    description: 'Documents failed or were quarantined in the last 15 minutes'
    query: 'ContainerAppConsoleLogs_CL | where ContainerAppName_s == "${namePrefix}-worker" | where Log_s has_any ("Extraction failed", "quarantined", "MaxAttemptsExceeded")'
  }
  {
    name: 'api-errors'
    description: 'Unhandled API errors in the last 15 minutes'
    query: 'ContainerAppConsoleLogs_CL | where ContainerAppName_s == "${namePrefix}-api" | where Log_s has "\\"LogLevel\\":\\"Error\\""'
  }
]

resource alerts 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = [for a in alertQueries: if (!empty(alertEmail)) {
  name: '${namePrefix}-${a.name}'
  location: location
  properties: {
    description: a.description
    severity: 2
    enabled: true
    scopes: [logs.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: a.query
          timeAggregation: 'Count'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: { numberOfEvaluationPeriods: 1, minFailingPeriodsToAlert: 1 }
        }
      ]
    }
    actions: { actionGroups: empty(alertEmail) ? [] : [actionGroup.id] }
  }
}]

output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output storageAccount string = storage.name
