FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY global.json .
COPY Secunitec.slnx .

COPY src/BuildingBlocks/Secunitec.BuildingBlocks/Secunitec.BuildingBlocks.csproj src/BuildingBlocks/Secunitec.BuildingBlocks/
COPY src/BuildingBlocks/Secunitec.BuildingBlocks.AspNetCore/Secunitec.BuildingBlocks.AspNetCore.csproj src/BuildingBlocks/Secunitec.BuildingBlocks.AspNetCore/
COPY src/Identity/Secunitec.Identity/Secunitec.Identity.csproj src/Identity/Secunitec.Identity/

RUN dotnet restore src/Identity/Secunitec.Identity/Secunitec.Identity.csproj

COPY src/BuildingBlocks src/BuildingBlocks
COPY src/Identity src/Identity

RUN dotnet publish src/Identity/Secunitec.Identity/Secunitec.Identity.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Secunitec.Identity.dll"]