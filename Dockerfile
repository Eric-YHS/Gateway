FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GatewayDemo.slnx ./
COPY src/GatewayDemo/GatewayDemo.csproj src/GatewayDemo/
RUN dotnet restore src/GatewayDemo/GatewayDemo.csproj

COPY src/GatewayDemo/. src/GatewayDemo/
RUN dotnet publish src/GatewayDemo/GatewayDemo.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080;http://+:8081

COPY --from=build /app/publish ./

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT ["dotnet", "GatewayDemo.dll"]
