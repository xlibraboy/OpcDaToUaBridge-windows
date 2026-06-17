FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY OpcDaToUaBridge.sln ./
COPY src/OpcBridge.App/OpcBridge.App.csproj src/OpcBridge.App/
COPY src/OpcBridge.Core/OpcBridge.Core.csproj src/OpcBridge.Core/
COPY src/OpcBridge.Da/OpcBridge.Da.csproj src/OpcBridge.Da/
COPY src/OpcBridge.Ua/OpcBridge.Ua.csproj src/OpcBridge.Ua/
RUN dotnet restore OpcDaToUaBridge.sln

COPY . .
RUN dotnet build OpcDaToUaBridge.sln --configuration Release --no-restore
RUN dotnet publish src/OpcBridge.App/OpcBridge.App.csproj \
    --configuration Release \
    --output /app/publish \
    --no-build

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 4840 8080
ENTRYPOINT ["dotnet", "OpcBridge.App.dll"]
