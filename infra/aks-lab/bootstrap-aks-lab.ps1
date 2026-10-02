param(
    [string]$SubscriptionId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot 'variables.ps1')

if ($SubscriptionId) {
    $AksLabConfig.SubscriptionId = $SubscriptionId
}

function Invoke-AzCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [switch]$CaptureOutput
    )

    Write-Host ('[az] az ' + ($Arguments -join ' '))
    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }

    if ($CaptureOutput) {
        return ($output -join "`n").Trim()
    }

    if ($null -ne $output) {
        $output | Write-Output
    }
}

function Test-AzResourceExists {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $null = & az @Arguments 2>$null
    return $LASTEXITCODE -eq 0
}

function Set-RoleAssignment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PrincipalId,

        [Parameter(Mandatory = $true)]
        [string]$Role,

        [Parameter(Mandatory = $true)]
        [string]$Scope
    )

    $existingAssignment = Invoke-AzCli -Arguments @(
        'role', 'assignment', 'list',
        '--assignee', $PrincipalId,
        '--role', $Role,
        '--scope', $Scope,
        '--query', '[0].id',
        '--output', 'tsv'
    ) -CaptureOutput

    if ([string]::IsNullOrWhiteSpace($existingAssignment)) {
        Invoke-AzCli -Arguments @(
            'role', 'assignment', 'create',
            '--assignee-object-id', $PrincipalId,
            '--assignee-principal-type', 'ServicePrincipal',
            '--role', $Role,
            '--scope', $Scope
        )
    } else {
        Write-Host "Role assignment already exists: $Role on $Scope"
    }
}

$subscription = $AksLabConfig.SubscriptionId
$location = $AksLabConfig.Location
$resourceGroupName = $AksLabConfig.ResourceGroupName
$controlPlaneIdentityName = $AksLabConfig.ControlPlaneIdentityName
$kubeletIdentityName = $AksLabConfig.KubeletIdentityName
$sharedResourceGroupName = $AksLabConfig.SharedResourceGroupName
$acrName = $AksLabConfig.AcrName
$azureDevOpsServicePrincipalId = $AksLabConfig.AzureDevOpsServicePrincipalId

if (-not [string]::IsNullOrWhiteSpace($subscription)) {
    Invoke-AzCli -Arguments @('account', 'set', '--subscription', $subscription)
}

foreach ($providerNamespace in @('Microsoft.Compute', 'Microsoft.ContainerService', 'Microsoft.Network')) {
    Invoke-AzCli -Arguments @('provider', 'register', '--namespace', $providerNamespace, '--wait')
}

Invoke-AzCli -Arguments @('group', 'create', '--name', $resourceGroupName, '--location', $location)

foreach ($identityName in @($controlPlaneIdentityName, $kubeletIdentityName)) {
    if (Test-AzResourceExists -Arguments @(
        'identity', 'show',
        '--resource-group', $resourceGroupName,
        '--name', $identityName
    )) {
        Write-Host "Managed identity already exists: $identityName"
    } else {
        Invoke-AzCli -Arguments @(
            'identity', 'create',
            '--resource-group', $resourceGroupName,
            '--name', $identityName,
            '--location', $location
        )
    }
}

$subscriptionId = Invoke-AzCli -Arguments @('account', 'show', '--query', 'id', '--output', 'tsv') -CaptureOutput
$labResourceGroupId = "/subscriptions/$subscriptionId/resourceGroups/$resourceGroupName"
$acrId = Invoke-AzCli -Arguments @(
    'acr', 'show',
    '--resource-group', $sharedResourceGroupName,
    '--name', $acrName,
    '--query', 'id',
    '--output', 'tsv'
) -CaptureOutput
$controlPlanePrincipalId = Invoke-AzCli -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $controlPlaneIdentityName,
    '--query', 'principalId',
    '--output', 'tsv'
) -CaptureOutput
$kubeletIdentityId = Invoke-AzCli -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $kubeletIdentityName,
    '--query', 'id',
    '--output', 'tsv'
) -CaptureOutput
$kubeletPrincipalId = Invoke-AzCli -Arguments @(
    'identity', 'show',
    '--resource-group', $resourceGroupName,
    '--name', $kubeletIdentityName,
    '--query', 'principalId',
    '--output', 'tsv'
) -CaptureOutput

Set-RoleAssignment -PrincipalId $azureDevOpsServicePrincipalId -Role 'Contributor' -Scope $labResourceGroupId
Set-RoleAssignment -PrincipalId $controlPlanePrincipalId -Role 'Managed Identity Operator' -Scope $kubeletIdentityId
Set-RoleAssignment -PrincipalId $kubeletPrincipalId -Role 'AcrPull' -Scope $acrId

Write-Host ''
Write-Host 'AKS Lab bootstrap completed.'
Write-Host "Lab resource group : $resourceGroupName"
Write-Host 'Node resource group: AKS will create and delete it with the cluster.'
Write-Host "Control identity   : $controlPlaneIdentityName"
Write-Host "Kubelet identity   : $kubeletIdentityName"
Write-Host 'No AKS cluster or billable compute resource was created.'
