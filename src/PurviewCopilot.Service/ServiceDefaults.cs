using PurviewCopilot.Application.Approvals;
using PurviewCopilot.Collaboration;

namespace PurviewCopilot.Service;

public static class ServiceDefaults
{
    /// <summary>
    /// Customer-hosted data location. ProgramData is used on Windows so a service account can write
    /// it without an interactive profile; other hosts fall back to a data folder beside the binary.
    /// </summary>
    public static string DefaultDatabasePath(IConfiguration configuration)
    {
        var directory = configuration["Collaboration:DataDirectory"];

        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "PurviewGovernanceCopilot")
                : Path.Combine(AppContext.BaseDirectory, "data");
        }

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "collaboration.db");
    }

    public static CollaborationLimits ReadLimits(IConfiguration configuration)
    {
        var maxPayload = configuration.GetValue("Collaboration:MaxPayloadBytes", 1_048_576);
        var maxOperations = configuration.GetValue("Collaboration:MaxOperations", 500);
        var maxPage = configuration.GetValue("Collaboration:MaxPageSize", 200);

        return new CollaborationLimits(
            Math.Clamp(maxPayload, 1_024, 16 * 1_048_576),
            Math.Clamp(maxOperations, 1, 5_000),
            Math.Clamp(maxPage, 1, 1_000));
    }

    /// <summary>
    /// Distinct approvers required at each stage. Invalid values fail startup rather than silently
    /// falling back to a weaker rule.
    /// </summary>
    public static ApprovalPolicy ReadApprovalPolicy(IConfiguration configuration)
    {
        var policy = new ApprovalPolicy(
            configuration.GetValue("Collaboration:Approval:Security", 1),
            configuration.GetValue("Collaboration:Approval:Compliance", 1),
            configuration.GetValue("Collaboration:Approval:Cab", 1));

        policy.Validate();
        return policy;
    }

    /// <summary>
    /// A production service must serve HTTPS. An isolated lab can opt out explicitly, and test hosts
    /// run without TLS because the in-memory transport never leaves the process.
    /// </summary>
    public static bool HttpsRequired(IConfiguration configuration, IHostEnvironment environment) =>
        configuration.GetValue("Service:RequireHttps", true) &&
        !environment.IsEnvironment("Testing") &&
        !environment.IsDevelopment();
}
