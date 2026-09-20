SELECT TOP 10 name FROM sys.tables
WHERE name LIKE 'Common_License%'
   OR name LIKE 'COMMON_License%'
   OR name LIKE 'Common_Soft%'
   OR name LIKE 'Common_Dog%'
   OR name LIKE 'Common_System%'
   OR name LIKE 'Common_Scheme%'
ORDER BY name
