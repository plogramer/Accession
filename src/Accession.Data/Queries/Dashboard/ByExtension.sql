SELECT x.Extension,
       c.Name AS Category,
       SUM(x.FileCount)  AS FileCount,
       SUM(x.TotalBytes) AS TotalBytes
FROM MediaExtensionSummary x
JOIN Media m ON m.MediaKey = x.MediaKey AND m.IsDeleted = 0
LEFT JOIN ExtensionCategory ec ON ec.Extension = x.Extension
JOIN FileCategory c ON c.CategoryId = COALESCE(ec.CategoryId, CASE WHEN x.Extension = '' THEN 19 ELSE 20 END)
WHERE (@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))
GROUP BY x.Extension, c.Name
ORDER BY TotalBytes DESC, x.Extension;
