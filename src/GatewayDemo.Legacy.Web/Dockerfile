# Legacy Gateway Web - .NET Framework 4.7.2 on IIS
# Requires Windows containers (Windows Server Core)

FROM mcr.microsoft.com/dotnet/framework/aspnet:4.8 AS runtime

# Remove default site
RUN Remove-Website -Name 'Default Web Site'; `
    New-WebAppPool -Name 'GatewayLegacyPool' -Force; `
    Set-ItemProperty -Path 'IIS:\AppPools\GatewayLegacyPool' -Name 'managedRuntimeVersion' -Value 'v4.0'; `
    Set-ItemProperty -Path 'IIS:\AppPools\GatewayLegacyPool' -Name 'managedPipelineMode' -Value 'Integrated';

# Copy gateway site files
COPY . /inetpub/wwwroot/

# Create gateway site with two bindings (public 80 + admin 8081)
RUN New-Website -Name 'GatewayLegacy' `
    -PhysicalPath 'C:\inetpub\wwwroot' `
    -ApplicationPool 'GatewayLegacyPool' `
    -Port 80 -Force; `
    New-WebBinding -Name 'GatewayLegacy' -Protocol 'http' -Port 8081 -IPAddress '*';

# Ensure App_Data directory exists and is writable
RUN mkdir C:\inetpub\wwwroot\App_Data 2>$null;

EXPOSE 80 8081
