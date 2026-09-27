using System.Globalization;
using Accession.Core.Export;
using Accession.Core.Formatting;
using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data.Audit;
using Accession.Data.Browsing;
using Accession.Data.Queries;
using Accession.Data.Repositories;
using Accession.Data.Sessions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Export;

/// <summary>
/// Exports an inventory to Excel (requirements 5.9): Summary, Media, Categories, Extensions, Files and Errors sheets,
/// streamed with <see cref="XlsxWriter"/> so 10M+ files export in constant memory. Every export is audited (EXP-06).
/// </summary>
public sealed class ExportService(
    FileBrowserQueries files,
    DashboardQueries dashboard,
    InventorySessionFactory factory,
    IAppInfo appInfo,
    ILogger<ExportService> logger)
{
    private const int ErrorPageSize = 5_000;

    private static readonly XlsxColumn[] FileColumns =
    [
        new("Media ID", Width: 16), new("Relative Path", Width: 50), new("File Name", Width: 36), new("Extension", Width: 10),
        new("Category", Width: 16), new("Size (bytes)", XlsxCellType.Integer, 16), new("Size", Width: 12),
        new("Created (UTC)", XlsxCellType.DateTimeUtc, 20), new("Modified (UTC)", XlsxCellType.DateTimeUtc, 20),
        new("Accessed (UTC)", XlsxCellType.DateTimeUtc, 20), new("SHA-1", Width: 42),
    ];

    /// <summary>Row counts for the dialog, before exporting.</summary>
    public ExportEstimate Estimate(InventorySession session, ExportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        var media = ScopeMedia(session, request, cancellationToken);
        var keys = media.Select(m => m.MediaKey).ToList();
        long fileRows = 0;
        var filesSheets = 0;
        if (request.Sheets.Contains(ExportSheet.Files))
        {
            if (request.OneWorkbookPerMedia)
            {
                foreach (var key in keys)
                {
                    var count = files.Totals(session.Database, FilesFilter(request, [key]), cancellationToken).FileCount;
                    fileRows += count;
                    filesSheets += SheetsFor(count, request.MaxRowsPerSheet);
                }
            }
            else
            {
                fileRows = files.Totals(session.Database, FilesFilter(request, keys), cancellationToken).FileCount;
                filesSheets = SheetsFor(fileRows, request.MaxRowsPerSheet);
            }
        }

        var errorRows = request.Sheets.Contains(ExportSheet.Errors) ? CountErrors(session, keys) : 0;
        var workbooks = request.OneWorkbookPerMedia ? keys.Count : 1;
        var other = request.Sheets.Contains(ExportSheet.Media) ? media.Count : 0;
        return new ExportEstimate(fileRows, errorRows, other, filesSheets, workbooks);
    }

    /// <summary>
    /// Writes the workbook(s). On cancel or error nothing is left behind: the workbook being written and any already
    /// finished in this export are deleted.
    /// </summary>
    public ExportResult Export(InventorySession session, ExportRequest request, IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        if (request.Sheets.Count == 0)
        {
            throw new ArgumentException("Choose at least one sheet.", nameof(request));
        }

        var media = ScopeMedia(session, request, cancellationToken);
        var estimate = Estimate(session, request, cancellationToken);
        var exportedAt = factory.TimeProvider.GetUtcNow();
        var written = new List<ExportedWorkbook>();
        long done = 0;
        try
        {
            var groups = request.OneWorkbookPerMedia
                ? media.Select(m => (Path: PerMediaPath(request.OutputPath, m.MediaId), Media: (IReadOnlyList<DashboardMediaRow>)[m])).ToList()
                : [(request.OutputPath, media)];
            foreach (var (path, groupMedia) in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ExportProgress(done, estimate.TotalRows, Path.GetFileName(path)));
                var start = done;
                var workbook = WriteWorkbook(session, request, path, groupMedia, exportedAt,
                    rows => progress?.Report(new ExportProgress(start + rows, estimate.TotalRows, Path.GetFileName(path))),
                    cancellationToken);
                written.Add(workbook);
                done += workbook.TotalRows;
            }

            progress?.Report(new ExportProgress(done, estimate.TotalRows, "Done"));
        }
        catch
        {
            foreach (var workbook in written)
            {
                TryDelete(workbook.Path);
            }

            throw;
        }

        var result = new ExportResult(written);
        Audit(session, request, result);
        logger.LogInformation("Exported {Rows} rows to {Count} workbook(s): {Paths}", result.TotalRows, written.Count,
            string.Join(", ", written.Select(w => w.Path)));
        return result;
    }

    /// <summary>"C:\x\ACME_Inventory.xlsx" + "123-123_001" → "C:\x\ACME_Inventory_123-123_001.xlsx".</summary>
    public static string PerMediaPath(string outputPath, string mediaId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeId = new string([.. mediaId.Select(c => invalid.Contains(c) ? '_' : c)]);
        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(outputPath)}_{safeId}{Path.GetExtension(outputPath)}");
    }

    private ExportedWorkbook WriteWorkbook(InventorySession session, ExportRequest request, string path,
        IReadOnlyList<DashboardMediaRow> media, DateTimeOffset exportedAt, Action<long> progress, CancellationToken token)
    {
        var keys = media.Select(m => m.MediaKey).ToList();
        var filter = new DashboardFilter(keys);
        var unit = request.SizeUnit;
        using var writer = XlsxWriter.Create(path, progress, token, request.MaxRowsPerSheet);

        if (request.Sheets.Contains(ExportSheet.Summary))
        {
            WriteSummary(writer, session, request, media, filter, exportedAt, token);
        }

        if (request.Sheets.Contains(ExportSheet.Media))
        {
            writer.BeginSheet("Media", [
                new("Media ID", Width: 16), new("Status", Width: 20), new("Folders", XlsxCellType.Integer, 12),
                new("Files", XlsxCellType.Integer, 14), new("Size (bytes)", XlsxCellType.Integer, 18), new("Size", Width: 12),
                new("Hashed files", XlsxCellType.Integer, 14), new("Errors", XlsxCellType.Integer, 10), new("Scans", XlsxCellType.Integer, 8),
                new("Last scan completed (UTC)", XlsxCellType.DateTimeUtc, 24)]);
            foreach (var m in media)
            {
                writer.BeginRow();
                writer.Text(m.MediaId);
                writer.Text(Words(m.Status.ToString()));
                writer.Integer(m.FolderCount);
                writer.Integer(m.FileCount);
                writer.Integer(m.TotalBytes);
                writer.Text(SizeFormatter.Format(m.TotalBytes, unit, provider: CultureInfo.InvariantCulture));
                writer.Integer(m.HashedCount);
                writer.Integer(m.ErrorCount);
                writer.Integer(m.ScanCount);
                writer.DateTimeUtc(m.LastScanCompletedUtc);
                writer.EndRow();
            }
        }

        if (request.Sheets.Contains(ExportSheet.Categories))
        {
            writer.BeginSheet("Categories", [
                new("Category", Width: 22), new("Files", XlsxCellType.Integer, 14), new("Size (bytes)", XlsxCellType.Integer, 18),
                new("Size", Width: 12)]);
            foreach (var c in dashboard.ByCategory(session.Database, filter, token).Where(c => c.FileCount > 0))
            {
                writer.BeginRow();
                writer.Text(c.Category);
                writer.Integer(c.FileCount);
                writer.Integer(c.TotalBytes);
                writer.Text(SizeFormatter.Format(c.TotalBytes, unit, provider: CultureInfo.InvariantCulture));
                writer.EndRow();
            }
        }

        if (request.Sheets.Contains(ExportSheet.Extensions))
        {
            writer.BeginSheet("Extensions", [
                new("Extension", Width: 12), new("Category", Width: 22), new("Files", XlsxCellType.Integer, 14),
                new("Size (bytes)", XlsxCellType.Integer, 18), new("Size", Width: 12)]);
            foreach (var e in dashboard.ByExtension(session.Database, filter, token))
            {
                writer.BeginRow();
                writer.Text(e.Extension.Length == 0 ? "(none)" : e.Extension);
                writer.Text(e.Category);
                writer.Integer(e.FileCount);
                writer.Integer(e.TotalBytes);
                writer.Text(SizeFormatter.Format(e.TotalBytes, unit, provider: CultureInfo.InvariantCulture));
                writer.EndRow();
            }
        }

        if (request.Sheets.Contains(ExportSheet.Files))
        {
            WriteFiles(writer, session, request, media, token);
        }

        if (request.Sheets.Contains(ExportSheet.Errors))
        {
            WriteErrors(writer, session, media, token);
        }

        writer.Complete();
        return new ExportedWorkbook(Path.GetFullPath(path), writer.Sheets);
    }

    private void WriteSummary(XlsxWriter writer, InventorySession session, ExportRequest request, IReadOnlyList<DashboardMediaRow> media,
        DashboardFilter filter, DateTimeOffset exportedAt, CancellationToken token)
    {
        var config = session.Config;
        var totals = dashboard.Summary(session.Database, filter, token);
        var unit = request.SizeUnit;
        writer.BeginSheet("Summary", [new("Item", Width: 28), new("Value", Width: 60)]);
        Text("Client", config.ClientName);
        Text("Client ID", config.ClientCode);
        Text("Matter", config.MatterName);
        Text("Matter ID", config.MatterCode);
        Text("Description", config.Description);
        Text("Matter link", config.MatterUrl);
        Text("Root path", config.RootPath);
        Text("Inventory file", session.DbPath);
        Text("Inventory ID", config.InventoryGuid.ToString());
        Text("Scope", Describe(request, media));
        Date("Exported at (UTC)", exportedAt);
        Text("Exported by", factory.User.UserName);
        Text("Computer", factory.User.MachineName);
        Text("Accession version", appInfo.Version);
        Text("Size units", unit == Core.Settings.SizeUnitSystem.Binary ? "Binary (1 KiB = 1,024 bytes)" : "Decimal (1 KB = 1,000 bytes)");
        Number("Media", totals.MediaCount);
        Number("Folders", totals.FolderCount);
        Number("Files", totals.FileCount);
        Number("Total size (bytes)", totals.TotalBytes);
        Text("Total size", SizeFormatter.Format(totals.TotalBytes, unit, provider: CultureInfo.InvariantCulture));
        Number("Hashed files", totals.HashedCount);
        Number("Errors", totals.ErrorCount);
        if (request.FilesView is not null)
        {
            Number("Files in this view", files.Totals(session.Database, FilesFilter(request, [.. media.Select(m => m.MediaKey)]), token).FileCount);
        }

        void Text(string item, string? value)
        {
            writer.BeginRow();
            writer.Text(item);
            writer.Text(value);
            writer.EndRow();
        }

        void Number(string item, long value)
        {
            writer.BeginRow();
            writer.Text(item);
            writer.Integer(value);
            writer.EndRow();
        }

        void Date(string item, DateTimeOffset value)
        {
            writer.BeginRow();
            writer.Text(item);
            writer.DateTimeUtc(value);
            writer.EndRow();
        }
    }

    private void WriteFiles(XlsxWriter writer, InventorySession session, ExportRequest request, IReadOnlyList<DashboardMediaRow> media,
        CancellationToken token)
    {
        writer.BeginSheet("Files", FileColumns);
        var unit = request.SizeUnit;

        // One media at a time, in Media ID order: each query sorts only that media's files.
        using var scope = session.Database.Open();
        foreach (var m in media)
        {
            foreach (var f in files.StreamForExport(scope, FilesFilter(request, [m.MediaKey]), token))
            {
                writer.BeginRow();
                writer.Text(f.MediaId);
                writer.Text(f.FolderPath);
                writer.Text(f.Name);
                writer.Text(f.Extension);
                writer.Text(f.Category);
                writer.Integer(f.SizeBytes);
                writer.Text(SizeFormatter.Format(f.SizeBytes, unit, provider: CultureInfo.InvariantCulture));
                writer.DateTimeUtc(f.CreatedUtc);
                writer.DateTimeUtc(f.ModifiedUtc);
                writer.DateTimeUtc(f.AccessedUtc);
                writer.Text(f.Sha1);
                writer.EndRow();
            }
        }
    }

    private static void WriteErrors(XlsxWriter writer, InventorySession session, IReadOnlyList<DashboardMediaRow> media, CancellationToken token)
    {
        writer.BeginSheet("Errors", [
            new("Occurred (UTC)", XlsxCellType.DateTimeUtc, 20), new("Media ID", Width: 16), new("Path", Width: 60),
            new("Item type", Width: 10), new("Error type", Width: 22), new("Severity", Width: 10), new("Code", XlsxCellType.Integer, 12),
            new("Message", Width: 60)]);
        using var scope = session.Database.Open();
        var repository = new ScanErrorRepository(scope);
        foreach (var m in media)
        {
            long? after = null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var page = repository.List(new ScanErrorQuery { MediaKey = m.MediaKey, AfterErrorId = after, PageSize = ErrorPageSize });
                foreach (var e in page)
                {
                    writer.BeginRow();
                    writer.DateTimeUtc(e.OccurredAtUtc);
                    writer.Text(m.MediaId);
                    writer.Text(e.RelativePath);
                    writer.Text(e.ItemType.ToString());
                    writer.Text(Words(e.ErrorType.ToString()));
                    writer.Text(e.Severity.ToString());
                    writer.Integer(e.ErrorCode);
                    writer.Text(e.Message);
                    writer.EndRow();
                }

                if (page.Count < ErrorPageSize)
                {
                    break;
                }

                after = page[^1].ErrorId;
            }
        }
    }

    /// <summary>The media the export covers, in Media ID order.</summary>
    private List<DashboardMediaRow> ScopeMedia(InventorySession session, ExportRequest request, CancellationToken token)
    {
        IReadOnlyCollection<long>? keys = request.MediaKeys
            ?? request.FilesView?.MediaKeys
            ?? (request.FilesView?.MediaKey is { } key ? [key] : null);
        var all = dashboard.ByMedia(session.Database, DashboardFilter.All, token);
        return [.. all.Where(m => keys is null || keys.Contains(m.MediaKey)).OrderBy(m => m.MediaId, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The Files sheet's filter for some media: the Files view's filter, or all their files.</summary>
    private static FileFilter FilesFilter(ExportRequest request, IReadOnlyCollection<long> mediaKeys) =>
        (request.FilesView ?? FileFilter.None) with { MediaKey = null, MediaKeys = mediaKeys };

    private static long CountErrors(InventorySession session, IReadOnlyCollection<long> keys)
    {
        using var scope = session.Database.Open();
        return Dapper.SqlMapper.ExecuteScalar<long>(scope.Connection,
            "SELECT COUNT(*) FROM ScanError WHERE MediaKey IN (SELECT value FROM json_each(@keys))",
            new { keys = System.Text.Json.JsonSerializer.Serialize(keys) });
    }

    private static int SheetsFor(long rows, int perSheet) => rows == 0 ? 1 : (int)((rows + perSheet - 1) / perSheet);

    private static string Describe(ExportRequest request, IReadOnlyList<DashboardMediaRow> media)
    {
        var mediaText = request.MediaKeys is null && request.FilesView?.MediaKey is null && request.FilesView?.MediaKeys is null
            ? "All media"
            : media.Count == 1 ? $"Media {media[0].MediaId}" : $"{media.Count:N0} media: {string.Join(", ", media.Select(m => m.MediaId))}";
        return request.FilesView is null ? mediaText : $"Current Files view ({mediaText.ToLowerInvariant()})";
    }

    private void Audit(InventorySession session, ExportRequest request, ExportResult result)
    {
        var details = new
        {
            Scope = request.FilesView is not null ? "FilesView" : request.MediaKeys is null ? "AllMedia" : "SelectedMedia",
            request.MediaKeys,
            Sheets = request.Sheets.Select(s => s.ToString()).ToList(),
            request.OneWorkbookPerMedia,
            Workbooks = result.Workbooks.Select(w => new { w.Path, Sheets = w.Sheets.Select(s => new { s.Name, s.Rows }).ToList() }).ToList(),
        };
        try
        {
            if (session.IsReadOnly)
            {
                // Like the read-only open (LCK-05): the audit entry is the only write; skipped if the file is not writable.
                new AuditService(new InventoryDatabase(session.DbPath), factory.User, factory.TimeProvider).Write(AuditAction.ExportCreated, details: details);
            }
            else
            {
                session.Audit.Write(AuditAction.ExportCreated, details: details);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write the export audit entry");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not delete {Path} after a cancelled export", path);
        }
    }

    /// <summary>"CompletedWithErrors" → "Completed with errors".</summary>
    private static string Words(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
}
