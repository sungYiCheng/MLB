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

    EnableAlertRules = $false
    AlertEvaluationFrequency = 'PT5M'
    AlertWindowSize = 'PT5M'
    AlertSeverity = 3
    Backend5xxAlertName = 'alert-mlb-ai-api-5xx-dev'
    BackendSlowRequestAlertName = 'alert-mlb-ai-api-slow-requests-dev'
    BackendHealthMissingAlertName = 'alert-mlb-ai-api-health-missing-dev'
    BackendSlowRequestThresholdMs = 3000

    AcrName = 'acrmlbaigo'
    ImageRepository = 'mlb-ai-api'
    ImageTag = 'infra-bootstrap'

    ContainerAppsEnvironmentName = 'cae-mlb-ai-go-dev'
    BackendContainerAppName = 'ca-mlb-ai-api'
    BackendTargetPort = 8080
    BackendMinReplicas = 0
    BackendMaxReplicas = 1

    ManagedIdentityName = 'id-mlb-ai-go-acr-pull'

    StaticWebAppName = 'swa-mlb-ai-go-dev'
    FrontendOrigin = 'https://yellow-forest-04081e300.5.azurestaticapps.net'
}
