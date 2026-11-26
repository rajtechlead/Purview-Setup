namespace PurviewCopilot.Application.Auth;

/// <summary>
/// Deployment operators may not approve their own work, and tenant administrators are not
/// acceptable approvers for a deployment they can also execute.
/// </summary>
public static class SeparationOfDuties
{
    public static readonly IReadOnlyList<string> RecommendedApproverRoles = new[]
    {
        "Change Manager",
        "CAB Member",
        "Governance Officer",
        "Compliance Officer",
        "Risk Officer",
        "Business Owner"
    };

    private static readonly string[] DisallowedApproverRoles =
    {
        "Global Administrator",
        "Purview Administrator",
        "Security Administrator",
        "Compliance Administrator",
        "Application Administrator",
        "Deployment Operator"
    };

    public static void Validate(string operatorName, string approver, string approverRole)
    {
        if (string.IsNullOrWhiteSpace(approver))
            throw new InvalidOperationException("An approver is required; operators may not self-approve.");

        if (!string.IsNullOrWhiteSpace(operatorName) &&
            string.Equals(operatorName.Trim(), approver.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Separation of duties: the approver must be a different person from the operator.");

        if (string.IsNullOrWhiteSpace(approverRole))
            throw new InvalidOperationException("The approver's role is required to evidence separation of duties.");

        if (DisallowedApproverRoles.Any(role =>
                string.Equals(role, approverRole.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"Separation of duties: {approverRole} cannot approve a change they can also administer. Use a change manager, CAB member, governance/compliance/risk officer or business owner.");
    }

    public static bool IsRecommendedRole(string role) =>
        RecommendedApproverRoles.Any(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));
}
