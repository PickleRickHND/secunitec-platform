FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY global.json .
COPY Secunitec.slnx .

COPY src/BuildingBlocks/Secunitec.BuildingBlocks/Secunitec.BuildingBlocks.csproj src/BuildingBlocks/Secunitec.BuildingBlocks/
COPY src/BuildingBlocks/Secunitec.BuildingBlocks.AspNetCore/Secunitec.BuildingBlocks.AspNetCore.csproj src/BuildingBlocks/Secunitec.BuildingBlocks.AspNetCore/
COPY src/Gateway/Secunitec.Gateway/Secunitec.Gateway.csproj src/Gateway/Secunitec.Gateway/

RUN dotnet restore src/Gateway/Secunitec.Gateway/Secunitec.Gateway.csproj

COPY src/BuildingBlocks src/BuildingBlocks
COPY src/Gateway src/Gateway

RUN dotnet publish src/Gateway/Secunitec.Gateway/Secunitec.Gateway.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Secunitec.Gateway.dll"]
