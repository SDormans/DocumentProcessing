// Production environment for DocumentProcessing on Azure Container Apps.
// Validate: bicep build infra/main.bicep  /  infra/deploy.sh --what-if-only (az deployment group what-if).
// CI never runs this template; cd.yml only updates the images of the apps it creates. See docs/deployment.md.
targetScope = 'resourceGroup'

@description('Short prefix for resource names. cd.yml expects the default app names unless AZURE_CONTAINERAPP_* variables are set.')
param namePrefix string = 'documentprocessing'
param location string = resourceGroup().location

@description('Image tag published by cd.yml (sha-<commit> or latest). Pass the currently deployed tag when re-running the template.')
param imageTag string = 'latest'
param imageRegistry string = 'ghcr.io/sdormans/documentprocessing'
@description('GitHub user and a token with read:packages, only needed while the GHCR packages are private.')
param registryUsername string = ''
@secure()
param registryPassword string = ''

@description('Address space of the virtual network. The Container Apps subnet is also the only network the API trusts X-Forwarded-* headers from.')
param vnetAddressPrefix string = '10.40.0.0/16'
param appsSubnetPrefix string = '10.40.0.0/23'
param postgresSubnetPrefix string = '10.40.2.0/27'

@description('PostgreSQL administrator login. Only the db-init job uses it; the apps connect as appDbUser.')
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

@allowed(['Fake', 'OpenAI', 'Anthropic', 'None'])
@description('Cloud model provider (Ai:Cloud:Provider). OpenAI also covers Azure OpenAI.')
param aiCloudProvider string = 'OpenAI'
param aiCloudEndpoint string = 'https://api.openai.com/v1/'
param aiCloudModel string = 'gpt-4o-mini'
@description('Header for the cloud key: Authorization (OpenAI), api-key (Azure OpenAI). Ignored for Anthropic.')
param aiCloudApiKeyHeader string = 'Authorization'
@secure()
param aiCloudApiKey string = ''

@allowed(['None', 'OpenAI', 'Fake'])
@description('Local model provider (Ai:Local:Provider). OpenAI means any OpenAI-compatible server (Ollama, llama-server) reachable from the virtual network.')
param aiLocalProvider string = 'None'
param aiLocalEndpoint string = ''
param aiLocalModel string = ''

@allowed(['Cloud', 'Local'])
param aiDefaultModelType string = 'Cloud'
@allowed(['None', 'Automatic'])
param aiFallbackMode string = 'None'

@allowed(['None', 'ClamAv'])
@description('ClamAv adds a clamd sidecar to the worker. Takes effect once the security milestone (malware scanning) is merged.')
param malwareScanner string = 'None'
@description('Lock admin actions until the admin signs in with an authenticator code. Takes effect once the security milestone is merged.')
param requireAdminMfa bool = false

@description('Email for alerts. Empty skips the action group and alert rules.')
param alertEmail string = ''

var suffix = uniqueString(resourceGroup().id)
var storageName = take('dp${suffix}', 24)
var dbName = 'documentprocessing'
var useRegistryCredentials = !empty(registryPassword)
var registrySecrets = useRegistryCredentials ? [{ name: 'registry-password', value: registryPassword }] : []
var registries = useRegistryCredentials
  ? [{ server: split(imageRegistry, '/')[0], username: registryUsername, passwordSecretRef: 'registry-password' }]
  : []

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

// Network: the apps and PostgreSQL share a virtual network, so the database has no public endpoint.
resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${namePrefix}-vnet'
  location: location
  properties: {
    addressSpace: { addressPrefixes: [vnetAddressPrefix] }
    subnets: [
      {
        name: 'apps'
        properties: {
          addressPrefix: appsSubnetPrefix
          delegations: [{ name: 'apps', properties: { serviceName: 'Microsoft.App/environments' } }]
        }
      }
      {
        name: 'postgres'
        properties: {
          addressPrefix: postgresSubnetPrefix
          delegations: [{ name: 'postgres', properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' } }]
        }
      }
    ]
  }
}

resource postgresDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: '${namePrefix}.private.postgres.database.azure.com'
  location: 'global'
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: postgresDns
  name: '${namePrefix}-vnet'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: { id: vnet.id }
  }
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
    network: {
      delegatedSubnetResourceId: vnet.properties.subnets[1].id
      privateDnsZoneArmResourceId: postgresDns.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [postgresDnsLink]
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: dbName
}

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: vnet.properties.subnets[0].id
      internal: false
    }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
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

// Shared by the API (which offers, validates and probes the models) and the worker (which calls them).
var aiEnv = [
  { name: 'Ai__Cloud__Provider', value: aiCloudProvider }
  { name: 'Ai__Cloud__Endpoint', value: aiCloudEndpoint }
  { name: 'Ai__Cloud__ModelName', value: aiCloudModel }
  { name: 'Ai__Cloud__ApiKeyHeader', value: aiCloudApiKeyHeader }
  { name: 'Ai__Cloud__ApiKey', secretRef: 'ai-cloud-key' }
  { name: 'Ai__Local__Provider', value: aiLocalProvider }
  { name: 'Ai__Local__Endpoint', value: aiLocalEndpoint }
  { name: 'Ai__Local__ModelName', value: aiLocalModel }
  { name: 'Ai__DefaultModelType', value: aiDefaultModelType }
  { name: 'Ai__Fallback__Mode', value: aiFallbackMode }
]
var commonEnv = concat(aiEnv, [
  { name: 'ConnectionStrings__DefaultConnection', secretRef: 'db' }
  { name: 'FileStorage__Provider', value: 'AzureBlob' }
  { name: 'FileStorage__AzureBlob__ServiceUri', value: storage.properties.primaryEndpoints.blob }
  { name: 'FileStorage__AzureBlob__ManagedIdentityClientId', value: identity.properties.clientId }
  { name: 'FileStorage__AzureBlob__ContainerName', value: 'documents' }
  { name: 'Logging__Console__FormatterName', value: 'json' }
])
// Container Apps rejects empty secret values. infra/deploy.sh refuses to deploy OpenAI/Anthropic without a key.
var commonSecrets = concat(registrySecrets, [
  { name: 'db', value: connectionString }
  { name: 'ai-cloud-key', value: empty(aiCloudApiKey) ? 'unset' : aiCloudApiKey }
])

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-api'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      ingress: { external: false, targetPort: 8080, transport: 'auto' }
      registries: registries
      secrets: concat(commonSecrets, [{ name: 'admin-password', value: bootstrapAdminPassword }])
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${imageRegistry}/api:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(commonEnv, [
            { name: 'Authentication__Bootstrap__Username', value: bootstrapAdminUserName }
            { name: 'Authentication__Bootstrap__Role', value: 'Admin' }
            { name: 'Authentication__Bootstrap__Email', value: bootstrapAdminEmail }
            { name: 'Authentication__Bootstrap__Password', secretRef: 'admin-password' }
            { name: 'Authentication__RequireAdminMfa', value: string(requireAdminMfa) }
            { name: 'Authentication__CookieSecurePolicy', value: 'Always' }
            // The Container Apps ingress (in the apps subnet) terminates TLS and forwards to the API.
            { name: 'ForwardedHeaders__KnownNetworks__0', value: appsSubnetPrefix }
            { name: 'DataProtection__BlobUri', value: '${storage.properties.primaryEndpoints.blob}dataprotection/keys.xml' }
          ])
          probes: [
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 } }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 } }
            // Migrations and database connection retries run before the app listens.
            { type: 'Startup', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 10, failureThreshold: 30 }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
  dependsOn: [blobRole, database]
}

var workerContainer = {
  name: 'worker'
  image: '${imageRegistry}/worker:${imageTag}'
  resources: { cpu: json('1.0'), memory: '2Gi' }
  env: concat(commonEnv, [
    { name: 'MalwareScanning__Provider', value: malwareScanner }
    { name: 'MalwareScanning__ClamAvHost', value: 'localhost' }
    { name: 'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', value: 'Warning' }
  ])
}
// clamd sidecar; the worker retries a document until the signature database has loaded.
var clamavContainer = {
  name: 'clamav'
  image: 'docker.io/clamav/clamav:1.4'
  resources: { cpu: json('1.0'), memory: '2Gi' }
}

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-worker'
  location: location
  identity: { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }
  properties: {
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      registries: registries
      secrets: commonSecrets
    }
    template: {
      containers: malwareScanner == 'ClamAv' ? [workerContainer, clamavContainer] : [workerContainer]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
  dependsOn: [blobRole, api]
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: '${namePrefix}-web'
  location: location
  properties: {
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      registries: registries
      secrets: registrySecrets
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

// Creates (or re-passwords) the application login. Run once after the first deployment and after rotating
// appDbPassword: az containerapp job start -g <rg> -n <namePrefix>-db-init (infra/deploy.sh does this).
var dbInitScript = '''
set -eu
psql --set=ON_ERROR_STOP=1 --set=app_user="$APP_DB_USER" --set=app_password="$APP_DB_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE %I LOGIN', :'app_user') WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'app_user') \gexec
SELECT format('ALTER ROLE %I WITH LOGIN PASSWORD %L', :'app_user', :'app_password') \gexec
SELECT format('GRANT USAGE, CREATE ON SCHEMA public TO %I', :'app_user') \gexec
SQL
echo "Application login $APP_DB_USER is ready."
'''

resource dbInit 'Microsoft.App/jobs@2024-03-01' = {
  name: '${namePrefix}-db-init'
  location: location
  properties: {
    environmentId: env.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 300
      replicaRetryLimit: 1
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      secrets: [
        { name: 'admin-password', value: postgresAdminPassword }
        { name: 'app-password', value: appDbPassword }
      ]
    }
    template: {
      containers: [
        {
          name: 'db-init'
          image: 'docker.io/library/postgres:16-alpine'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          command: ['/bin/sh', '-c', dbInitScript]
          env: [
            { name: 'PGHOST', value: postgres.properties.fullyQualifiedDomainName }
            { name: 'PGDATABASE', value: dbName }
            { name: 'PGUSER', value: postgresAdminLogin }
            { name: 'PGPASSWORD', secretRef: 'admin-password' }
            { name: 'PGSSLMODE', value: 'require' }
            { name: 'APP_DB_USER', value: appDbUser }
            { name: 'APP_DB_PASSWORD', secretRef: 'app-password' }
          ]
        }
      ]
    }
  }
  dependsOn: [database]
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
  {
    name: 'restarts'
    description: 'Container restarts (crash loops, failed probes) in the last 15 minutes'
    query: 'ContainerAppSystemLogs_CL | where ContainerAppName_s startswith "${namePrefix}-" | where Reason_s in ("ContainerTerminated", "ProbeFailed", "BackOff")'
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
output apiApp string = api.name
output workerApp string = worker.name
output webApp string = web.name
output dbInitJob string = dbInit.name
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output storageAccount string = storage.name
