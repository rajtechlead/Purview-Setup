using System.Net;
using System.Net.Http;
using System.Text.Json;
using PurviewCopilot.Application.Monitoring;
using PurviewCopilot.Domain;
using Xunit;

namespace PurviewCopilot.Tests;

public class AlertNotifierTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public int Calls { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status);
        }
    }

    private static AlertItem Alert() => new(
        "ALERT-SCORE-DROP",
        "Overall score regressed",
        "80 → 70 (-10).",
        Severity.High,
        new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Send_PostsAlertsAsJson()
    {
        var handler = new CapturingHandler();
        var notifier = new AlertNotifier(new HttpClient(handler));

        var result = await notifier.SendAsync("https://hooks.example.com/alerts", new[] { Alert() }, DateTimeOffset.UtcNow);

        Assert.True(result.Delivered);
        Assert.Equal(1, result.Count);
        Assert.Equal(1, handler.Calls);

        using var document = JsonDocument.Parse(handler.Body!);
        var alert = document.RootElement.GetProperty("alerts")[0];
        Assert.Equal("ALERT-SCORE-DROP", alert.GetProperty("id").GetString());
        Assert.Equal("High", alert.GetProperty("severity").GetString());
        Assert.Equal("Purview Governance Copilot", document.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Send_RejectsNonHttpsWebhook()
    {
        var notifier = new AlertNotifier(new HttpClient(new CapturingHandler()));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notifier.SendAsync("http://hooks.example.com/alerts", new[] { Alert() }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Send_WithNoAlerts_DoesNotPost()
    {
        var handler = new CapturingHandler();
        var notifier = new AlertNotifier(new HttpClient(handler));

        var result = await notifier.SendAsync("https://hooks.example.com/alerts", Array.Empty<AlertItem>(), DateTimeOffset.UtcNow);

        Assert.False(result.Delivered);
        Assert.Equal(0, result.Count);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Send_SurfacesNonSuccessStatus()
    {
        var notifier = new AlertNotifier(new HttpClient(new CapturingHandler { Status = HttpStatusCode.BadGateway }));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            notifier.SendAsync("https://hooks.example.com/alerts", new[] { Alert() }, DateTimeOffset.UtcNow));
    }
}
