-- Files per modified year (UTC). Files without a modified date are grouped as 'Unknown'.
SELECT COALESCE(substr(f.ModifiedUtc, 1, 4), 'Unknown') AS Year,
       COUNT(*) AS FileCount,
       COALESCE(SUM(f.SizeBytes), 0) AS TotalBytes
FROM File f JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
WHERE (@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))
GROUP BY 1
ORDER BY 1;
