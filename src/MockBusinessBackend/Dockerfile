FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY GatewayDemo.slnx ./
COPY src/MockBusinessBackend/MockBusinessBackend.csproj src/MockBusinessBackend/
RUN dotnet restore src/MockBusinessBackend/MockBusinessBackend.csproj

COPY src/MockBusinessBackend/. src/MockBusinessBackend/
RUN dotnet publish src/MockBusinessBackend/MockBusinessBackend.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish ./

EXPOSE 8080

ENTRYPOINT ["dotnet", "MockBusinessBackend.dll"]
