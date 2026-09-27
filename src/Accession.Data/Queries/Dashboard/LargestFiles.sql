SELECT f.MediaKey, m.MediaId, fo.RelativePath || f.Name AS RelativePath, f.SizeBytes, f.ModifiedUtc
FROM File f
JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
JOIN Folder fo ON fo.FolderId = f.FolderId
WHERE (@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))
ORDER BY f.SizeBytes DESC
LIMIT @Limit;
