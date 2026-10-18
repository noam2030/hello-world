# syntax=docker/dockerfile:1

# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached when only source files change.
COPY HelloWorld.csproj ./
RUN dotnet restore HelloWorld.csproj

COPY . .
RUN dotnet publish HelloWorld.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Cloud Run sends traffic to $PORT (8080 by default); the entrypoint below
# binds Kestrel to $PORT, falling back to 8080 when it is not set.
# Cloud Run terminates TLS and forwards plain HTTP with X-Forwarded-Proto/For headers.
# ASPNETCORE_FORWARDEDHEADERS_ENABLED makes ASP.NET Core trust those headers, so
# Request.Scheme is "https" and the Google OAuth redirect URI is built correctly.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
EXPOSE 8080

COPY --from=build /app/publish .

# Run as the non-root user that ships with the official .NET images.
USER $APP_UID

ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet HelloWorld.dll"]
