FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY services/platform-api/TravelOS.PlatformApi.csproj services/platform-api/
RUN dotnet restore services/platform-api/TravelOS.PlatformApi.csproj
COPY services/platform-api/ services/platform-api/
RUN dotnet publish services/platform-api/TravelOS.PlatformApi.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install --yes --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish/ ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "TravelOS.PlatformApi.dll"]
