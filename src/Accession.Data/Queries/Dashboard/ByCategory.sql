-- Every category, including those with no files, so the list is stable.
WITH s AS (
    SELECT COALESCE(ec.CategoryId, CASE WHEN x.Extension = '' THEN 19 ELSE 20 END) AS CategoryId,
           SUM(x.FileCount) AS FileCount, SUM(x.TotalBytes) AS TotalBytes
    FROM MediaExtensionSummary x
    JOIN Media m ON m.MediaKey = x.MediaKey AND m.IsDeleted = 0
    LEFT JOIN ExtensionCategory ec ON ec.Extension = x.Extension
    WHERE (@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))
    GROUP BY 1
)
SELECT c.CategoryId, c.Name AS Category, c.SortOrder,
       COALESCE(s.FileCount, 0) AS FileCount, COALESCE(s.TotalBytes, 0) AS TotalBytes
FROM FileCategory c
LEFT JOIN s ON s.CategoryId = c.CategoryId
ORDER BY TotalBytes DESC, c.SortOrder;
