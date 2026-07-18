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
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "FI.Api.dll"]
