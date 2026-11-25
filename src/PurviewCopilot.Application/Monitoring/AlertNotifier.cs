using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace PurviewCopilot.Application.Monitoring;

public sealed record AlertDeliveryResult(bool Delivered, int Count, string Message);

/// <summary>
/// Delivers monitoring alerts to an operator-supplied HTTPS webhook (Teams, Slack or an internal
/// receiver). Payloads contain only the alert id, title, detail, severity and timestamp.
/// </summary>
public sealed class AlertNotifier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public AlertNotifier(HttpClient http) => _http = http;

    public async Task<AlertDeliveryResult> SendAsync(
        string webhookUrl,
        IReadOnlyList<AlertItem> alerts,
        DateTimeOffset sentUtc,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Alert webhook must be an absolute https URL.");
        }

        if (alerts.Count == 0)
        {
            return new AlertDeliveryResult(false, 0, "No alerts to deliver.");
        }

        var payload = new
        {
            source = "Purview Governance Copilot",
            sentUtc,
            alerts = alerts.Select(a => new
            {
                id = a.Id,
                title = a.Title,
                detail = a.Detail,
                severity = a.Severity.ToString(),
                atUtc = a.AtUtc
            })
        };

        using var response = await _http.PostAsJsonAsync(uri, payload, Json, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return new AlertDeliveryResult(
            true,
            alerts.Count,
            $"Delivered {alerts.Count} alert(s) to {uri.Host} ({(int)response.StatusCode}).");
    }
}
