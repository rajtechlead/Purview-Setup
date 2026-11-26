using System.Text.RegularExpressions;

namespace PurviewCopilot.Domain.Privacy;

/// <summary>
/// Detects PII, secrets, credentials, tenant identifiers and customer URLs before any
/// prompt leaves the process. Detection is local and deterministic; nothing is sent here.
/// </summary>
public static class SensitiveDataDetector
{
    private sealed record Rule(
        string Id,
        SensitiveCategory Category,
        Regex Pattern,
        Func<string, bool>? Validate = null);

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    private static readonly Rule[] Rules =
    {
        new("EMAIL", SensitiveCategory.Pii,
            new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled, Timeout)),

        new("TENANT_GUID", SensitiveCategory.TenantIdentifier,
            new Regex(@"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b", RegexOptions.Compiled, Timeout)),

        new("TENANT_DOMAIN", SensitiveCategory.TenantIdentifier,
            new Regex(@"\b[A-Za-z0-9-]+\.onmicrosoft\.com\b", RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout)),

        new("SHAREPOINT_URL", SensitiveCategory.CustomerData,
            new Regex(@"https://[A-Za-z0-9-]+\.sharepoint\.com/[^\s""'<>]*", RegexOptions.Compiled | RegexOptions.IgnoreCase, Timeout)),

        new("CUSTOMER_LABEL", SensitiveCategory.CustomerData,
            new Regex(@"(?im)\b(?:customer|client|tenant|organisation|organization)\s*(?:name)?\s*[:=]\s*[A-Z][^.\r\n,;]{2,60}", RegexOptions.Compiled, Timeout)),

        new("COMPANY_NAME", SensitiveCategory.CustomerData,
            new Regex(@"\b[A-Z][A-Za-z0-9&.'-]+(?:\s+[A-Z][A-Za-z0-9&.'-]+){0,4}\s+(?:Ltd|Limited|Inc|LLC|PLC|GmbH|AG|Pty|Corp|Corporation|Company|S\.A\.)\b", RegexOptions.Compiled, Timeout)),

        new("PERSON_NAME", SensitiveCategory.CustomerData,
            new Regex(@"\b(?:Mr|Mrs|Ms|Miss|Dr|Prof)\.?\s+[A-Z][a-z]+(?:\s+[A-Z][a-z]+){0,2}\b", RegexOptions.Compiled, Timeout)),

        new("BEARER_TOKEN", SensitiveCategory.Secret,
            new Regex(@"\bBearer\s+[A-Za-z0-9\-._~+/]{16,}=*", RegexOptions.Compiled, Timeout)),

        new("JWT", SensitiveCategory.Secret,
            new Regex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b", RegexOptions.Compiled, Timeout)),

        new("AWS_ACCESS_KEY", SensitiveCategory.Secret,
            new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.Compiled, Timeout)),

        new("PRIVATE_KEY", SensitiveCategory.Secret,
            new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled, Timeout)),

        new("CONNECTION_SECRET", SensitiveCategory.Credential,
            new Regex(@"(?i)\b(?:AccountKey|SharedAccessKey|client_secret|clientsecret|password|pwd)\s*=\s*[^;\s""']{6,}", RegexOptions.Compiled, Timeout)),

        new("SSN", SensitiveCategory.Pii,
            new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled, Timeout)),

        new("CREDIT_CARD", SensitiveCategory.Financial,
            new Regex(@"\b(?:\d[ -]?){13,19}\b", RegexOptions.Compiled, Timeout),
            value => PassesLuhn(value)),

        new("IBAN", SensitiveCategory.Financial,
            new Regex(@"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b", RegexOptions.Compiled, Timeout)),

        new("PHONE", SensitiveCategory.Pii,
            new Regex(@"(?<![\w])(?:\+\d{1,3}[\s.-]?)?(?:\(\d{3}\)|\d{3})[\s.-]?\d{3}[\s.-]?\d{4}(?![\w])", RegexOptions.Compiled, Timeout))
    };

    public static IReadOnlyList<SensitiveMatch> Detect(string? text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<SensitiveMatch>();

        var matches = new List<SensitiveMatch>();

        foreach (var rule in Rules)
        {
            foreach (Match match in rule.Pattern.Matches(text))
            {
                if (!match.Success || match.Length == 0) continue;
                if (rule.Validate is not null && !rule.Validate(match.Value)) continue;

                matches.Add(new SensitiveMatch(rule.Id, rule.Category, match.Index, match.Length, match.Value));
            }
        }

        // Keep the earliest, longest match per region so overlapping rules cannot
        // produce duplicated or partially masked values.
        var ordered = matches.OrderBy(m => m.Start).ThenByDescending(m => m.Length).ToList();
        var deduplicated = new List<SensitiveMatch>();
        var lastEnd = -1;

        foreach (var match in ordered)
        {
            if (match.Start < lastEnd) continue;
            deduplicated.Add(match);
            lastEnd = match.Start + match.Length;
        }

        return deduplicated;
    }

    public static bool ContainsSecrets(string? text) =>
        Detect(text).Any(m => m.Category is SensitiveCategory.Secret or SensitiveCategory.Credential);

    private static bool PassesLuhn(string value)
    {
        var digits = value.Where(char.IsDigit).Select(c => c - '0').ToArray();
        if (digits.Length is < 13 or > 19) return false;

        var sum = 0;
        var doubleDigit = false;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var digit = digits[i];
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
