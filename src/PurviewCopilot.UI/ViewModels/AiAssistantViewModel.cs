using System.Collections.ObjectModel;
using System.Windows.Input;
using PurviewCopilot.Application.Abstractions;
using PurviewCopilot.Application.Ai;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.Domain;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed record ChatMessage(string Role, string Text);

public sealed class AiAssistantViewModel : PageViewModel
{
    private readonly CopilotService _copilot;
    private readonly GovernanceWorkspace _workspace;
    private readonly IAiUsageStore _usage;
    private readonly ISettingsStore _settings;
    private PreparedPrompt? _prepared;
    private string _question = "What are the three highest-risk Purview gaps for this tenant, and what should I fix first?";
    private string _promptPreview = string.Empty;
    private string _redactionSummary = "Nothing prepared yet.";
    private string _status = "Prompts are sanitized locally before any provider call.";
    private string _usageSummary = string.Empty;
    private bool _busy;
    private bool _cloudDisabled;

    public AiAssistantViewModel(
        CopilotService copilot,
        GovernanceWorkspace workspace,
        IAiUsageStore usage,
        ISettingsStore settings)
    {
        _copilot = copilot;
        _workspace = workspace;
        _usage = usage;
        _settings = settings;

        PrepareCommand = new AsyncRelayCommand(PrepareAsync, () => !Busy);
        SendCommand = new AsyncRelayCommand(SendAsync, () => !Busy && _prepared is not null);
        AskCommand = new AsyncRelayCommand(AskAsync, () => !Busy);
    }

    public override string Title => "AI Assistant";
    public override string Subtitle => "Governance guidance with mandatory local redaction and a complete audit trail.";

    public ObservableCollection<ChatMessage> Transcript { get; } = new();

    public string Question { get => _question; set => SetProperty(ref _question, value); }
    public string PromptPreview { get => _promptPreview; private set => SetProperty(ref _promptPreview, value); }
    public string RedactionSummary { get => _redactionSummary; private set => SetProperty(ref _redactionSummary, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string UsageSummary { get => _usageSummary; private set => SetProperty(ref _usageSummary, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public bool CloudDisabled { get => _cloudDisabled; private set => SetProperty(ref _cloudDisabled, value); }

    public ICommand PrepareCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand AskCommand { get; }

    public override async Task RefreshAsync()
    {
        var settings = await _settings.LoadAsync().ConfigureAwait(true);
        CloudDisabled = settings.OfflineMode || !settings.CloudAiEnabled;

        var usage = await _usage.ListAsync().ConfigureAwait(true);
        UsageSummary = $"{usage.Count} call(s) · {usage.Sum(u => u.PromptTokens + u.CompletionTokens):N0} tokens · ${usage.Sum(u => u.EstimatedCostUsd):0.0000} estimated";

        if (Transcript.Count == 0)
        {
            Transcript.Add(new ChatMessage("system",
                "Local privacy guard is active. Sensitive values are tokenized or redacted before transmission; blocked prompts never leave this machine."));
        }
    }

    private async Task PrepareAsync()
    {
        Busy = true;
        try
        {
            _prepared = await _copilot.PrepareAsync(Question, _workspace.Assessment).ConfigureAwait(true);
            PromptPreview = _prepared.UserPrompt;
            RedactionSummary = _prepared.Redactions.Count == 0
                ? "No sensitive data detected in the prompt."
                : $"{_prepared.Redactions.Count} sensitive value(s) sanitized: {string.Join(", ", _prepared.Redactions.Select(r => r.RuleId).Distinct())}";
            Status = _prepared.Providers.Count == 0
                ? "No provider is enabled."
                : $"Prepared for {_prepared.Providers.Count} approved provider(s). Review the sanitized prompt before sending.";
        }
        catch (PrivacyGuardException ex)
        {
            PromptPreview = string.Empty;
            RedactionSummary = ex.Message;
            Status = "Prompt blocked by the privacy guard. Nothing was sent.";
        }
        catch (Exception ex)
        {
            Status = $"Preparation failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task SendAsync()
    {
        if (_prepared is null)
        {
            Status = "Prepare the prompt first.";
            return;
        }

        Busy = true;
        try
        {
            var answer = await _copilot.SendAsync(_prepared).ConfigureAwait(true);
            Transcript.Add(new ChatMessage("you", Question));
            Transcript.Add(new ChatMessage("assistant", answer.Content));
            Status = $"Answered by {answer.ProviderId} ({answer.Model}) · {answer.PromptTokens + answer.CompletionTokens} tokens · ${answer.EstimatedCostUsd:0.0000}";
            _prepared = null;
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Status = $"AI call failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task AskAsync()
    {
        await PrepareAsync().ConfigureAwait(true);
        if (_prepared is not null) await SendAsync().ConfigureAwait(true);
    }
}
