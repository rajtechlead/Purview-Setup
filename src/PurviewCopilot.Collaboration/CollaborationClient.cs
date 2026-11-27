using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Collaboration;

public sealed record ClientIdentity(
    string UserId,
    IReadOnlyList<string> TokenRoles,
    IReadOnlyList<TenantMemberView> Memberships);

public sealed class CollaborationClientException : Exception
{
    public CollaborationClientException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public static CollaborationClientException From(HttpStatusCode statusCode, string body) =>
        new(statusCode, statusCode switch
        {
            HttpStatusCode.Unauthorized => "The service rejected the credential. Sign in again through Entra ID.",
            HttpStatusCode.Forbidden => "Your collaboration role does not permit this action in this tenant.",
            HttpStatusCode.NotFound => "The requested record does not exist in this tenant.",
            HttpStatusCode.Conflict => $"The record changed since it was read. {Trim(body)}".Trim(),
            _ => $"The collaboration service returned {(int)statusCode}. {Trim(body)}".Trim()
        }, body);

    private static string Trim(string body) => body.Length <= 500 ? body : body[..500];
}

/// <summary>
/// Typed client for the customer-hosted collaboration service. The caller supplies the
/// <see cref="HttpClient"/>, so the desktop app and CLI share one authentication and proxy setup;
/// this type never stores credentials itself.
/// </summary>
public sealed class CollaborationClient
{
    private readonly HttpClient _http;

    public CollaborationClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));

        if (_http.BaseAddress is null)
            throw new InvalidOperationException("The HttpClient must have a BaseAddress before it can call the collaboration service.");
    }

    public Task<ClientIdentity> GetMeAsync(CancellationToken cancellationToken = default) =>
        GetAsync<ClientIdentity>("api/me", cancellationToken);

    public Task<IReadOnlyList<SharedAssessmentView>> ListAssessmentsAsync(
        string tenantId, int take = 50, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SharedAssessmentView>>(
            $"api/tenants/{Escape(tenantId)}/assessments?take={Math.Max(1, take)}", cancellationToken);

    public Task<SharedAssessmentView> PublishAssessmentAsync(
        string tenantId, AssessmentSubmission submission, CancellationToken cancellationToken = default) =>
        PostAsync<SharedAssessmentView>(
            $"api/tenants/{Escape(tenantId)}/assessments", submission, cancellationToken);

    public Task<IReadOnlyList<SharedPlanView>> ListPlansAsync(
        string tenantId, int take = 50, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SharedPlanView>>(
            $"api/tenants/{Escape(tenantId)}/plans?take={Math.Max(1, take)}", cancellationToken);

    public Task<SharedPlanView> PublishPlanAsync(
        string tenantId, ChangePlan plan, CancellationToken cancellationToken = default) =>
        PostAsync<SharedPlanView>($"api/tenants/{Escape(tenantId)}/plans", plan, cancellationToken);

    public async Task<SharedApprovalView?> GetApprovalAsync(
        string tenantId, Guid planId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            $"api/tenants/{Escape(tenantId)}/plans/{planId:D}/approval", cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await ReadAsync<SharedApprovalView>(response, cancellationToken).ConfigureAwait(false);
    }

    public Task<SharedApprovalView> StartApprovalAsync(
        string tenantId, Guid planId, string? comment = null, CancellationToken cancellationToken = default) =>
        PostAsync<SharedApprovalView>(
            $"api/tenants/{Escape(tenantId)}/plans/{planId:D}/approval/start",
            new { comment },
            cancellationToken);

    public async Task<SharedApprovalView> DecideAsync(
        string tenantId,
        Guid planId,
        ApprovalDecision decision,
        string comment = "",
        string? concurrencyStamp = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"api/tenants/{Escape(tenantId)}/plans/{planId:D}/approval/decision")
        {
            Content = JsonContent.Create(new { decision, comment })
        };

        if (!string.IsNullOrWhiteSpace(concurrencyStamp))
        {
            request.Headers.TryAddWithoutValidation("If-Match", $"\"{concurrencyStamp}\"");
        }

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<SharedApprovalView>(response, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<SharedAuditView>> ListAuditAsync(
        string tenantId, int take = 100, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<SharedAuditView>>(
            $"api/tenants/{Escape(tenantId)}/audit?take={Math.Max(1, take)}", cancellationToken);

    public Task<AuditVerification> VerifyAuditAsync(string tenantId, CancellationToken cancellationToken = default) =>
        GetAsync<AuditVerification>($"api/tenants/{Escape(tenantId)}/audit/verify", cancellationToken);

    public Task<IReadOnlyList<TenantMemberView>> ListMembersAsync(
        string tenantId, CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<TenantMemberView>>($"api/tenants/{Escape(tenantId)}/members", cancellationToken);

    public Task<TenantMemberView> GrantMemberAsync(
        string tenantId, string userId, CollaborationRole role, CancellationToken cancellationToken = default) =>
        PostAsync<TenantMemberView>(
            $"api/tenants/{Escape(tenantId)}/members",
            new { userId, role },
            cancellationToken);

    public async Task RemoveMemberAsync(
        string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(
            $"api/tenants/{Escape(tenantId)}/members/{Escape(userId)}", cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw CollaborationClientException.From(response.StatusCode, await BodyAsync(response, cancellationToken).ConfigureAwait(false));
        }
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<T> PostAsync<T>(string path, object body, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsJsonAsync(path, body, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw CollaborationClientException.From(
                response.StatusCode, await BodyAsync(response, cancellationToken).ConfigureAwait(false));
        }

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken).ConfigureAwait(false);
        return value ?? throw new CollaborationClientException(
            response.StatusCode, "The collaboration service returned an empty response.");
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return string.Empty;
        }
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}

public interface ICollaborationTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Adds a bearer token to service calls; an already-set Authorization header is preserved.</summary>
public sealed class CollaborationBearerHandler : DelegatingHandler
{
    private readonly ICollaborationTokenProvider _tokens;

    public CollaborationBearerHandler(ICollaborationTokenProvider tokens) : base(new HttpClientHandler())
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    public CollaborationBearerHandler(ICollaborationTokenProvider tokens, HttpMessageHandler inner)
        : base(inner)
    {
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = await _tokens.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

public sealed class StaticTokenProvider : ICollaborationTokenProvider
{
    private readonly string? _token;

    public StaticTokenProvider(string? token) => _token = token;

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_token);
}
