using System.Security.Cryptography;
using System.Text;
using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// Produces a deterministic fingerprint of the executable content of a change plan. Approvals
/// record this hash, so editing an operation, its payload or its target invalidates every prior
/// decision instead of silently executing different work under an old approval.
/// </summary>
public static class ChangePlanHasher
{
    public static string Compute(ChangePlan plan)
    {
        var builder = new StringBuilder();
        builder.Append(plan.TenantId).Append('|').Append(plan.Mode).Append('|');

        foreach (var operation in plan.Operations.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            builder
                .Append(operation.Id).Append('\u001f')
                .Append(operation.Module).Append('\u001f')
                .Append(operation.Action).Append('\u001f')
                .Append(operation.Target).Append('\u001f')
                .Append(operation.PayloadJson).Append('\u001f')
                .Append(operation.IsDestructive).Append('\n');
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
