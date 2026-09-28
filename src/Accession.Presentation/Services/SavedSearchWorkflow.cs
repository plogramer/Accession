using System.Globalization;
using Accession.Core.Threading;
using Accession.Data.Browsing;
using Accession.Data.SavedSearches;
using Accession.Presentation.ViewModels;
using Accession.UI.Components;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.Services;

/// <summary>
/// Saved searches from the Files screen: the new/edit dialog, delete, and adding or removing files (in the background,
/// since "all results" can be millions of files), with a notification of the outcome.
/// </summary>
public sealed class SavedSearchWorkflow(
    InventoryHost host,
    SavedSearchService service,
    IDialogService dialogs,
    BusyTracker busy,
    ToastService toasts,
    ILogger<SavedSearchWorkflow> logger)
{
    public bool CanChange => host.Session is { IsReadOnly: false };

    public IReadOnlyList<SavedSearchInfo> List()
    {
        if (host.Session is not { } session)
        {
            return [];
        }

        try
        {
            return service.List(session.Database);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            logger.LogError(ex, "Loading saved searches failed");
            return [];
        }
    }

    /// <summary>Asks for a name and description and creates the saved search. Returns its id, or null if cancelled.</summary>
    public long? Create()
    {
        if (host.Session is not { IsReadOnly: false } session)
        {
            return null;
        }

        var dialog = new SavedSearchViewModel(session, service);
        return dialogs.ShowDialog(dialog) == true ? dialog.SavedSearchId : null;
    }

    /// <summary>Edits the name and description. Returns true if saved.</summary>
    public bool Edit(SavedSearchInfo savedSearch)
    {
        ArgumentNullException.ThrowIfNull(savedSearch);
        return host.Session is { IsReadOnly: false } session
            && dialogs.ShowDialog(new SavedSearchViewModel(session, service, savedSearch)) == true;
    }

    /// <summary>Deletes after asking. Returns true if deleted.</summary>
    public bool Delete(SavedSearchInfo savedSearch)
    {
        ArgumentNullException.ThrowIfNull(savedSearch);
        if (host.Session is not { IsReadOnly: false } session
            || !dialogs.Confirm("Delete saved search",
                $"Delete the saved search '{savedSearch.Name}' ({Files(savedSearch.FileCount)})? The files themselves are not changed."))
        {
            return false;
        }

        return Try("Delete saved search", () => service.Delete(session, savedSearch.SavedSearchId));
    }

    /// <summary>
    /// Adds the files matching <paramref name="filter"/> to a saved search, or to a new one when
    /// <paramref name="savedSearchId"/> is null. Returns the target's id, or null if cancelled or failed.
    /// </summary>
    public async Task<long?> AddAsync(long? savedSearchId, string? name, FileFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (host.Session is not { IsReadOnly: false } session)
        {
            return null;
        }

        var target = savedSearchId ?? Create();
        if (target is not { } id)
        {
            return null;
        }

        name ??= service.List(session.Database).FirstOrDefault(s => s.SavedSearchId == id)?.Name ?? "the saved search";
        try
        {
            var added = await busy.RunAsync(token => Task.FromResult(service.AddFiles(session, id, filter, token)), $"Adding files to {name}…");
            toasts.Show(added == 0 ? $"Those files are already in {name}." : $"Added {Files(added)} to {name}.", ToastKind.Success);
            return id;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            logger.LogError(ex, "Adding files to a saved search failed");
            dialogs.ShowError("Add to saved search", $"The files could not be added: {ex.Message}", ex);
            return null;
        }
    }

    /// <summary>Removes the files matching <paramref name="filter"/> from a saved search (asks first when it is more than a few).</summary>
    public async Task<bool> RemoveAsync(long savedSearchId, string name, FileFilter filter, long count)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (host.Session is not { IsReadOnly: false } session)
        {
            return false;
        }

        if (count > 1 && !dialogs.Confirm("Remove from saved search", $"Remove {Files(count)} from {name}? The files themselves are not changed."))
        {
            return false;
        }

        try
        {
            var removed = await busy.RunAsync(token => Task.FromResult(service.RemoveFiles(session, savedSearchId, filter, token)), $"Removing files from {name}…");
            toasts.Show($"Removed {Files(removed)} from {name}.", ToastKind.Success);
            return true;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            logger.LogError(ex, "Removing files from a saved search failed");
            dialogs.ShowError("Remove from saved search", $"The files could not be removed: {ex.Message}", ex);
            return false;
        }
    }

    private bool Try(string title, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            logger.LogError(ex, "{Title} failed", title);
            dialogs.ShowError(title, ex.Message, ex);
            return false;
        }
    }

    private static string Files(long count) =>
        count == 1 ? "1 file" : $"{count.ToString("N0", CultureInfo.CurrentCulture)} files";
}
