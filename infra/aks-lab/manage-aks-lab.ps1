param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Plan', 'Preflight', 'Create', 'Deploy', 'Stop', 'Start', 'Status', 'Destroy')]
    [string]$Operation,

    [string]$SubscriptionId,
    [string]$ImageTag = 'dev-latest',
    [string]$BuildId = 'manual',
    [switch]$IncludeIngress,
    [switch]$ConfirmDestroy
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..\..')
. (Join-Path $scriptRoot 'variables.ps1')

if ($SubscriptionId) {
    $AksLabConfig.SubscriptionId = $SubscriptionId
}

$subscription = $AksLabConfig.SubscriptionId
$location = $AksLabConfig.Location
$resourceGroupName = $AksLabConfig.ResourceGroupName
$clusterName = $AksLabConfig.ClusterName
$controlPlaneIdentityName = $AksLabConfig.ControlPlaneIdentityName
$kubeletIdentityName = $AksLabConfig.KubeletIdentityName
$nodeVmSize = $AksLabConfig.NodeVmSize
$nodeCount = [string]$AksLabConfig.NodeCount
$nodeOsDiskSizeGb = [string]$AksLabConfig.NodeOsDiskSizeGb
$sharedResourceGroupName = $AksLabConfig.SharedResourceGroupName
$acrName = $AksLabConfig.AcrName
$imageRepository = $AksLabConfig.ImageRepository
$applicationInsightsName = $AksLabConfig.ApplicationInsightsName
$kubernetesNamespace = $AksLabConfig.KubernetesNamespace
$kubernetesDeploymentName = $AksLabConfig.KubernetesDeploymentName
$kubernetesServiceName = $AksLabConfig.KubernetesServiceName
$frontendOrigin = $AksLabConfig.FrontendOrigin
$baseManifestPath = Join-Path $repoRoot 'k8s\base'
$ingressManifestPath = Join-Path $repoRoot 'k8s\overlays\ingress'

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

function Assert-ClusterExists {
    if (-not (Test-AzResourceExists -Arguments @(
        'aks', 'show',
        '--resource-group', $resourceGroupName,
        '--name', $clusterName
    ))) {
        throw "AKS lab cluster does not exist: $resourceGroupName/$clusterName. Run Create first."
    }
}

function Connect-AksCluster {
    Assert-ClusterExists
    Invoke-AzCli -Arguments @(
        'aks', 'get-credentials',
        '--resource-group', $resourceGroupName,
        '--name', $clusterName,
        '--admin',
        '--overwrite-existing'
    )
}

function Invoke-Kubectl {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host ('[kubectl] kubectl ' + ($Arguments -join ' '))
    & kubectl @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "kubectl command failed: kubectl $($Arguments -join ' ')"
    }
}

function Invoke-Helm {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host ('[helm] helm ' + ($Arguments -join ' '))
    & helm @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Helm command failed: helm $($Arguments -join ' ')"
    }
}

if (-not [string]::IsNullOrWhiteSpace($subscription)) {
    Invoke-AzCli -Arguments @('account', 'set', '--subscription', $subscription)
}

switch ($Operation) {
    'Plan' {
        Write-Host 'AKS lab plan (no Azure resources will be changed)'
        Write-Host "Lab resource group : $resourceGroupName"
        Write-Host 'Node resource group: AKS-managed; created and deleted with the cluster'
        Write-Host "AKS cluster        : $clusterName"
        Write-Host "Region             : $location"
        Write-Host "AKS tier           : Free"
        Write-Host "Node pool          : $nodeCount x $nodeVmSize"
        Write-Host "OS disk            : $nodeOsDiskSizeGb GiB managed disk"
        Write-Host "Shared ACR         : $sharedResourceGroupName/$acrName"
        Write-Host "Control identity   : $controlPlaneIdentityName"
        Write-Host "Kubelet identity   : $kubeletIdentityName"
        Write-Host "Image              : $acrName.azurecr.io/${imageRepository}:$ImageTag"
        Write-Host "Ingress requested  : $([bool]$IncludeIngress)"
        Write-Host 'The existing dev resource group will not be deleted by this workflow.'
        break
    }

    'Preflight' {
        if ($resourceGroupName -eq $sharedResourceGroupName) {
            throw 'The AKS bootstrap resource group must be different from the shared dev resource group.'
        }

        $accountSummary = Invoke-AzCli -Arguments @(
            'account', 'show',
            '--query', '{account:user.name,subscription:name,state:state}',
            '--output', 'table'
        ) -CaptureOutput
        Write-Host 'Azure account:'
        Write-Host $accountSummary

        foreach ($providerNamespace in @('Microsoft.Compute', 'Microsoft.ContainerService', 'Microsoft.Network')) {
            $providerState = Invoke-AzCli -Arguments @(
                'provider', 'show',
                '--namespace', $providerNamespace,
                '--query', 'registrationState',
                '--output', 'tsv'
            ) -CaptureOutput
            if ($providerState -ne 'Registered') {
                throw "$providerNamespace provider is not registered. Current state: $providerState"
            }
            Write-Host "Azure provider     : $providerNamespace = $providerState"
        }

        $acrSummary = Invoke-AzCli -Arguments @(
            'acr', 'show',
            '--resource-group', $sharedResourceGroupName,
            '--name', $acrName,
            '--query', '{name:name,loginServer:loginServer,sku:sku.name}',
            '--output', 'table'
        ) -CaptureOutput
        Write-Host 'Shared ACR:'
        Write-Host $acrSummary

        $skuName = Invoke-AzCli -Arguments @(
            'vm', 'list-sizes',
            '--location', $location,
            '--query', "[?name=='$nodeVmSize'] | [0].name",
            '--output', 'tsv'
        ) -CaptureOutput
        if ($skuName -ne $nodeVmSize) {
            throw "Node VM size $nodeVmSize is not available in $location."
        }
        Write-Host "Node VM size       : $skuName is listed in $location"

        $quotaSummary = Invoke-AzCli -Arguments @(
            'vm', 'list-usage',
            '--location', $location,
            '--query', "[?name.value=='cores' || name.value=='standardBSFamily'].{quota:name.localizedValue,current:currentValue,limit:limit}",
            '--output', 'table'
        ) -CaptureOutput
        Write-Host 'Relevant compute quota:'
        if ([string]::IsNullOrWhiteSpace($quotaSummary)) {
            Write-Host 'Quota rows were not returned. Review East Asia Compute usage in the Azure portal before Create.'
        } else {
            Write-Host $quotaSummary
        }

        $groupSummary = Invoke-AzCli -Arguments @(
            'group', 'show',
            '--name', $resourceGroupName,
            '--query', '{name:name,location:location,provisioningState:properties.provisioningState}',
            '--output', 'table'
        ) -CaptureOutput
        Write-Host 'Bootstrap resource group:'
        Write-Host $groupSummary

        foreach ($identityName in @($controlPlaneIdentityName, $kubeletIdentityName)) {
            $identitySummary = Invoke-AzCli -Arguments @(
                'identity', 'show',
                '--resource-group', $resourceGroupName,
                '--name', $identityName,
                '--query', '{name:name,clientId:clientId}',
                '--output', 'table'
            ) -CaptureOutput
            Write-Host 'Bootstrap managed identity:'
            Write-Host $identitySummary
        }

        Write-Host "Destroy boundary   : AKS cluster $clusterName; bootstrap group and identities remain"

        Invoke-Kubectl -Arguments @('kustomize', $baseManifestPath)
        Invoke-Kubectl -Arguments @('kustomize', $ingressManifestPath)
        Write-Host 'Kustomize          : base and ingress overlay rendered successfully'
        Write-Host 'Preflight completed without creating or changing AKS resources.'
        break
    }

    'Create' {
        $controlPlaneIdentityId = Invoke-AzCli -Arguments @(
            'identity', 'show',
            '--resource-group', $resourceGroupName,
            '--name', $controlPlaneIdentityName,
            '--query', 'id',
            '--output', 'tsv'
        ) -CaptureOutput

        $kubeletIdentityId = Invoke-AzCli -Arguments @(
            'identity', 'show',
            '--resource-group', $resourceGroupName,
            '--name', $kubeletIdentityName,
            '--query', 'id',
            '--output', 'tsv'
        ) -CaptureOutput

        if (Test-AzResourceExists -Arguments @(
            'aks', 'show',
            '--resource-group', $resourceGroupName,
            '--name', $clusterName
        )) {
            Write-Host "AKS lab already exists: $clusterName"
        } else {
            Invoke-AzCli -Arguments @(
                'aks', 'create',
                '--resource-group', $resourceGroupName,
                '--name', $clusterName,
                '--location', $location,
                '--tier', 'free',
                '--node-count', $nodeCount,
                '--node-vm-size', $nodeVmSize,
                '--node-osdisk-size', $nodeOsDiskSizeGb,
                '--node-osdisk-type', 'Managed',
                '--network-plugin', 'azure',
                '--network-plugin-mode', 'overlay',
                '--load-balancer-sku', 'standard',
                '--outbound-type', 'loadBalancer',
                '--assign-identity', $controlPlaneIdentityId,
                '--assign-kubelet-identity', $kubeletIdentityId,
                '--enable-oidc-issuer',
                '--enable-workload-identity',
                '--generate-ssh-keys'
            )
        }

        Invoke-AzCli -Arguments @(
            'aks', 'show',
            '--resource-group', $resourceGroupName,
            '--name', $clusterName,
            '--query', '{name:name,powerState:powerState.code,nodeResourceGroup:nodeResourceGroup,kubernetesVersion:kubernetesVersion}',
            '--output', 'table'
        )
        break
    }

    'Deploy' {
        Connect-AksCluster

        $powerState = Invoke-AzCli -Arguments @(
            'aks', 'show',
            '--resource-group', $resourceGroupName,
            '--name', $clusterName,
            '--query', 'powerState.code',
            '--output', 'tsv'
        ) -CaptureOutput

        if ($powerState -ne 'Running') {
            throw "AKS lab must be Running before Deploy. Current state: $powerState"
        }

        $applicationInsightsConnectionString = Invoke-AzCli -Arguments @(
            'resource', 'show',
            '--resource-group', $sharedResourceGroupName,
            '--name', $applicationInsightsName,
            '--resource-type', 'Microsoft.Insights/components',
            '--query', 'properties.ConnectionString',
            '--output', 'tsv'
        ) -CaptureOutput

        Invoke-Kubectl -Arguments @('apply', '-f', (Join-Path $baseManifestPath 'namespace.yaml'))

        $secretArguments = @(
            'create', 'secret', 'generic', 'mlb-ai-api-secrets',
            '--namespace', $kubernetesNamespace,
            '--from-literal', "APPLICATIONINSIGHTS_CONNECTION_STRING=$applicationInsightsConnectionString",
            '--from-literal', "ApplicationInsights__ConnectionString=$applicationInsightsConnectionString",
            '--dry-run=client',
            '-o', 'yaml'
        )
        $secretYaml = & kubectl @secretArguments
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to render the Application Insights Kubernetes Secret.'
        }

        $secretYaml | & kubectl apply -f -
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to create or update the Application Insights Kubernetes Secret.'
        }

        Invoke-Kubectl -Arguments @('apply', '-k', $baseManifestPath)

        $image = "$acrName.azurecr.io/${imageRepository}:$ImageTag"
        Invoke-Kubectl -Arguments @(
            'set', 'image',
            "deployment/$kubernetesDeploymentName",
            "$kubernetesDeploymentName=$image",
            '--namespace', $kubernetesNamespace
        )
        Invoke-Kubectl -Arguments @(
            'set', 'env',
            "deployment/$kubernetesDeploymentName",
            '--namespace', $kubernetesNamespace,
            "Cors__AllowedOrigins__0=$frontendOrigin",
            "AppVersion=$ImageTag",
            "AppBuildId=$BuildId",
            "AppImageTag=$ImageTag"
        )
        Invoke-Kubectl -Arguments @(
            'rollout', 'status',
            "deployment/$kubernetesDeploymentName",
            '--namespace', $kubernetesNamespace,
            '--timeout=5m'
        )

        $smokePodName = "mlb-api-smoke-$($BuildId.ToLowerInvariant() -replace '[^a-z0-9-]', '-')"
        if ($smokePodName.Length -gt 63) {
            $smokePodName = $smokePodName.Substring(0, 63).TrimEnd('-')
        }

        Invoke-Kubectl -Arguments @(
            'run', $smokePodName,
            '--namespace', $kubernetesNamespace,
            '--image=curlimages/curl:8.12.1',
            '--restart=Never',
            '--rm',
            '--attach',
            '--command', '--',
            'sh', '-c',
            "curl --fail --retry 10 --retry-delay 5 http://${kubernetesServiceName}/health"
        )

        if ($IncludeIngress) {
            Invoke-Helm -Arguments @('repo', 'add', 'ingress-nginx', 'https://kubernetes.github.io/ingress-nginx')
            Invoke-Helm -Arguments @('repo', 'update')
            Invoke-Helm -Arguments @(
                'upgrade', '--install', 'ingress-nginx', 'ingress-nginx/ingress-nginx',
                '--namespace', 'ingress-nginx',
                '--create-namespace',
                '--wait',
                '--timeout', '10m'
            )
            Invoke-Kubectl -Arguments @('apply', '-k', $ingressManifestPath)

            Invoke-Kubectl -Arguments @(
                'get', 'service', 'ingress-nginx-controller',
                '--namespace', 'ingress-nginx',
                '--output', 'wide'
            )
            Write-Host 'Ingress is enabled. Azure may need several minutes to assign the external IP.'
            Write-Host 'Test with the Host header api.mlb-ai-go.local after EXTERNAL-IP is assigned.'
        } else {
            Write-Host 'Ingress was not installed, so the API has no application-facing public endpoint.'
            Write-Host 'AKS can still retain its managed Standard Load Balancer for cluster outbound traffic.'
        }

        Invoke-Kubectl -Arguments @('get', 'pods,services,deployments', '--namespace', $kubernetesNamespace, '--output', 'wide')
        break
    }

    'Stop' {
        Assert-ClusterExists
        Invoke-AzCli -Arguments @('aks', 'stop', '--resource-group', $resourceGroupName, '--name', $clusterName)
        Write-Host "AKS lab stopped: $clusterName"
        break
    }

    'Start' {
        Assert-ClusterExists
        Invoke-AzCli -Arguments @('aks', 'start', '--resource-group', $resourceGroupName, '--name', $clusterName)
        Write-Host "AKS lab started: $clusterName"
        break
    }

    'Status' {
        if (Test-AzResourceExists -Arguments @(
            'aks', 'show',
            '--resource-group', $resourceGroupName,
            '--name', $clusterName
        )) {
            Invoke-AzCli -Arguments @(
                'aks', 'show',
                '--resource-group', $resourceGroupName,
                '--name', $clusterName,
                '--query', '{name:name,powerState:powerState.code,provisioningState:provisioningState,nodeResourceGroup:nodeResourceGroup,kubernetesVersion:kubernetesVersion}',
                '--output', 'table'
            )
        } else {
            Write-Host 'AKS lab is not currently provisioned.'
        }
        break
    }

    'Destroy' {
        if (-not $ConfirmDestroy) {
            throw 'Destroy requires -ConfirmDestroy. This protects the AKS cluster from accidental deletion.'
        }

        if (-not (Test-AzResourceExists -Arguments @('group', 'show', '--name', $resourceGroupName))) {
            Write-Host "AKS bootstrap resource group is absent: $resourceGroupName. Nothing can be destroyed."
            break
        }

        if (Test-AzResourceExists -Arguments @(
            'aks', 'show',
            '--resource-group', $resourceGroupName,
            '--name', $clusterName
        )) {
            $nodeResourceGroup = Invoke-AzCli -Arguments @(
                'aks', 'show',
                '--resource-group', $resourceGroupName,
                '--name', $clusterName,
                '--query', 'nodeResourceGroup',
                '--output', 'tsv'
            ) -CaptureOutput
            Write-Host "Deleting disposable AKS cluster and its billable resources: $clusterName"
            Invoke-AzCli -Arguments @(
                'aks', 'delete',
                '--resource-group', $resourceGroupName,
                '--name', $clusterName,
                '--yes'
            )
            Write-Host "AKS also deletes its managed node resource group: $nodeResourceGroup"
        } else {
            Write-Host "AKS lab cluster is already absent: $clusterName"
        }

        Write-Host 'AKS lab compute was destroyed. The free bootstrap group, identities, and RBAC assignments were preserved.'
        Write-Host 'The shared dev resource group was not changed.'
        break
    }
}
