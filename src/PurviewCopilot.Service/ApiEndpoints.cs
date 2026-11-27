using PurviewCopilot.Collaboration;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Service;

public sealed record StartApprovalRequest(string? Comment);

public sealed record DecisionRequest(ApprovalDecision Decision, string? Comment);

public sealed record GrantMemberRequest(string UserId, CollaborationRole Role);

public sealed record MeResponse(string UserId, IReadOnlyList<string> TokenRoles, IReadOnlyList<TenantMemberView> Memberships);

public sealed record HealthResponse(string Status, string Provider, string Version);

public static class ApiEndpoints
{
    public static void MapCollaborationApi(this WebApplication app)
    {
        app.MapGet("/health", (IConfiguration configuration) =>
            Results.Ok(new HealthResponse(
                "ok",
                configuration["Collaboration:Provider"] ?? "Sqlite",
                typeof(ApiEndpoints).Assembly.GetName().Version?.ToString() ?? "1.0.0")));

        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/me", async (
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            var memberships = await store.ListMembershipsAsync(caller, cancellationToken);
            return Results.Ok(new MeResponse(caller, ServiceIdentity.Roles(context.User), memberships));
        });

        // ---------------- assessments ----------------

        api.MapGet("/tenants/{tenantId}/assessments", async (
            string tenantId,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            int? take,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.Read, cancellationToken);
            return Results.Ok(await store.ListAssessmentsAsync(tenantId, take ?? 50, cancellationToken));
        });

        api.MapPost("/tenants/{tenantId}/assessments", async (
            string tenantId,
            AssessmentSubmission submission,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.CreateAssessment, cancellationToken);
            var published = await store.PublishAssessmentAsync(tenantId, submission, caller, cancellationToken);
            return Results.Created($"/api/tenants/{tenantId}/assessments/{published.Id}", published);
        });

        // ---------------- plans ----------------

        api.MapGet("/tenants/{tenantId}/plans", async (
            string tenantId,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            int? take,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.Read, cancellationToken);
            return Results.Ok(await store.ListPlansAsync(tenantId, take ?? 50, cancellationToken));
        });

        api.MapPost("/tenants/{tenantId}/plans", async (
            string tenantId,
            ChangePlan plan,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.CreatePlan, cancellationToken);
            var published = await store.PublishPlanAsync(tenantId, plan, caller, cancellationToken);
            return Results.Created($"/api/tenants/{tenantId}/plans/{published.Id}", published);
        });

        // ---------------- approvals ----------------

        api.MapGet("/tenants/{tenantId}/plans/{planId:guid}/approval", async (
            string tenantId,
            Guid planId,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.Read, cancellationToken);
            var approval = await store.GetApprovalAsync(tenantId, planId, cancellationToken);
            return approval is null ? Results.NotFound() : Results.Ok(approval);
        });

        api.MapPost("/tenants/{tenantId}/plans/{planId:guid}/approval/start", async (
            string tenantId,
            Guid planId,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.StartApproval, cancellationToken);
            return Results.Ok(await store.StartApprovalAsync(tenantId, planId, caller, cancellationToken));
        });

        api.MapPost("/tenants/{tenantId}/plans/{planId:guid}/approval/decision", async (
            string tenantId,
            Guid planId,
            DecisionRequest request,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.DecideApproval, cancellationToken);

            var stamp = context.Request.Headers.IfMatch.ToString();
            var approval = await store.DecideApprovalAsync(
                tenantId, planId, request.Decision, caller, request.Comment ?? string.Empty,
                string.IsNullOrWhiteSpace(stamp) ? null : stamp.Trim('"'), cancellationToken);

            return Results.Ok(approval);
        });

        // ---------------- audit ----------------

        api.MapGet("/tenants/{tenantId}/audit", async (
            string tenantId,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            int? take,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.ReadAudit, cancellationToken);
            return Results.Ok(await store.ListAuditAsync(tenantId, take ?? 100, cancellationToken));
        });

        api.MapGet("/tenants/{tenantId}/audit/verify", async (
            string tenantId,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.ReadAudit, cancellationToken);
            return Results.Ok(await store.VerifyAuditAsync(tenantId, cancellationToken));
        });

        // ---------------- members ----------------

        api.MapGet("/tenants/{tenantId}/members", async (
            string tenantId,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.ManageMembers, cancellationToken);
            return Results.Ok(await store.ListMembersAsync(tenantId, cancellationToken));
        });

        api.MapPost("/tenants/{tenantId}/members", async (
            string tenantId,
            GrantMemberRequest request,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.ManageMembers, cancellationToken);
            var member = await store.GrantMemberAsync(tenantId, request.UserId, request.Role, caller, cancellationToken);
            return Results.Ok(member);
        });

        api.MapDelete("/tenants/{tenantId}/members/{userId}", async (
            string tenantId,
            string userId,
            HttpContext context,
            CollaborationStore store,
            TenantAuthorizationService authorization,
            CancellationToken cancellationToken) =>
        {
            var caller = RequireCaller(context);
            await authorization.RequireAsync(tenantId, caller, CollaborationPermission.ManageMembers, cancellationToken);
            await store.RemoveMemberAsync(tenantId, userId, caller, cancellationToken);
            return Results.NoContent();
        });
    }

    private static string RequireCaller(HttpContext context) =>
        ServiceIdentity.CallerId(context.User)
        ?? throw new TenantAccessDeniedException("<anonymous>", "unknown");
}
