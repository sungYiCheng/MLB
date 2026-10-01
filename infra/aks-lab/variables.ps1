$AksLabConfig = @{
    SubscriptionId = ''
    Location = 'eastasia'

    ResourceGroupName = 'rg-mlb-ai-go-aks-lab'
    ClusterName = 'aks-mlb-ai-go-lab'
    NodeVmSize = 'Standard_B2s'
    NodeCount = 1
    NodeOsDiskSizeGb = 32

    SharedResourceGroupName = 'rg-mlb-ai-go-dev'
    AcrName = 'acrmlbaigo'
    ImageRepository = 'mlb-ai-api'
    ApplicationInsightsName = 'appi-mlb-ai-api-dev'

    KubernetesNamespace = 'mlb-ai-go'
    KubernetesDeploymentName = 'mlb-ai-api'
    KubernetesServiceName = 'mlb-ai-api'
    FrontendOrigin = 'https://yellow-forest-04081e300.5.azurestaticapps.net'
}
