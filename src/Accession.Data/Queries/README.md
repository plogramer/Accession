# Dashboard query sets

Each dashboard widget runs one SQL file from `Dashboard/`. The files are embedded in the application
(`Accession.Data.Queries.Dashboard.<Name>.sql`) and can be replaced **without rebuilding**: put a file with
the same name in

```
%APPDATA%\Accession\Queries\Dashboard\<Name>.sql
```

and it is used instead of the built-in one.

## Conventions

| Parameter | Meaning |
|---|---|
| `@MediaKeysJson` | Media filter. `NULL` = all active media; otherwise a JSON array of `MediaKey` values, e.g. `[1,4,7]`. Filter with `(@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))`. |
| `@Limit` | Row limit for top-N queries (Largest Files). |

- Always join `Media m` and filter `m.IsDeleted = 0` so deleted media never appear.
- Column names and types must stay as listed below; the dashboard binds to them.
- Category of an extension: mapped extension → its category, `''` → *No Extension* (19), anything else →
  *Other / Unknown* (20). See `ByCategory.sql` for the join.
- Sizes are bytes (`INTEGER`); the UI formats them in the unit chosen in Settings.

## Files and result columns

| File | Columns | Source |
|---|---|---|
| `SummaryTiles.sql` | MediaCount, FolderCount, FileCount, TotalBytes, HashedCount, ErrorCount | `Media` totals (fast) |
| `ByMedia.sql` | MediaKey, MediaId, Status, FolderCount, FileCount, TotalBytes, HashedCount, ErrorCount, ScanCount, LastScanCompletedUtc | `Media` (fast) |
| `ByCategory.sql` | CategoryId, Category, SortOrder, FileCount, TotalBytes | `MediaExtensionSummary` (fast) |
| `ByExtension.sql` | Extension, Category, FileCount, TotalBytes | `MediaExtensionSummary` (fast) |
| `Duplicates.sql` | HashedFiles, UniqueFiles, HashedBytes, UniqueBytes, NotHashedFiles | `File` (background) |
| `DuplicatesByMedia.sql` | MediaKey, HashedFiles, UniqueFiles | `File` (background) |
| `ByYear.sql` | Year, FileCount, TotalBytes | `File` modified date, UTC year (background) |
| `LargestFiles.sql` | MediaKey, MediaId, RelativePath, SizeBytes, ModifiedUtc | `File` top `@Limit` (background) |
