using PurviewCopilot.UI.Mvvm;

namespace PurviewCopilot.UI.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Subtitle { get; }

    public virtual Task RefreshAsync() => Task.CompletedTask;
}

public sealed record NavigationItem(string Label, string Glyph, PageViewModel Page);
