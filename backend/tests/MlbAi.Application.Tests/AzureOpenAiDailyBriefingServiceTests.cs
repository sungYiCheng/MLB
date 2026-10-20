using System.Net;
using System.Text;
using System.Text.Json;
using MlbAi.Application.Models;
using MlbAi.Infrastructure.Services;
using Xunit;

namespace MlbAi.Application.Tests;

public sealed class AzureOpenAiDailyBriefingServiceTests
{
    [Fact]
    public async Task GenerateAsync_UsesStructuredOutputAndMapsTheModelResponse()
    {
        var generatedContent = JsonSerializer.Serialize(new
        {
            headline = "One matchup to follow",
            summary = "A concise grounded summary.",
            keyGames = new[]
            {
                new { title = "Guardians at Rays", detail = "The supplied probable starters are set." }
            },
            playersToWatch = new[]
            {
                new { title = "Yordan Alvarez", detail = "He leads the supplied batting average list." }
            }
        });
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = generatedContent
                    }
                }
            }
        });
        var handler = new RecordingHttpMessageHandler(responseBody);
        var service = new AzureOpenAiDailyBriefingService(
            new HttpClient(handler),
            new AzureOpenAiOptions(
                "https://test-resource.openai.azure.com",
                "gpt-4.1-mini",
                "2024-10-21",
                "test-key"));

        var result = await service.GenerateAsync(
            [],
            new MlbStandingsDto([], [], null),
            new MlbStatLeadersDto([]),
            DateTimeOffset.UnixEpoch,
            CancellationToken.None);

        Assert.True(result.IsAiGenerated);
        Assert.Equal("azure-openai", result.Mode);
        Assert.Equal("One matchup to follow", result.Headline);
        Assert.Equal("Guardians at Rays", result.KeyGames.Single().Title);
        Assert.Contains("gpt-4.1-mini", result.DataNotes[1]);
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Contains("/openai/deployments/gpt-4.1-mini/chat/completions", handler.RequestUri?.AbsoluteUri);
        Assert.Contains("api-version=2024-10-21", handler.RequestUri?.Query);
        Assert.Contains("\"json_schema\"", handler.RequestBody);
    }

    [Fact]
    public void IsConfigured_IsFalseWhenCredentialsAreMissing()
    {
        var service = new AzureOpenAiDailyBriefingService(
            new HttpClient(new RecordingHttpMessageHandler("{}")),
            new AzureOpenAiOptions(string.Empty, string.Empty, "2024-10-21", string.Empty));

        Assert.False(service.IsConfigured);
    }

    private sealed class RecordingHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? ApiKey { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("api-key").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
