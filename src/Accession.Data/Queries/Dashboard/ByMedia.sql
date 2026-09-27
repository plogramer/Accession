SELECT m.MediaKey, m.MediaId, m.Status, m.FolderCount, m.FileCount, m.TotalBytes, m.HashedCount, m.ErrorCount,
       m.ScanCount, m.LastScanCompletedUtc
FROM Media m
WHERE m.IsDeleted = 0
  AND (@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))
ORDER BY m.MediaId COLLATE NOCASE;
