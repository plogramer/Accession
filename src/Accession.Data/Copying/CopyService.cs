using System.Globalization;
using System.Text;
using Accession.Core.Copying;
using Accession.Core.Model;
using Accession.Core.Time;
using Accession.Data.Audit;
using Accession.Data.Browsing;
using Accession.Data.Schema;
using Accession.Data.Sessions;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Accession.Data.Copying;

/// <summary>
/// Copies files out of the inventory (requirements 5.8b): writes a copy batch (.bat) for the user to run, or copies the
/// files itself. Files are taken in media, folder, name order, so sequential numbers are stable. Both write a CSV
/// manifest and are audited. The root is only ever read (SCN-40).
/// </summary>
public sealed class CopyService(InventorySessionFactory factory, ILogger<CopyService> logger)
{
    private const int ChunkSize = 2_000;

    public static readonly IReadOnlyList<string> ManifestHeaders =
        ["#", "Destination Path", "Source Path", "Media ID", "Size (bytes)", "Modified (UTC)", "SHA-1 (inventory)", "Outcome", "Message"];

    private readonly EvidenceFileCopier _copier = new();

    /// <summary>Number of files and bytes the request covers (for the dialog).</summary>
    public FileTotals Estimate(InventorySession session, FileFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new FileBrowserQueries().Totals(session.Database, filter, cancellationToken);
    }

    /// <summary>Why the request cannot run; null when it can.</summary>
    public static string? Validate(InventorySession session, CopyRequest request, long fileCount)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        var root = session.Config.RootPath;
        if (CopyPaths.ValidateDestination(request.Destination, root) is { } destinationError)
        {
            return destinationError;
        }

        if (request.Naming.Validate(fileCount) is { } namingError)
        {
            return namingError;
        }

        return ValidateOutput(request.ManifestPath, root, "manifest");
    }

    /// <summary>Why a file cannot be written at <paramref name="path"/>; null when it can.</summary>
    public static string? ValidateOutput(string? path, string rootPath, string what)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return $"Choose where to save the {what}.";
        }

        return !string.IsNullOrWhiteSpace(rootPath) && CopyPaths.IsSameOrInside(path, rootPath)
            ? $"The {what} cannot be saved under the root folder."
            : null;
    }

    /// <summary>
    /// Writes the copy batch and the manifest. On cancel or error neither file is kept.
    /// </summary>
    public CopyBatchResult GenerateBatch(InventorySession session, CopyRequest request, CopyCommandTemplate template, string batchPath,
        IProgress<CopyProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(template);
        var root = session.Config.RootPath;
        var order = OrderedFiles(session, request.Filter, cancellationToken);
        Check(Validate(session, request, order.Count) ?? template.Validate(request.Naming.Mode) ?? ValidateOutput(batchPath, root, "batch file"));
        var totalBytes = order.Sum(o => o.SizeBytes);

        var partial = batchPath + ".partial";
        long done = 0;
        long bytes = 0;
        var manifestWritten = false;
        try
        {
            if (Path.GetDirectoryName(batchPath) is { Length: > 0 } batchFolder)
            {
                Directory.CreateDirectory(batchFolder);
            }

            using (var manifest = new CsvWriter(request.ManifestPath, ManifestHeaders))
            using (var bat = new StreamWriter(partial, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1 << 16) { NewLine = "\r\n" })
            {
                WriteBatchHeader(bat, session, request, template, order.Count, totalBytes);
                string? lastFolder = null;
                foreach (var file in Rows(session, order, cancellationToken))
                {
                    var (source, destination) = Paths(root, request, file, done);
                    var folder = Path.GetDirectoryName(destination) ?? request.Destination;
                    if (!string.Equals(folder, lastFolder, StringComparison.Ordinal))
                    {
                        bat.WriteLine($"if not exist {BatchFile.Quote(WithTrailingSeparator(folder))} mkdir {BatchFile.Quote(folder)}");
                        lastFolder = folder;
                    }

                    var destinationQuoted = BatchFile.Quote(destination);
                    bat.WriteLine($"if exist {destinationQuoted} (set /a SKIPPED+=1 & echo Skipped, already exists: {destinationQuoted}) else (");
                    bat.WriteLine("  " + template.Render(source, destination));
                    bat.WriteLine($"  if errorlevel {template.FailureExitCode} (set /a FAILED+=1 & echo FAILED: {BatchFile.Quote(source)}) else (set /a COPIED+=1)");
                    bat.WriteLine(")");

                    done++;
                    bytes += file.SizeBytes;
                    WriteManifestRow(manifest, done, destination, source, file, "In batch", null);
                    if (done % 1_000 == 0)
                    {
                        progress?.Report(new CopyProgress(done, order.Count, bytes, totalBytes, 0, 0, 0));
                    }
                }

                WriteBatchFooter(bat);
                bat.Flush();
                manifest.Complete();
                manifestWritten = true;
            }

            File.Move(partial, batchPath, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            if (manifestWritten)
            {
                TryDelete(request.ManifestPath);
            }

            throw;
        }

        progress?.Report(new CopyProgress(done, order.Count, bytes, totalBytes, 0, 0, 0));
        WriteAudit(session, AuditAction.CopyBatchGenerated, new
        {
            request.ScopeText,
            Files = done,
            Bytes = bytes,
            request.Destination,
            Naming = NamingDetails(request.Naming),
            Command = template.Command,
            BatchPath = batchPath,
            request.ManifestPath,
        });
        logger.LogInformation("Copy batch {Path}: {Files} files to {Destination}", batchPath, done, request.Destination);
        return new CopyBatchResult(batchPath, request.ManifestPath, done, bytes);
    }

    /// <summary>
    /// Copies the files. Existing files at the destination are skipped, failures are recorded and the copy goes on.
    /// On cancel, the files already copied stay and the manifest says where it stopped.
    /// </summary>
    public CopyFilesResult CopyFiles(InventorySession session, CopyRequest request, CopyFileOptions options,
        IProgress<CopyProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);
        var root = session.Config.RootPath;
        var order = OrderedFiles(session, request.Filter, cancellationToken);
        Check(Validate(session, request, order.Count));
        var totalBytes = order.Sum(o => o.SizeBytes);

        long done = 0, copied = 0, verified = 0, skipped = 0, failed = 0, bytes = 0, warnings = 0;
        var cancelled = false;
        var preserveFolders = options.PreserveMetadata && request.Naming.Mode == CopyNamingMode.PreserveStructure;
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var createdFolders = new List<(string Source, string Destination)>();
        Directory.CreateDirectory(request.Destination);

        using (var manifest = new CsvWriter(request.ManifestPath, ManifestHeaders))
        {
            var lastReport = 0L;
            var fileBytes = 0L;
            try
            {
                foreach (var file in Rows(session, order, cancellationToken))
                {
                    fileBytes = 0;
                    cancellationToken.ThrowIfCancellationRequested();
                    var (source, destination) = Paths(root, request, file, done);
                    if (preserveFolders)
                    {
                        NoteFolders(root, request.Destination, file.FolderPath, seenFolders, createdFolders);
                    }

                    var result = _copier.Copy(source, destination, options, read =>
                    {
                        bytes += read;
                        fileBytes += read;
                        if (bytes - lastReport >= 64L << 20)
                        {
                            lastReport = bytes;
                            progress?.Report(new CopyProgress(done, order.Count, bytes, totalBytes, copied, skipped, failed));
                        }
                    }, cancellationToken);

                    done++;
                    var message = result.Message;
                    switch (result.Outcome)
                    {
                        case CopyOutcome.Copied:
                        case CopyOutcome.Verified:
                            copied++;
                            verified += result.Outcome == CopyOutcome.Verified ? 1 : 0;
                            warnings += message is null ? 0 : 1;
                            if (result.Sha1 is { } readHash && file.Sha1 is { Length: > 0 } inventoryHash
                                && !string.Equals(readHash, inventoryHash, StringComparison.OrdinalIgnoreCase))
                            {
                                message = Join(message, $"The file's SHA-1 is now {readHash}, not the inventory's: it changed after the scan.");
                            }

                            break;
                        case CopyOutcome.Skipped:
                            skipped++;
                            break;
                        default:
                            failed++;
                            bytes -= fileBytes; // the partial copy was deleted
                            break;
                    }

                    fileBytes = 0;
                    WriteManifestRow(manifest, done, destination, source, file, result.Outcome.ToString(), message);
                    progress?.Report(new CopyProgress(done, order.Count, bytes, totalBytes, copied, skipped, failed));
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                bytes -= fileBytes; // the file being copied was deleted
                manifest.WriteRow([null, null, null, null, null, null, null, "Stopped",
                    $"Cancelled by the user after {done.ToString("N0", CultureInfo.InvariantCulture)} of {order.Count.ToString("N0", CultureInfo.InvariantCulture)} files. The files below this row in copy order were not copied."]);
            }

            if (preserveFolders)
            {
                // Last, because adding files changes a folder's modified time.
                foreach (var (sourceFolder, destinationFolder) in createdFolders)
                {
                    if (Directory.Exists(destinationFolder) && EvidenceFileCopier.CopyFolderTimes(sourceFolder, destinationFolder) is not null)
                    {
                        warnings++;
                    }
                }
            }

            manifest.Complete();
        }

        var result2 = new CopyFilesResult(request.Destination, request.ManifestPath, order.Count, copied, verified, skipped, failed, bytes, cancelled, warnings);
        WriteAudit(session, AuditAction.FilesCopied, new
        {
            request.ScopeText,
            Files = order.Count,
            Copied = copied,
            Verified = verified,
            Skipped = skipped,
            Failed = failed,
            BytesCopied = bytes,
            Cancelled = cancelled,
            request.Destination,
            Naming = NamingDetails(request.Naming),
            options.PreserveMetadata,
            options.Verify,
            request.ManifestPath,
        });
        logger.LogInformation("Copied {Copied} of {Files} files to {Destination} ({Skipped} skipped, {Failed} failed, cancelled: {Cancelled})",
            copied, order.Count, request.Destination, skipped, failed, cancelled);
        return result2;
    }

    /// <summary>Source and destination of the <paramref name="index"/>-th file (0-based, in copy order).</summary>
    private static (string Source, string Destination) Paths(string root, CopyRequest request, CopySourceRow file, long index)
    {
        var source = CopyPaths.Combine(root, file.FolderPath + file.Name);
        var destination = request.Naming.Mode == CopyNamingMode.Sequential
            ? Path.Combine(request.Destination, request.Naming.SequentialName(request.Naming.StartNumber + index, OriginalExtension(file.Name)))
            : CopyPaths.Combine(request.Destination, file.FolderPath + file.Name);
        return (source, destination);
    }

    /// <summary>Extension as written in the name (case kept), without the dot; "" when none.</summary>
    internal static string OriginalExtension(string name)
    {
        var extension = Path.GetExtension(name);
        return extension.Length <= 1 ? string.Empty : extension[1..];
    }

    /// <summary>Remembers the destination folders (and their parents up to the media folder) that this copy creates.</summary>
    private static void NoteFolders(string root, string destination, string folderPath, HashSet<string> seen, List<(string, string)> created)
    {
        var parts = folderPath.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var relative = new StringBuilder(@"\");
        foreach (var part in parts)
        {
            relative.Append(part).Append('\\');
            var key = relative.ToString();
            if (seen.Add(key))
            {
                var target = CopyPaths.Combine(destination, key);
                if (!Directory.Exists(target))
                {
                    created.Add((CopyPaths.Combine(root, key), target));
                }
            }
        }
    }

    /// <summary>File IDs and sizes in copy order: media, folder, name (case-insensitive), then FileId.</summary>
    private static List<(long FileId, long SizeBytes)> OrderedFiles(InventorySession session, FileFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var scope = session.Database.Open();
        var (where, parameters) = FileBrowserQueries.BuildWhere(scope, filter);
        var sql =
            $"""
            SELECT f.FileId, f.SizeBytes
            FROM File f
            JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
            JOIN Folder fo ON fo.FolderId = f.FolderId
            {(filter.CategoryId is not null ? CategorySql.JoinCategory("f.Extension", "cat") : string.Empty)}
            WHERE {where}
            ORDER BY m.MediaId COLLATE NOCASE, fo.RelativePath COLLATE NOCASE, f.Name COLLATE NOCASE, f.FileId
            """;
        return scope.Connection.Query<(long, long)>(new CommandDefinition(sql, parameters, commandTimeout: 0, cancellationToken: cancellationToken)).AsList();
    }

    /// <summary>The rows in <paramref name="order"/>, read a chunk at a time (short reads; no transaction held while copying).</summary>
    private static IEnumerable<CopySourceRow> Rows(InventorySession session, List<(long FileId, long SizeBytes)> order, CancellationToken cancellationToken)
    {
        for (var start = 0; start < order.Count; start += ChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ids = order.GetRange(start, Math.Min(ChunkSize, order.Count - start)).Select(o => o.FileId).ToList();
            Dictionary<long, CopySourceRow> rows;
            using (var scope = session.Database.Open())
            {
                rows = scope.Connection.Query<CopySourceRow>(
                    """
                    SELECT f.FileId, m.MediaId, fo.RelativePath AS FolderPath, f.Name, f.SizeBytes, f.ModifiedUtc, f.Sha1
                    FROM File f
                    JOIN Media m ON m.MediaKey = f.MediaKey
                    JOIN Folder fo ON fo.FolderId = f.FolderId
                    WHERE f.FileId IN (SELECT value FROM json_each(@ids))
                    """,
                    new { ids = System.Text.Json.JsonSerializer.Serialize(ids) }).ToDictionary(r => r.FileId);
            }

            foreach (var id in ids)
            {
                if (rows.TryGetValue(id, out var row))
                {
                    yield return row;
                }
            }
        }
    }

    private static void WriteManifestRow(CsvWriter manifest, long number, string destination, string source, CopySourceRow file, string outcome, string? message) =>
        manifest.WriteRow([
            number.ToString(CultureInfo.InvariantCulture), destination, source, file.MediaId, file.SizeBytes.ToString(CultureInfo.InvariantCulture),
            file.ModifiedUtc is { } modified ? modified.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null,
            file.Sha1, outcome, message,
        ]);

    private void WriteBatchHeader(StreamWriter bat, InventorySession session, CopyRequest request, CopyCommandTemplate template, long files, long bytes)
    {
        var now = factory.TimeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var naming = request.Naming.Mode == CopyNamingMode.Sequential
            ? $"sequential names ({request.Naming.Example()} …)"
            : "original folders and names";
        bat.WriteLine("@echo off");
        bat.WriteLine(Rem($"Copy batch written by Accession on {now} UTC by {session.UserName}."));
        bat.WriteLine(Rem($"Inventory: {session.Config.ClientCode} {session.Config.MatterCode} ({session.DbPath})"));
        bat.WriteLine(Rem($"{files.ToString("N0", CultureInfo.InvariantCulture)} files, {bytes.ToString("N0", CultureInfo.InvariantCulture)} bytes, to {request.Destination} with {naming}."));
        bat.WriteLine(Rem($"Command: {template.Command}"));
        bat.WriteLine(Rem($"Manifest: {request.ManifestPath}"));
        bat.WriteLine(Rem("Files that already exist at the destination are skipped. The source files are only read."));
        bat.WriteLine("chcp 65001 >nul");
        bat.WriteLine("setlocal");
        bat.WriteLine("set /a COPIED=0");
        bat.WriteLine("set /a SKIPPED=0");
        bat.WriteLine("set /a FAILED=0");
        bat.WriteLine($"echo Copying {files.ToString("N0", CultureInfo.InvariantCulture)} files to {BatchFile.Quote(request.Destination)}...");
    }

    private static void WriteBatchFooter(StreamWriter bat)
    {
        bat.WriteLine("echo.");
        bat.WriteLine("echo Copied: %COPIED%   Skipped (already there): %SKIPPED%   Failed: %FAILED%");
        bat.WriteLine("if %FAILED% gtr 0 (endlocal & exit /b 1)");
        bat.WriteLine("endlocal & exit /b 0");
    }

    /// <summary>A comment line. % is doubled because variables are expanded even in rem lines.</summary>
    private static string Rem(string text) => "rem " + BatchFile.EscapePercent(text.ReplaceLineEndings(" "));

    private static string WithTrailingSeparator(string folder) =>
        folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;

    private static object NamingDetails(CopyNaming naming) => naming.Mode == CopyNamingMode.Sequential
        ? new { Mode = naming.Mode.ToString(), naming.Prefix, naming.Digits, naming.StartNumber }
        : new { Mode = naming.Mode.ToString(), Prefix = (string?)null, Digits = (int?)null, StartNumber = (long?)null };

    private static string Join(string? a, string b) => a is null ? b : a + " " + b;

    private static void Check(string? error)
    {
        if (error is not null)
        {
            throw new ArgumentException(error);
        }
    }

    private void WriteAudit(InventorySession session, AuditAction action, object details)
    {
        try
        {
            if (session.IsReadOnly)
            {
                // Like exports: the audit entry is the only write to a read-only inventory; skipped if the file is not writable.
                new AuditService(new InventoryDatabase(session.DbPath), factory.User, factory.TimeProvider).Write(action, details: details);
            }
            else
            {
                session.Audit.Write(action, details: details);
            }
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write the {Action} audit entry", action);
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
            logger.LogWarning(ex, "Could not delete {Path}", path);
        }
    }
}
