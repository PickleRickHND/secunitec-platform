FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Billing/Secunitec.Billing.Api/Secunitec.Billing.Api.csproj
RUN dotnet publish src/Billing/Secunitec.Billing.Api/Secunitec.Billing.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build --chown=1654:1654 /out .
USER 1654
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Secunitec.Billing.Api.dll"]
