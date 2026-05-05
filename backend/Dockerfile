# syntax=docker/dockerfile:1.7
# ─────────────────────────────────────────────────────────────────────────────
# SayımLink API — Render.com production image
# Multi-stage: SDK builds + publishes, ASP.NET runtime serves.
# Render injects PORT (default 10000); ENTRYPOINT binds 0.0.0.0:$PORT.
# ─────────────────────────────────────────────────────────────────────────────

# ─── Build stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first → maximizes Docker layer caching when only source changes.
COPY SayimLink.Api.csproj ./
RUN dotnet restore SayimLink.Api.csproj

# Copy the rest of the backend source and publish.
COPY . ./
RUN dotnet publish SayimLink.Api.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false \
        /p:PublishTrimmed=false \
        /p:DebugType=None \
        /p:DebugSymbols=false

# ─── Runtime stage ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# The aspnet:9.0 image already ships with a non-root `app` user (uid 1654).
# We reuse it instead of creating our own.
COPY --from=build --chown=app:app /app/publish ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Render injects PORT at runtime; default 10000 if not set.
EXPOSE 10000

USER app

# Shell-form ENTRYPOINT so ${PORT} is expanded at container start.
# `exec` replaces the shell with dotnet so signals (SIGTERM) reach the app.
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} exec dotnet SayimLink.Api.dll"]
