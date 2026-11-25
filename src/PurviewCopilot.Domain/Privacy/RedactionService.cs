using System.Text;
using System.Text.RegularExpressions;

namespace PurviewCopilot.Domain.Privacy;

/// <summary>
/// Applies the configured privacy mode to outbound text. Tokenization is deterministic
/// within a single call so downstream correlating of the same value still works, while the
/// real value never leaves the machine. The token map is retained locally for audit review.
/// </summary>
public sealed class RedactionService
{
    private readonly AiRedactionMode _mode;

    public RedactionService(AiRedactionMode mode = AiRedactionMode.Tokenize) => _mode = mode;

    public RedactionResult Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new RedactionResult(string.Empty, Array.Empty<SensitiveMatch>(), new Dictionary<string, string>(), false);
        }

        var matches = SensitiveDataDetector.Detect(text);
        if (matches.Count == 0)
        {
            return new RedactionResult(text, matches, new Dictionary<string, string>(), false);
        }

        if (_mode == AiRedactionMode.Block)
        {
            var blocked = matches.Any(m => m.Category is SensitiveCategory.Secret or SensitiveCategory.Credential or SensitiveCategory.TenantIdentifier)
                          || matches.Count >= 3;
            return new RedactionResult(text, matches, new Dictionary<string, string>(), blocked);
        }

        var tokenMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        var builder = new StringBuilder(text);

        foreach (var match in matches.OrderByDescending(m => m.Start))
        {
            string replacement;

            switch (_mode)
            {
                case AiRedactionMode.Mask:
                    replacement = Mask(match.Value);
                    break;

                case AiRedactionMode.Redact:
                    replacement = $"[REDACTED:{match.Category.ToString().ToUpperInvariant()}]";
                    break;

                default:
                {
                    if (!tokenMap.TryGetValue(match.Value, out var token))
                    {
                        counters.TryGetValue(match.RuleId, out var count);
                        counters[match.RuleId] = ++count;
                        token = $"<<{match.RuleId}_{count}>>";
                        tokenMap[match.Value] = token;
                    }

                    replacement = token;
                    break;
                }
            }

            builder.Remove(match.Start, match.Length);
            builder.Insert(match.Start, replacement);
        }

        return new RedactionResult(builder.ToString(), matches, tokenMap, false);
    }

    /// <summary>Reverses tokenization locally, for administrator review of a logged prompt.</summary>
    public static string Restore(string text, IReadOnlyDictionary<string, string> tokenMap)
    {
        if (string.IsNullOrEmpty(text) || tokenMap.Count == 0) return text;

        var restored = text;
        foreach (var pair in tokenMap.OrderByDescending(p => p.Value.Length))
        {
            restored = restored.Replace(pair.Value, pair.Key, StringComparison.Ordinal);
        }

        return restored;
    }

    private static string Mask(string value)
    {
        if (value.Length <= 4) return new string('*', value.Length);
        return new string('*', value.Length - 4) + value[^4..];
    }

    /// <summary>Redacts secrets from log lines without tokenizing (logs must stay readable).</summary>
    public static string ScrubForLog(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var scrubbed = text;
        foreach (var pattern in new[]
                 {
                     @"\bBearer\s+[A-Za-z0-9\-._~+/]{8,}=*",
                     @"(?i)\b(?:AccountKey|SharedAccessKey|client_secret|password|pwd)\s*=\s*[^;\s""']{4,}",
                     @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b"
                 })
        {
            scrubbed = Regex.Replace(scrubbed, pattern, "[REDACTED:SECRET]", RegexOptions.None, TimeSpan.FromMilliseconds(200));
        }

        return scrubbed;
    }
}
