SELECT TOP 20 [Key], [Value] FROM COMMON_SystemVariable
WHERE [Key] LIKE '%dog%' OR [Key] LIKE '%soft%' OR [Key] LIKE '%lic%'
   OR [Key] LIKE '%auth%' OR [Key] LIKE '%regist%' OR [Key] LIKE '%serial%'
   OR [Key] LIKE '%enterprise%' OR [Key] LIKE '%company%'
ORDER BY [Key]
