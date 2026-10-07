$AksLabConfig = @{
    SubscriptionId = ''
    Location = 'eastasia'

    ResourceGroupName = 'rg-mlb-ai-go-aks-lab'
    ClusterName = 'aks-mlb-ai-go-lab'
    ControlPlaneIdentityName = 'id-mlb-ai-go-aks-control'
    KubeletIdentityName = 'id-mlb-ai-go-aks-kubelet'
    NodeVmSize = 'Standard_D2_v4'
    NodeVmQuotaFamily = 'standardDv4Family'
    NodeCount = 2
    NodeOsDiskSizeGb = 32

    SharedResourceGroupName = 'rg-mlb-ai-go-dev'
    AcrName = 'acrmlbaigo'
    ImageRepository = 'mlb-ai-api'
    ApplicationInsightsName = 'appi-mlb-ai-api-dev'
    AzureDevOpsServicePrincipalId = 'ce79afe0-6b1d-40ac-a412-ef7bb341b011'

    KubernetesNamespace = 'mlb-ai-go'
    KubernetesDeploymentName = 'mlb-ai-api'
    KubernetesServiceName = 'mlb-ai-api'
    KubernetesGatewayName = 'mlb-ai-api-gateway'
    FrontendOrigin = 'https://yellow-forest-04081e300.5.azurestaticapps.net'
}
