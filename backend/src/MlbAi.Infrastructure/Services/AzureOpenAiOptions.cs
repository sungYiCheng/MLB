namespace MlbAi.Infrastructure.Services;

public sealed record AzureOpenAiOptions(
    string Endpoint,
    string DeploymentName,
    string ApiVersion,
    string ApiKey)
{
    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(DeploymentName) &&
        !string.IsNullOrWhiteSpace(ApiVersion) &&
        !string.IsNullOrWhiteSpace(ApiKey);
}
