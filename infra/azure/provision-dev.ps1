param(
    [string]$SubscriptionId,
    [switch]$PlanOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
$configPath = Join-Path $scriptRoot 'variables.dev.ps1'

. $configPath

if ($SubscriptionId) {
    $AzureInfraConfig.SubscriptionId = $SubscriptionId
}

function Invoke-AzCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$CaptureOutput
    )

    $display = 'az ' + ($Arguments -join ' ')

    if ($PlanOnly) {
        Write-Host "[plan] $display"
        if ($CaptureOutput) {
            return 'planned-value'
        }

        return ''
    }

    Write-Host "[run] $display"

    if ($CaptureOutput) {
        $output = & az @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Azure CLI command failed: $display"
        }

        return ($output -join "`n").Trim()
    }

    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: $display"
    }
}

function Test-AzResourceExists {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    if ($PlanOnly) {
        return $false
    }

    $null = & az @Arguments 2>$null
    return $LASTEXITCODE -eq 0
}

$subscription = $AzureInfraConfig.SubscriptionId
$location = $AzureInfraConfig.Location
$staticWebAppLocation = $AzureInfraConfig.StaticWebAppLocation
$resourceGroupName = $AzureInfraConfig.ResourceGroupName
$logAnalyticsWorkspaceName = $AzureInfraConfig.LogAnalyticsWorkspaceName
$logAnalyticsRetentionDays = [string]$AzureInfraConfig.LogAnalyticsRetentionDays
$logAnalyticsDailyQuotaGb = [string]$AzureInfraConfig.LogAnalyticsDailyQuotaGb
$applicationInsightsName = $AzureInfraConfig.ApplicationInsightsName
$acrName = $AzureInfraConfig.AcrName
$imageRepository = $AzureInfraConfig.ImageRepository
$imageTag = $AzureInfraConfig.ImageTag
$containerAppsEnvironmentName = $AzureInfraConfig.ContainerAppsEnvironmentName
$backendContainerAppName = $AzureInfraConfig.BackendContainerAppName
$backendTargetPort = [string]$AzureInfraConfig.BackendTargetPort
$backendMinReplicas = [string]$AzureInfraConfig.BackendMinReplicas
$backendMaxReplicas = [string]$AzureInfraConfig.BackendMaxReplicas
$managedIdentityName = $AzureInfraConfig.ManagedIdentityName
$staticWebAppName = $AzureInfraConfig.StaticWebAppName
$frontendOrigin = $AzureInfraConfig.FrontendOrigin

if ([string]::IsNullOrWhiteSpace($subscription)) {
    Write-Host "No subscription id supplied. The script will use the current az account subscription."
} else {
    Invoke-AzCli -Arguments @('account', 'set', '--subscription', $subscription)
}

foreach ($provider in @(
    'Microsoft.App',
    'Microsoft.ContainerRegistry',
    'Microsoft.Insights',
    'Microsoft.ManagedIdentity',
    'Microsoft.OperationalInsights',
    'Microsoft.Web'
)) {
    Invoke-AzCli -Arguments @('provider', 'register', '--namespace', $provider)
}

Invoke-AzCli -Arguments @(
    'group', 'create',
    '--name', $resourceGroupName,
    '--location', $location
)

$acrExists = Test-AzResourceExists -Arguments @(
    'acr', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $acrName
)

$identityExists = Test-AzResourceExists -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $managedIdentityName
)

$logAnalyticsWorkspaceExists = Test-AzResourceExists -Arguments @(
    'monitor', 'log-analytics', 'workspace', 'show',
    '--resource-group', $resourceGroupName,
    '--workspace-name', $logAnalyticsWorkspaceName
)

if ($acrExists) {
    Write-Host "ACR already exists: $acrName"
} else {
    Invoke-AzCli -Arguments @(
        'acr', 'create',
        '--resource-group', $resourceGroupName,
        '--name', $acrName,
        '--sku', 'Basic',
        '--admin-enabled', 'false'
    )
}

if ($identityExists) {
    Write-Host "Managed identity already exists: $managedIdentityName"
} else {
    Invoke-AzCli -Arguments @(
        'identity', 'create',
        '--resource-group', $resourceGroupName,
        '--name', $managedIdentityName,
        '--location', $location
    )
}

if ($logAnalyticsWorkspaceExists) {
    Write-Host "Log Analytics workspace already exists: $logAnalyticsWorkspaceName"
} else {
    Invoke-AzCli -Arguments @(
        'monitor', 'log-analytics', 'workspace', 'create',
        '--resource-group', $resourceGroupName,
        '--workspace-name', $logAnalyticsWorkspaceName,
        '--location', $location,
        '--sku', 'PerGB2018',
        '--retention-time', $logAnalyticsRetentionDays,
        '--quota', $logAnalyticsDailyQuotaGb
    )
}

$acrId = Invoke-AzCli -Arguments @(
    'acr', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $acrName,
    '--query', 'id',
    '--output', 'tsv'
) -CaptureOutput

$acrLoginServer = Invoke-AzCli -Arguments @(
    'acr', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $acrName,
    '--query', 'loginServer',
    '--output', 'tsv'
) -CaptureOutput

$identityId = Invoke-AzCli -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $managedIdentityName,
    '--query', 'id',
    '--output', 'tsv'
) -CaptureOutput

$identityPrincipalId = Invoke-AzCli -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $managedIdentityName,
    '--query', 'principalId',
    '--output', 'tsv'
) -CaptureOutput

$logAnalyticsWorkspaceResourceId = Invoke-AzCli -Arguments @(
    'monitor', 'log-analytics', 'workspace', 'show',
    '--resource-group', $resourceGroupName,
    '--workspace-name', $logAnalyticsWorkspaceName,
    '--query', 'id',
    '--output', 'tsv'
) -CaptureOutput

$logAnalyticsWorkspaceCustomerId = Invoke-AzCli -Arguments @(
    'monitor', 'log-analytics', 'workspace', 'show',
    '--resource-group', $resourceGroupName,
    '--workspace-name', $logAnalyticsWorkspaceName,
    '--query', 'customerId',
    '--output', 'tsv'
) -CaptureOutput

$logAnalyticsWorkspaceKey = Invoke-AzCli -Arguments @(
    'monitor', 'log-analytics', 'workspace', 'get-shared-keys',
    '--resource-group', $resourceGroupName,
    '--workspace-name', $logAnalyticsWorkspaceName,
    '--query', 'primarySharedKey',
    '--output', 'tsv'
) -CaptureOutput

$applicationInsightsExists = Test-AzResourceExists -Arguments @(
    'resource', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $applicationInsightsName,
    '--resource-type', 'Microsoft.Insights/components'
)

if ($applicationInsightsExists) {
    Write-Host "Application Insights already exists: $applicationInsightsName"
} else {
    $applicationInsightsResource = "{`"kind`":`"web`",`"location`":`"$location`",`"properties`":{`"Application_Type`":`"web`",`"WorkspaceResourceId`":`"$logAnalyticsWorkspaceResourceId`"}}"

    Invoke-AzCli -Arguments @(
        'resource', 'create',
        '--resource-group', $resourceGroupName,
        '--name', $applicationInsightsName,
        '--resource-type', 'Microsoft.Insights/components',
        '--is-full-object',
        '--properties',
        $applicationInsightsResource
    )
}

$applicationInsightsConnectionString = Invoke-AzCli -Arguments @(
    'resource', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $applicationInsightsName,
    '--resource-type', 'Microsoft.Insights/components',
    '--query', 'properties.ConnectionString',
    '--output', 'tsv'
) -CaptureOutput

$roleAssignmentId = Invoke-AzCli -Arguments @(
    'role', 'assignment', 'list',
    '--assignee', $identityPrincipalId,
    '--role', 'AcrPull',
    '--scope', $acrId,
    '--query', '[0].id',
    '--output', 'tsv'
) -CaptureOutput

if ([string]::IsNullOrWhiteSpace($roleAssignmentId) -or $PlanOnly) {
    Invoke-AzCli -Arguments @(
        'role', 'assignment', 'create',
        '--assignee-object-id', $identityPrincipalId,
        '--assignee-principal-type', 'ServicePrincipal',
        '--role', 'AcrPull',
        '--scope', $acrId
    )
} else {
    Write-Host "AcrPull role assignment already exists for identity: $managedIdentityName"
}

$containerAppsEnvironmentExists = Test-AzResourceExists -Arguments @(
    'containerapp', 'env', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $containerAppsEnvironmentName
)

if ($containerAppsEnvironmentExists) {
    Write-Host "Container Apps environment already exists: $containerAppsEnvironmentName"
} else {
    Invoke-AzCli -Arguments @(
        'containerapp', 'env', 'create',
        '--resource-group', $resourceGroupName,
        '--name', $containerAppsEnvironmentName,
        '--location', $location,
        '--logs-workspace-id', $logAnalyticsWorkspaceCustomerId,
        '--logs-workspace-key', $logAnalyticsWorkspaceKey
    )
}

$dockerfilePath = Join-Path $repoRoot 'backend\Dockerfile'
$buildContext = Join-Path $repoRoot 'backend'

Invoke-AzCli -Arguments @(
    'acr', 'build',
    '--registry', $acrName,
    '--image', "${imageRepository}:${imageTag}",
    '--file', $dockerfilePath,
    $buildContext
)

$image = "${acrLoginServer}/${imageRepository}:${imageTag}"
$appExists = Test-AzResourceExists -Arguments @(
    'containerapp', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $backendContainerAppName
)

if ($appExists) {
    Invoke-AzCli -Arguments @(
        'containerapp', 'update',
        '--resource-group', $resourceGroupName,
        '--name', $backendContainerAppName,
        '--image', $image,
        '--set-env-vars',
        'ASPNETCORE_ENVIRONMENT=Development',
        "Cors__AllowedOrigins__0=$frontendOrigin",
        "APPLICATIONINSIGHTS_CONNECTION_STRING=$applicationInsightsConnectionString",
        "ApplicationInsights__ConnectionString=$applicationInsightsConnectionString",
        '--min-replicas', $backendMinReplicas,
        '--max-replicas', $backendMaxReplicas
    )
} else {
    Invoke-AzCli -Arguments @(
        'containerapp', 'create',
        '--resource-group', $resourceGroupName,
        '--name', $backendContainerAppName,
        '--environment', $containerAppsEnvironmentName,
        '--image', $image,
        '--target-port', $backendTargetPort,
        '--ingress', 'external',
        '--min-replicas', $backendMinReplicas,
        '--max-replicas', $backendMaxReplicas,
        '--user-assigned', $identityId,
        '--registry-server', $acrLoginServer,
        '--registry-identity', $identityId,
        '--env-vars',
        'ASPNETCORE_ENVIRONMENT=Development',
        "Cors__AllowedOrigins__0=$frontendOrigin",
        "APPLICATIONINSIGHTS_CONNECTION_STRING=$applicationInsightsConnectionString",
        "ApplicationInsights__ConnectionString=$applicationInsightsConnectionString"
    )
}

$staticWebAppExists = Test-AzResourceExists -Arguments @(
    'staticwebapp', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $staticWebAppName
)

if ($staticWebAppExists) {
    Write-Host "Static Web App already exists: $staticWebAppName"
} else {
    Invoke-AzCli -Arguments @(
        'staticwebapp', 'create',
        '--resource-group', $resourceGroupName,
        '--name', $staticWebAppName,
        '--location', $staticWebAppLocation,
        '--sku', 'Free'
    )
}

Write-Host ''
Write-Host 'Provisioning flow completed.'
Write-Host "Resource group: $resourceGroupName"
Write-Host "Log Analytics workspace: $logAnalyticsWorkspaceName"
Write-Host "Application Insights: $applicationInsightsName"
Write-Host "ACR: $acrName"
Write-Host "Backend Container App: $backendContainerAppName"
Write-Host "Static Web App: $staticWebAppName"
