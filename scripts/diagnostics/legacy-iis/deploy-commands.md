## Fix KpDemo site (run in admin PowerShell one by one)

```powershell
C:\Windows\system32\inetsrv\appcmd.exe delete site "KpDemo"
```

```powershell
C:\Windows\system32\inetsrv\appcmd.exe add site /name:"KpDemo" /physicalPath:"E:\验证页面\演示站点20260330\演示站点20260330\web\701日构建20260330" /bindings:http://*:8080:
```

注意：physicalPath 和 /bindings 之间有一个空格，确保它们是分开的两个参数。

```powershell
C:\Windows\system32\inetsrv\appcmd.exe set site "KpDemo" "/[path='/'].applicationPool:KpDemoPool"
```

```powershell
C:\Windows\system32\inetsrv\appcmd.exe start site "KpDemo"
```
