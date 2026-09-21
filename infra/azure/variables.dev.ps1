$AzureInfraConfig = @{
    EnvironmentName = 'dev'

    SubscriptionId = ''
    Location = 'eastasia'
    StaticWebAppLocation = 'eastasia'

    ResourceGroupName = 'rg-mlb-ai-go-dev'

    LogAnalyticsWorkspaceName = 'log-mlb-ai-go-dev'
    LogAnalyticsRetentionDays = 30
    LogAnalyticsDailyQuotaGb = 0.023
    ApplicationInsightsName = 'appi-mlb-ai-api-dev'

    AcrName = 'acrmlbaigo'
    ImageRepository = 'mlb-ai-api'
    ImageTag = 'infra-bootstrap'

    ContainerAppsEnvironmentName = 'cae-mlb-ai-go-dev'
    BackendContainerAppName = 'ca-mlb-ai-api'
    BackendTargetPort = 8080
    BackendMinReplicas = 0
    BackendMaxReplicas = 1

    ManagedIdentityName = 'id-mlb-ai-api-dev'

    StaticWebAppName = 'swa-mlb-ai-go-dev'
    FrontendOrigin = 'https://yellow-forest-04081e300.5.azurestaticapps.net'
}
