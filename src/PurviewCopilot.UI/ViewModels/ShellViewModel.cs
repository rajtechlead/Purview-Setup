using System.Collections.ObjectModel;
using System.Windows.Input;
using PurviewCopilot.Application.Workspace;
using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    private readonly GovernanceWorkspace _workspace;
    private PageViewModel _currentPage;
    private string _statusMessage = "Ready. No tenant connected.";

    public ShellViewModel(GovernanceWorkspace workspace, IEnumerable<PageViewModel> pages)
    {
        _workspace = workspace;
        var list = pages.ToList();
        Navigation = new ObservableCollection<NavigationItem>(list.Select(p => new NavigationItem(p.Title, GlyphFor(p), p)));
        _currentPage = list.First();
        NavigateCommand = new AsyncRelayCommand(async parameter =>
        {
            if (parameter is NavigationItem item)
            {
                await NavigateAsync(item.Page).ConfigureAwait(true);
            }
        });
    }

    public ObservableCollection<NavigationItem> Navigation { get; }

    public PageViewModel CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                Raise(nameof(CurrentTitle));
                Raise(nameof(CurrentSubtitle));
            }
        }
    }

    public string CurrentTitle => CurrentPage.Title;
    public string CurrentSubtitle => CurrentPage.Subtitle;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string TenantLabel =>
        _workspace.Tenant is null ? "No tenant connected" : _workspace.Tenant.TenantName;

    public string ScoreLabel =>
        _workspace.Assessment is null ? "Not assessed" : $"{_workspace.Assessment.Scores.Overall:0.#} / 100";

    public string AiModeLabel { get; set; } = "Cloud AI: review required";

    public ICommand NavigateCommand { get; }

    public async Task InitializeAsync()
    {
        await CurrentPage.RefreshAsync().ConfigureAwait(true);
        Raise(nameof(TenantLabel));
        Raise(nameof(ScoreLabel));
    }

    public async Task NavigateAsync(PageViewModel page)
    {
        CurrentPage = page;
        await page.RefreshAsync().ConfigureAwait(true);
        Raise(nameof(TenantLabel));
        Raise(nameof(ScoreLabel));
    }

    public void NotifyStateChanged()
    {
        Raise(nameof(TenantLabel));
        Raise(nameof(ScoreLabel));
    }

    private static string GlyphFor(PageViewModel page) => page switch
    {
        DashboardViewModel => "\uE80F",
        AssessmentViewModel => "\uE9D9",
        DeploymentViewModel => "\uE8B7",
        MonitoringViewModel => "\uE9D2",
        ReportsViewModel => "\uE9F9",
        AiAssistantViewModel => "\uE99A",
        SettingsViewModel => "\uE713",
        HelpViewModel => "\uE897",
        _ => "\uE946"
    };
}
