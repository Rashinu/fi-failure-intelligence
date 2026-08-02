# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/FI.Domain/FI.Domain.csproj src/FI.Domain/
COPY src/FI.Application/FI.Application.csproj src/FI.Application/
COPY src/FI.Infrastructure/FI.Infrastructure.csproj src/FI.Infrastructure/
COPY src/FI.Api/FI.Api.csproj src/FI.Api/
RUN dotnet restore src/FI.Api/FI.Api.csproj

COPY src/ src/
RUN dotnet publish src/FI.Api/FI.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080

# Bkz. docs/product/DEPLOYMENT.md - lokal docker-compose 8080'i sabit bekliyor (ASPNETCORE_URLS
# env var'i orada set edilir ve asagidaki komut satiri argumaniyla ayni degere cozulur, davranis
# degismez). Platform-hosted ortamlarda (Render, Railway vb.) container disaridan enjekte edilen
# bir $PORT env var'ina bagli olmayi bekleyebilir; exec-form bir ENTRYPOINT bunu genisletemez, bu
# yuzden shell-form kullanilip --urls komut satiri argumaniyla (ASPNETCORE_URLS env var'indan daha
# yuksek onceliklidir) cozuluyor. docker-compose'un `entrypoint: [...--migrate]` override'i bu
# shell script'i tamamen degistirir, bu yuzden migrate modunu etkilemez.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet FI.Api.dll --urls http://+:${PORT:-8080}"]
