using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Data.SavedSearches;
using Accession.Data.Sessions;
using Accession.Presentation.Mvvm;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Accession.Presentation.ViewModels;

/// <summary>New / edit saved search dialog: name (required, unique) and description.</summary>
public sealed partial class SavedSearchViewModel : DialogViewModelBase
{
    private readonly InventorySession _session;
    private readonly SavedSearchService _service;
    private readonly long? _savedSearchId;
    private bool _tried;

    /// <param name="existing">The saved search to edit, or null for a new one.</param>
    public SavedSearchViewModel(InventorySession session, SavedSearchService service, SavedSearchInfo? existing = null,
        DisplayTimeZone zone = DisplayTimeZone.Local)
    {
        if (existing is not null)
        {
            CreatedText = Describe("Created", existing.CreatedBy, existing.CreatedOnMachine, existing.CreatedAtUtc, zone);
            ChangedText = Describe("Last changed", existing.ModifiedBy, existing.ModifiedOnMachine, existing.ModifiedAtUtc, zone);
        }

        _session = session;
        _service = service;
        _savedSearchId = existing?.SavedSearchId;
        Title = existing is null ? "New saved search" : "Edit saved search";
        Name = existing?.Name ?? string.Empty;
        Description = existing?.Description ?? string.Empty;
    }

    public bool IsNew => _savedSearchId is null;

    /// <summary>"Created by CORP\jdoe on WS-114, 2026-09-27 14:10" (empty for a new saved search).</summary>
    public string CreatedText { get; } = string.Empty;

    public string ChangedText { get; } = string.Empty;

    /// <summary>"Created by CORP\jdoe on WS-114, 2026-09-27 14:10"; the computer is left out when it is not known.</summary>
    public static string Describe(string what, string user, string? machine, DateTimeOffset atUtc, DisplayTimeZone zone) =>
        $"{what} by {user}{(string.IsNullOrEmpty(machine) ? string.Empty : $" on {machine}")}, {TimeFormatter.Format(atUtc, zone)}";

    public string SaveText => IsNew ? "Create" : "Save";

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    /// <summary>What is wrong with the name; shown once the user tried to save.</summary>
    [ObservableProperty]
    public partial string NameError { get; private set; } = string.Empty;

    /// <summary>The saved search's id after saving.</summary>
    public long? SavedSearchId { get; private set; }

    partial void OnNameChanged(string value)
    {
        if (_tried)
        {
            NameError = SavedSearchService.ValidateName(value);
        }
    }

    [RelayCommand]
    private void Save()
    {
        _tried = true;
        NameError = SavedSearchService.ValidateName(Name);
        if (NameError.Length > 0)
        {
            return;
        }

        try
        {
            if (_savedSearchId is { } id)
            {
                _service.Update(_session, id, Name, Description);
                SavedSearchId = id;
            }
            else
            {
                SavedSearchId = _service.Create(_session, Name, Description);
            }
        }
        catch (SavedSearchNameTakenException)
        {
            NameError = "Another saved search already has this name.";
            return;
        }

        Close(true);
    }

    [RelayCommand]
    private void Cancel() => Close(false);
}
