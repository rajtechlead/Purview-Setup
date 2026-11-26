using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Deployment;

/// <summary>
/// The behaviour each deployment mode promises. Keeping it here (rather than in the view model)
/// lets the modes be tested without a WPF dispatcher.
/// </summary>
public static class DeploymentModePolicy
{
    /// <summary>Assessment only never produces a change plan.</summary>
    public static bool AllowsPlan(DeploymentMode mode) => mode != DeploymentMode.AssessmentOnly;

    /// <summary>One-click builds the recommended plan as soon as the mode is chosen.</summary>
    public static bool RequiresAutomaticPlan(DeploymentMode mode) => mode == DeploymentMode.OneClickRecommended;

    public static bool Preselect(DeploymentMode mode, string recommendationId, Severity priority, bool greenfield) => mode switch
    {
        DeploymentMode.AssessmentOnly => false,
        DeploymentMode.OneClickRecommended => priority >= Severity.High,
        DeploymentMode.GuidedDeployment => priority >= Severity.High,
        DeploymentMode.Custom => false,
        DeploymentMode.Expert => true,
        DeploymentMode.Greenfield => greenfield && GreenfieldBaseline.IsFoundation(recommendationId),
        _ => false
    };

    public static string Describe(DeploymentMode mode, bool greenfield) => mode switch
    {
        DeploymentMode.AssessmentOnly => "Assessment only: evidence is collected and scored, but no change plan can be built in this mode.",
        DeploymentMode.OneClickRecommended => "One-click: high and critical recommendations are preselected and the plan is built automatically. Simulation and approvals still apply.",
        DeploymentMode.Custom => "Custom: nothing is preselected; choose exactly the recommendations you want.",
        DeploymentMode.Expert => "Expert: every recommendation is preselected for full control; edit the selection before building the plan.",
        DeploymentMode.Greenfield => greenfield
            ? "Greenfield tenant detected. The first-run baseline preselects labels, label policy, auto-labelling, DLP, retention and audit retention in a safe starting order."
            : "Greenfield expects an empty tenant; existing labels or policies were found, so nothing is preselected as a first-run baseline.",
        _ => "Guided: high and critical recommendations are preselected; adjust the selection before building the plan."
    };
}
