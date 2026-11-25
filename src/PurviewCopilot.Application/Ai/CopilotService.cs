using System.Text;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Domain;
using PurviewCopilot.Domain.Privacy;

namespace PurviewCopilot.Application.Ai;

public sealed class PrivacyGuardException : Exception
{
    public PrivacyGuardException(string message, IReadOnlyList<SensitiveMatch> matches) : base(message)
    {
        Matches = matches;
    }

    public IReadOnlyList<SensitiveMatch> Matches { get; }
}

public sealed record PreparedPrompt(
    string SystemPrompt,
    string UserPrompt,
    IReadOnlyList<SensitiveMatch> Redactions,
    IReadOnlyList<AiProviderDescriptor> Providers,
    bool Blocked);

public sealed record CopilotAnswer(
    string Content,
    string ProviderId,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    double EstimatedCostUsd,
    IReadOnlyList<SensitiveMatch> Redactions);

/// <summary>
/// Mandatory privacy gate between the application and any AI provider. Raw tenant data never
/// reaches this class' outbound path: every prompt is sanitized, and blocked content never leaves.
/// </summary>
public sealed class CopilotService
{
    public const string SystemPrompt =
        """
        You are Purview Governance Copilot, a principal Microsoft Purview and data-security architect.
        Answer for an enterprise administrator. Be specific, cite Microsoft Purview capability names,
        and reference ISO 27001, NIST CSF, CIS Controls, SOC 2, HIPAA, GDPR or PCI-DSS where relevant.
        Never invent tenant facts: use only the assessment context provided. If context is missing, say
        what must be collected. Produce short, ordered, actionable steps. No marketing language.
        """;

    private readonly AiFailoverOrchestrator _orchestrator;
    private readonly ISettingsStore _settings;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public CopilotService(
        AiFailoverOrchestrator orchestrator,
        ISettingsStore settings,
        IAuditLog audit,
        IClock clock)
    {
        _orchestrator = orchestrator;
        _settings = settings;
        _audit = audit;
        _clock = clock;
    }

    public async Task<PreparedPrompt> PrepareAsync(
        string question,
        AssessmentResult? assessment,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        var guard = new RedactionService(settings.RedactionMode);

        var context = BuildContext(assessment);
        var combined = string.IsNullOrWhiteSpace(context)
            ? question
            : $"{question}{Environment.NewLine}{Environment.NewLine}Assessment context:{Environment.NewLine}{context}";

        var redacted = guard.Apply(combined);
        var providers = SelectProviders(settings);

        if (redacted.Blocked)
        {
            await _audit.WriteAsync(new AuditEntry(
                Guid.NewGuid(), _clock.UtcNow, "ai-privacy-guard", "PromptBlocked", "ai",
                $"Blocked {redacted.Matches.Count} sensitive match(es): {Summarize(redacted.Matches)}", null),
                cancellationToken).ConfigureAwait(false);

            throw new PrivacyGuardException(
                "The prompt contains secrets, credentials or tenant identifiers and was blocked. Remove them or switch the privacy mode.",
                redacted.Matches);
        }

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, "ai-privacy-guard", "PromptSanitized", "ai",
            $"Mode={settings.RedactionMode}; Matches={redacted.Matches.Count}; Providers={providers.Count}", null),
            cancellationToken).ConfigureAwait(false);

        return new PreparedPrompt(SystemPrompt, redacted.Text, redacted.Matches, providers, false);
    }

    public async Task<CopilotAnswer> SendAsync(PreparedPrompt prepared, CancellationToken cancellationToken = default)
    {
        var response = await _orchestrator.CompleteAsync(
            prepared.Providers,
            new AiRequest(prepared.SystemPrompt, prepared.UserPrompt),
            cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry(
            Guid.NewGuid(), _clock.UtcNow, "ai-orchestrator", "AiCompletion", response.ProviderId,
            $"Tokens={response.PromptTokens + response.CompletionTokens}; CostUsd={response.EstimatedCostUsd:0.######}",
            null), cancellationToken).ConfigureAwait(false);

        return new CopilotAnswer(
            response.Content, response.ProviderId, response.Model,
            response.PromptTokens, response.CompletionTokens, response.EstimatedCostUsd,
            prepared.Redactions);
    }

    public async Task<CopilotAnswer> AskAsync(
        string question,
        AssessmentResult? assessment,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(question, assessment, cancellationToken).ConfigureAwait(false);
        return await SendAsync(prepared, cancellationToken).ConfigureAwait(false);
    }

    private static List<AiProviderDescriptor> SelectProviders(AppSettings settings)
    {
        var providers = settings.Providers.Where(p => p.Enabled).ToList();

        if (settings.OfflineMode || !settings.CloudAiEnabled)
        {
            providers = providers.Where(p => p.IsLocal).ToList();
        }

        if (providers.Count == 0)
        {
            throw new InvalidOperationException(
                settings.OfflineMode || !settings.CloudAiEnabled
                    ? "No local AI provider is enabled. Enable Ollama or LM Studio for offline operation."
                    : "No AI provider is enabled.");
        }

        return providers;
    }

    private static string BuildContext(AssessmentResult? assessment)
    {
        if (assessment is null) return string.Empty;

        var builder = new StringBuilder();
        var scores = assessment.Scores;

        builder.AppendLine($"Overall={scores.Overall:0.#}; Security={scores.Security:0.#}; Governance={scores.Governance:0.#}; Compliance={scores.Compliance:0.#}; Risk={scores.Risk:0.#}; Maturity={scores.MaturityLevel}; CopilotReadiness={scores.CopilotReadiness:0.#}");

        foreach (var finding in assessment.Findings
                     .Where(f => f.Status is FindingStatus.Fail or FindingStatus.Warning)
                     .OrderByDescending(f => f.Severity)
                     .Take(25))
        {
            builder.AppendLine($"- [{finding.Status}] {finding.Id} ({finding.Module}): {finding.Title}. Evidence: {finding.Evidence}");
        }

        return builder.ToString();
    }

    private static string Summarize(IReadOnlyList<SensitiveMatch> matches) =>
        string.Join(", ", matches.Select(m => m.RuleId).Distinct(StringComparer.Ordinal));
}
