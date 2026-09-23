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
    -c Release -o /app/publish --no-restore \
 && mkdir -p /keys

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Directorio de las llaves de Data Protection (volumen identity_keys). La imagen chiseled no tiene shell para crearlo:
# se copia vacío con el dueño del usuario sin privilegios, y el volumen hereda ese dueño al montarse la primera vez.
COPY --from=build --chown=1654:1654 /keys /keys
USER $APP_UID
ENTRYPOINT ["dotnet", "Secunitec.Identity.dll"]
