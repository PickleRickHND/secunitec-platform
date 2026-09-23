FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY global.json .
COPY Secunitec.slnx .

COPY src/BuildingBlocks/Secunitec.BuildingBlocks/Secunitec.BuildingBlocks.csproj src/BuildingBlocks/Secunitec.BuildingBlocks/
COPY src/BuildingBlocks/Secunitec.BuildingBlocks.AspNetCore/Secunitec.BuildingBlocks.AspNetCore.csproj src/BuildingBlocks/Secunitec.BuildingBlocks.AspNetCore/
COPY src/Billing/Secunitec.Billing.Domain/Secunitec.Billing.Domain.csproj src/Billing/Secunitec.Billing.Domain/
COPY src/Billing/Secunitec.Billing.Application/Secunitec.Billing.Application.csproj src/Billing/Secunitec.Billing.Application/
COPY src/Billing/Secunitec.Billing.Infrastructure/Secunitec.Billing.Infrastructure.csproj src/Billing/Secunitec.Billing.Infrastructure/
COPY src/Billing/Secunitec.Billing.Api/Secunitec.Billing.Api.csproj src/Billing/Secunitec.Billing.Api/

RUN dotnet restore src/Billing/Secunitec.Billing.Api/Secunitec.Billing.Api.csproj

COPY src/BuildingBlocks src/BuildingBlocks
COPY src/Billing src/Billing

RUN dotnet publish src/Billing/Secunitec.Billing.Api/Secunitec.Billing.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "Secunitec.Billing.Api.dll"]
