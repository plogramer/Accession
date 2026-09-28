using System.Globalization;
using Accession.Core.Formatting;
using Accession.Core.Settings;
using Accession.Core.Threading;
using Accession.Data.Browsing;
using Accession.Data.Copying;
using Accession.Presentation.Platform;
using Accession.Presentation.ViewModels;
using Accession.UI.Components;
using Microsoft.Extensions.Logging;

namespace Accession.Presentation.Services;

/// <summary>
/// Copying files out of the evidence from the Files screen (requirements 5.8b): the dialog, then the batch or the copy in
/// the background with progress and Cancel, then the outcome. The dialog's choices are offered again next time.
/// </summary>
public sealed class CopyWorkflow(
    InventoryHost host,
    CopyService copy,
    ISettingsService settings,
    IDialogService dialogs,
    BusyTracker busy,
    ToastService toasts,
    IDesktop desktop,
    IUiDispatcher ui,
    TimeProvider time,
    ILogger<CopyWorkflow> logger)
{
    private CopyDialogChoices? _last;
    private CopyDialogChoices? _lastCopyTo;

    public bool CanCopy => host.HasSession;

    /// <summary>Asks for the options and writes the copy batch and its manifest.</summary>
    public async Task GenerateBatchAsync(IReadOnlyCollection<long> ticked, FileFilter allResults, string allResultsText)
    {
        if (host.Session is not { } session || Ask(CopyDialogMode.Batch, ticked, allResults, allResultsText) is not { } dialog)
        {
            return;
        }

        var request = dialog.Request!;
        var template = dialog.CurrentTemplate;
        var batchPath = dialog.BatchPath.Trim();
        try
        {
            var result = await busy.RunAsync((token, update) =>
                Task.FromResult(copy.GenerateBatch(session, request, template, batchPath, new ProgressText(update, "Writing the copy batch…", settings), token)),
                "Writing the copy batch…", cancellable: true);
            var culture = CultureInfo.CurrentCulture;
            var left = result.NotInBatch == 0 ? string.Empty
                : $"\n{result.NotInBatch.ToString("N0", culture)} files have no SHA-1 yet and were left out (listed in the manifest).";
            if (dialogs.Confirm("Copy batch written",
                    $"{result.BatchPath}\ncopies {result.Files.ToString("N0", culture)} files ({SizeFormatter.Format(result.Bytes, settings.Current.SizeUnit, provider: culture)}) " +
                    $"to {request.Destination}.{left}\n\nManifest: {result.ManifestPath}\n\nRun the batch file from a command prompt. Show it in Explorer?"))
            {
                desktop.SelectInExplorer(result.BatchPath);
            }
        }
        catch (OperationCanceledException)
        {
            toasts.Show("Cancelled. No batch file was kept.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or ArgumentException)
        {
            logger.LogError(ex, "Writing the copy batch failed");
            dialogs.ShowError("Copy batch failed", $"The copy batch could not be written: {ex.Message}\n\nNo batch file was kept.", ex);
        }
    }

    /// <summary>Asks for the options and copies the files.</summary>
    public async Task CopyFilesAsync(IReadOnlyCollection<long> ticked, FileFilter allResults, string allResultsText)
    {
        if (host.Session is not { } session || Ask(CopyDialogMode.Copy, ticked, allResults, allResultsText) is not { } dialog)
        {
            return;
        }

        var request = dialog.Request!;
        var options = dialog.Options;
        try
        {
            var result = await busy.RunAsync((token, update) =>
                Task.FromResult(copy.CopyFiles(session, request, options, new ProgressText(update, "Copying files…", settings), token)),
                "Copying files…", cancellable: true);
            Finished(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or ArgumentException)
        {
            logger.LogError(ex, "Copying files failed");
            dialogs.ShowError("Copy failed", $"The files could not be copied: {ex.Message}", ex);
        }
    }

    /// <summary>Copy To: the given files straight into one folder as &lt;sha1&gt;_&lt;name&gt; (right-click on files).</summary>
    public async Task CopyToAsync(IReadOnlyCollection<long> fileIds)
    {
        ArgumentNullException.ThrowIfNull(fileIds);
        if (fileIds.Count == 0 || host.Session is not { } session
            || Ask(CopyDialogMode.CopyTo, fileIds, FileFilter.None with { FileIds = [.. fileIds] }, string.Empty) is not { } dialog)
        {
            return;
        }

        var request = dialog.Request!;
        var options = dialog.Options;
        try
        {
            var result = await busy.RunAsync((token, update) =>
                Task.FromResult(copy.CopyFiles(session, request, options, new ProgressText(update, "Copying…", settings), token)),
                "Copying…", cancellable: true);
            Finished(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException or ArgumentException)
        {
            logger.LogError(ex, "Copy To failed");
            dialogs.ShowError("Copy failed", $"The files could not be copied: {ex.Message}", ex);
        }
    }

    private CopyViewModel? Ask(CopyDialogMode mode, IReadOnlyCollection<long> ticked, FileFilter allResults, string allResultsText)
    {
        var last = mode == CopyDialogMode.CopyTo ? _lastCopyTo : _last;
        var dialog = new CopyViewModel(mode, host.Session!, copy, settings, dialogs, ui, logger, ticked, allResults, allResultsText, time.GetUtcNow(), last);
        if (dialogs.ShowDialog(dialog) != true || dialog.Request is null)
        {
            return null;
        }

        if (mode == CopyDialogMode.CopyTo)
        {
            _lastCopyTo = dialog.Choices;
        }
        else
        {
            _last = dialog.Choices;
        }

        return dialog;
    }

    private void Finished(CopyFilesResult result)
    {
        var message = Summary(result, settings.Current.SizeUnit, CultureInfo.CurrentCulture);
        var title = result.Cancelled ? "Copy cancelled" : result.Failed > 0 ? "Copy finished with failures" : "Copy finished";
        if (dialogs.Confirm(title, message + "\n\nOpen the destination folder?"))
        {
            desktop.OpenFolder(result.Destination);
        }
    }

    /// <summary>The result as text: counts, then where the manifest is.</summary>
    internal static string Summary(CopyFilesResult result, SizeUnitSystem unit, CultureInfo culture)
    {
        string N(long value) => value.ToString("N0", culture);
        var lines = new List<string>
        {
            $"Copied: {N(result.Copied)} of {N(result.TotalFiles)} files ({SizeFormatter.Format(result.BytesCopied, unit, provider: culture)})" +
            (result.Verified > 0 ? $", {N(result.Verified)} verified" : string.Empty),
        };
        if (result.Skipped > 0)
        {
            lines.Add($"Skipped: {N(result.Skipped)} (already at the destination)");
        }

        if (result.Failed > 0)
        {
            lines.Add($"Failed: {N(result.Failed)}. The manifest says why for each file.");
        }

        if (result.Cancelled)
        {
            lines.Add($"Not copied: {N(result.NotReached)} (cancelled). The files already copied were kept.");
        }

        if (result.MetadataWarnings > 0)
        {
            lines.Add($"Metadata could not be set on {N(result.MetadataWarnings)} files or folders.");
        }

        lines.Add(string.Empty);
        lines.Add($"Manifest: {result.ManifestPath}");
        return string.Join("\n", lines);
    }

    /// <summary>Turns copy progress into the busy overlay's message ("Copying files… 1,234 of 5,678 (21 %) · 3 skipped").</summary>
    private sealed class ProgressText(Action<string> update, string what, ISettingsService settings) : IProgress<CopyProgress>
    {
        private long _lastUpdate = long.MinValue;

        public void Report(CopyProgress value)
        {
            // At most five updates a second: small files can go by in the thousands.
            var now = Environment.TickCount64;
            if (now - _lastUpdate < 200 && value.FilesDone < value.TotalFiles)
            {
                return;
            }

            _lastUpdate = now;
            var culture = CultureInfo.CurrentCulture;
            var percent = value.TotalBytes > 0 ? (int)Math.Min(100, 100 * value.BytesDone / value.TotalBytes)
                : value.TotalFiles <= 0 ? 0 : (int)Math.Min(100, 100 * value.FilesDone / value.TotalFiles);
            var text = $"{what} {value.FilesDone.ToString("N0", culture)} of {value.TotalFiles.ToString("N0", culture)} files, " +
                       $"{SizeFormatter.Format(value.BytesDone, settings.Current.SizeUnit, provider: culture)} ({percent} %)";
            if (value.Skipped > 0)
            {
                text += $" · {value.Skipped.ToString("N0", culture)} skipped";
            }

            if (value.Failed > 0)
            {
                text += $" · {value.Failed.ToString("N0", culture)} failed";
            }

            update(text);
        }
    }
}
