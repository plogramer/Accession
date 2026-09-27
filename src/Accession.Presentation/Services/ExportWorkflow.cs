using System.Globalization;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Accession.Data.Audit;
using Accession.Data.Browsing;
using Accession.Data.Export;
using Accession.Data.Repositories;
using Accession.Presentation.Platform;
using Accession.Presentation.ViewModels;
using Accession.UI.Components;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.Services;

/// <summary>
/// Excel export from the UI (requirements 5.9, section 8.15): the Export dialog, then the export in the background with
/// progress and Cancel, then an offer to open the folder. Entry points: the "…" menu, the Media screen (selected media),
/// the Files screen (current view) and the Audit Log screen.
/// </summary>
public sealed class ExportWorkflow(
    InventoryHost host,
    ExportService export,
    ISettingsService settings,
    IDialogService dialogs,
    BusyTracker busy,
    ToastService toasts,
    IDesktop desktop,
    IUiDispatcher ui,
    TimeProvider time,
    ILogger<ExportWorkflow> logger)
{
    public bool CanExport => host.HasSession;

    /// <summary>Shows the Export dialog and runs the export.</summary>
    /// <param name="selectedMedia">Media to preselect (Media screen).</param>
    /// <param name="filesView">The Files screen's filter, offered as the "Current Files view" scope.</param>
    /// <param name="filesViewText">A short description of that filter.</param>
    public async Task ExportAsync(IReadOnlyCollection<long>? selectedMedia = null, FileFilter? filesView = null, string? filesViewText = null)
    {
        if (host.Session is not { } session)
        {
            return;
        }

        IReadOnlyList<(long Key, string MediaId)> media;
        using (var scope = session.Database.Open())
        {
            media = [.. new MediaRepository(scope).ListActive().OrderBy(m => m.MediaId, StringComparer.OrdinalIgnoreCase).Select(m => (m.MediaKey, m.MediaId))];
        }

        var dialog = new ExportViewModel(session, export, settings, dialogs, ui, logger, media, time.GetUtcNow(), selectedMedia, filesView, filesViewText);
        if (dialogs.ShowDialog(dialog) != true || dialog.Request is not { } request)
        {
            return;
        }

        var result = await RunAsync((token, progress) => export.Export(session, request, progress, token));
        if (result is not null)
        {
            Finished(result.TotalRows, [.. result.Workbooks.Select(w => w.Path)]);
        }
    }

    /// <summary>Exports the Audit Log entries matching the screen's filter (AUD-04).</summary>
    public async Task ExportAuditLogAsync(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (host.Session is not { } session)
        {
            return;
        }

        var config = session.Config;
        var name = $"{config.ClientCode}_{config.MatterCode}_AuditLog_{time.GetUtcNow().ToLocalTime():yyyyMMdd}.xlsx";
        var invalid = Path.GetInvalidFileNameChars();
        name = new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]);
        var path = dialogs.PickSaveFile("Export audit log", "Excel workbook (*.xlsx)|*.xlsx", name, settings.Current.ResolveExportFolder());
        if (path is null)
        {
            return;
        }

        var workbook = await RunAsync((token, progress) => export.ExportAuditLog(session, query, path, progress, token));
        if (workbook is not null)
        {
            Finished(workbook.TotalRows, [workbook.Path]);
        }
    }

    /// <summary>Runs an export under the busy overlay with progress and Cancel. Null when cancelled or failed.</summary>
    private async Task<T?> RunAsync<T>(Func<CancellationToken, IProgress<ExportProgress>, T> work)
        where T : class
    {
        const string start = "Exporting to Excel…";
        try
        {
            return await busy.RunAsync((token, update) => Task.FromResult(work(token, new ProgressText(update))), start, cancellable: true);
        }
        catch (OperationCanceledException)
        {
            toasts.Show("Export cancelled. No workbook was kept.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException
                                       or InvalidOperationException or ArgumentException)
        {
            logger.LogError(ex, "Export failed");
            dialogs.ShowError("Export failed", $"The export could not be written: {ex.Message}\n\nNo workbook was kept.", ex);
        }

        return null;
    }

    private void Finished(long rows, IReadOnlyList<string> paths)
    {
        var culture = CultureInfo.CurrentCulture;
        var where = paths.Count == 1 ? paths[0] : $"{paths.Count.ToString("N0", culture)} workbooks in {Path.GetDirectoryName(paths[0])}";
        if (dialogs.Confirm("Export finished", $"{rows.ToString("N0", culture)} rows were exported to:\n{where}\n\nOpen the folder?"))
        {
            desktop.SelectInExplorer(paths[0]);
        }
    }

    /// <summary>Turns export progress into the busy overlay's message ("1,234 of 5,678 rows (21 %)").</summary>
    private sealed class ProgressText(Action<string> update) : IProgress<ExportProgress>
    {
        private int _lastPercent = -1;

        public void Report(ExportProgress value)
        {
            var culture = CultureInfo.CurrentCulture;
            var percent = value.TotalRows <= 0 ? 0 : (int)Math.Min(100, 100 * value.RowsWritten / value.TotalRows);
            if (percent == _lastPercent && value.RowsWritten % 50_000 != 0)
            {
                return;
            }

            _lastPercent = percent;
            update($"Exporting to Excel… {value.RowsWritten.ToString("N0", culture)} of {value.TotalRows.ToString("N0", culture)} rows ({percent} %)");
        }
    }
}
