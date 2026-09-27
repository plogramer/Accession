-- Totals of the selected media, from the Media rows (kept up to date at the end of each scan).
SELECT
    COUNT(*)                          AS MediaCount,
    COALESCE(SUM(m.FolderCount), 0)   AS FolderCount,
    COALESCE(SUM(m.FileCount), 0)     AS FileCount,
    COALESCE(SUM(m.TotalBytes), 0)    AS TotalBytes,
    COALESCE(SUM(m.HashedCount), 0)   AS HashedCount,
    COALESCE(SUM(m.ErrorCount), 0)    AS ErrorCount
FROM Media m
WHERE m.IsDeleted = 0
  AND (@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)));
