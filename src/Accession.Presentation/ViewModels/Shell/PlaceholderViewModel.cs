using Accession.Presentation.Mvvm;

namespace Accession.Presentation.ViewModels.Shell;

/// <summary>Stand-in content for screens that are not built yet.</summary>
public sealed class PlaceholderViewModel(string title, string description, string plannedIn) : ViewModelBase
{
    public string Title { get; } = title;

    public string Description { get; } = description;

    public string PlannedIn { get; } = plannedIn;
}
