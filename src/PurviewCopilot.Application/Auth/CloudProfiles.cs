using PurviewCopilot.Domain;

namespace PurviewCopilot.Application.Auth;

/// <summary>
/// Endpoints for the Microsoft national clouds. GCC uses the worldwide endpoints; GCC High and
/// DoD use Azure Government identity, their own Graph host and separate compliance PowerShell.
/// </summary>
public sealed record CloudProfile(
    string Name,
    Uri AuthorityHost,
    string GraphBaseUrl,
    string GraphBetaUrl,
    string GraphScope,
    string? ComplianceConnectionUri,
    string? ComplianceAuthorizationUri);

public static class CloudProfiles
{
    private static readonly CloudProfile Worldwide = new(
        "Commercial / GCC",
        new Uri("https://login.microsoftonline.com/"),
        "https://graph.microsoft.com/v1.0",
        "https://graph.microsoft.com/beta",
        "https://graph.microsoft.com/.default",
        null,
        null);

    private static readonly CloudProfile GccHigh = new(
        "GCC High",
        new Uri("https://login.microsoftonline.us/"),
        "https://graph.microsoft.us/v1.0",
        "https://graph.microsoft.us/beta",
        "https://graph.microsoft.us/.default",
        "https://ps.compliance.protection.office365.us/powershell-liveid/",
        "https://login.microsoftonline.us/organizations");

    private static readonly CloudProfile Dod = new(
        "DoD",
        new Uri("https://login.microsoftonline.us/"),
        "https://dod-graph.microsoft.us/v1.0",
        "https://dod-graph.microsoft.us/beta",
        "https://dod-graph.microsoft.us/.default",
        "https://l5.ps.compliance.protection.office365.us/powershell-liveid/",
        "https://login.microsoftonline.us/organizations");

    public static CloudProfile For(CloudEnvironment cloud) => cloud switch
    {
        CloudEnvironment.GccHigh => GccHigh,
        CloudEnvironment.Dod => Dod,
        _ => Worldwide
    };
}
