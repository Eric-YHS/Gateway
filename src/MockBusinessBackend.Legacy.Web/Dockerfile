# Mock Business Backend (Legacy) - .NET Framework 4.7.2 on IIS
# Requires Windows containers (Windows Server Core)

FROM mcr.microsoft.com/dotnet/framework/aspnet:4.8 AS runtime

RUN Remove-Website -Name 'Default Web Site'; `
    New-WebAppPool -Name 'MockBackendPool' -Force; `
    Set-ItemProperty -Path 'IIS:\AppPools\MockBackendPool' -Name 'managedRuntimeVersion' -Value 'v4.0'; `
    Set-ItemProperty -Path 'IIS:\AppPools\MockBackendPool' -Name 'managedPipelineMode' -Value 'Integrated';

COPY . /inetpub/wwwroot/

RUN New-Website -Name 'MockBackend' `
    -PhysicalPath 'C:\inetpub\wwwroot' `
    -ApplicationPool 'MockBackendPool' `
    -Port 80 -Force;

EXPOSE 80
