# ── Stage 1: Build ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy project files first for efficient layer caching
COPY Roma.slnx ./
COPY src/Roma.Domain/Roma.Domain.csproj src/Roma.Domain/
COPY src/Roma.Application/Roma.Application.csproj src/Roma.Application/
COPY src/Roma.Infrastructure/Roma.Infrastructure.csproj src/Roma.Infrastructure/
COPY src/Roma.Api/Roma.Api.csproj src/Roma.Api/

# Restore NuGet packages
RUN dotnet restore src/Roma.Api/Roma.Api.csproj

# Copy all source files
COPY src/ src/

# Publish Release build
RUN dotnet publish src/Roma.Api/Roma.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ── Stage 2: Runtime ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app

# Install SQLite runtime dependencies and curl for healthcheck
RUN apk add --no-cache icu-libs tzdata curl

# Create directory for persistent SQLite database
RUN mkdir -p /app/data && chown -R app:app /app

# Switch to unprivileged non-root user (OWASP A05)
USER app

# Default environment variables for Dokploy
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV DB_PATH=/app/data/roma.db
ENV Roma__DbPath=/app/data/roma.db
ENV Roma__Categories=Trabajo,Estudio,Salud,Personal,Hogar,Otros
ENV Roma__AppTz=America/Argentina/Buenos_Aires
ENV Roma__EventReminderOffsets=-1d,-2h,0

COPY --from=build --chown=app:app /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

VOLUME ["/app/data"]

ENTRYPOINT ["dotnet", "Roma.Api.dll"]
