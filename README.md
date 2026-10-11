# Roma Backend API

A high-performance personal productivity and habit-tracking backend service built with **.NET 10 LTS**, **Clean Architecture**, and **SQLite WAL mode**.

---

## 🏛️ Architecture Overview

The backend follows Clean / Hexagonal architecture principles with strict dependency boundaries:

```
src/
├── Roma.Domain/           # Enterprise domain rules, entities, validation, repository interfaces
├── Roma.Application/      # Use cases, application services, configuration options, ports
├── Roma.Infrastructure/   # SQLite persistence (Dapper), Telegram notifier, background workers
└── Roma.Api/              # ASP.NET Core controllers, middleware, security headers, entry point
tests/
└── Roma.Tests/            # Integration & unit test suite (xUnit, WebApplicationFactory)
```

### Key Technical Highlights
- **Framework**: .NET 10 LTS (`net10.0`)
- **Persistence**: SQLite with Write-Ahead Logging (`PRAGMA journal_mode=WAL`) and foreign keys enabled.
- **Data Access**: High-throughput micro-ORM via Dapper with 100% parameterized SQL queries.
- **Data Integrity**: Global **Soft Delete** pattern (`is_deleted = 0`) across Tasks, Categories, and Workout Sessions.
- **Resilience**: Asynchronous Telegram background notification worker for scheduled event reminders.

---

## ⚙️ Configuration & Environment Variables

All configuration is read from environment variables or `appsettings.json` under the `Roma` section.

| Variable | Default | Recommended Production | Description |
| :--- | :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | `Development` | `Production` | ASP.NET Core environment mode. |
| `SWAGGER_ENABLED` | *(null - auto)* | `false` | Force enables/disables Swagger UI at `/swagger`. In dev, defaults to `true`. In prod, defaults to `false`. |
| `CORS_ORIGINS` | `*` | `https://your-frontend.com` | Allowed CORS origins. Comma-separated or `*`. |
| `APP_TZ` | `America/Argentina/Buenos_Aires` | Your IANA Timezone | Timezone for end-of-day calculations and reminders. |
| `EOD_TIME` | `23:55` | `23:55` | End-of-Day cutoff time (HH:mm in 24h format). |
| `CATEGORIES` | `Trabajo,Estudio,Salud,Personal,Hogar,Otros` | Customized list | Comma-separated initial category names. |
| `DB_PATH` | `./data/roma.db` | `/app/data/roma.db` | Absolute or relative path to the SQLite database file. |
| `NOTIFY_ENABLED` | `false` | `true` (if using bot) | Enables the background Telegram notification worker. |
| `TELEGRAM_BOT_TOKEN` | *empty* | Bot Token | Required if `NOTIFY_ENABLED=true`. |
| `TELEGRAM_CHAT_ID` | *empty* | Target Chat ID | Required if `NOTIFY_ENABLED=true`. |
| `EVENT_REMINDER_OFFSETS` | `-7d,-3d,-1d,-2h,0` | `-7d,-3d,-1d,-2h,0` | Pre-alarm offsets before scheduled events. |

---

## 🌐 Development vs. Production Modes

### Environment Switching
Switch environments by setting:
```bash
ASPNETCORE_ENVIRONMENT=Production
# or
ASPNETCORE_ENVIRONMENT=Development
```

### Key Differences:
1. **Error Handling (`ExceptionHandlingMiddleware`)**:
   - **Development**: Unhandled exceptions return the full exception message and stack trace to assist debugging.
   - **Production**: Unhandled 500 errors return a sanitized RFC 7807 Problem Details response (`"An internal error occurred. Please contact support."`) to prevent sensitive data leakage.
2. **OpenAPI / Swagger (`/swagger`)**:
   - **Development**: Swagger UI is active at `/swagger` and root `/` redirects to `/swagger`.
   - **Production**: Swagger UI is disabled by default. Root `/` returns a lightweight JSON status `{ "name": "Roma API", "status": "running" }`. You can override this in production with `SWAGGER_ENABLED=true` if required.
3. **Reverse Proxy & Forwarded Headers**:
   - `UseForwardedHeaders` is enabled with `X-Forwarded-For` and `X-Forwarded-Proto`, allowing SSL-termination at Dokploy / Traefik / Cloudflare while maintaining correct protocol and client IP detection.

---

## 🔒 CORS Configuration

CORS is dynamically managed via the `CORS_ORIGINS` environment variable:

- **Specific Origins (Recommended for Production)**:
  ```env
  CORS_ORIGINS=https://roma.mydomain.com,https://admin.mydomain.com
  ```
  Enables `WithOrigins(...)`, `AllowAnyHeader()`, `AllowAnyMethod()`, and **`AllowCredentials()`**.
- **Wildcard (Development default)**:
  ```env
  CORS_ORIGINS=*
  ```
  Enables `AllowAnyOrigin()`, `AllowAnyHeader()`, and `AllowAnyMethod()` (credentials disabled as required by CORS security specs).

---

## 🚀 Dokploy Deployment Guide

Dokploy is an open-source alternative to Vercel/Heroku running on Docker and Traefik. Follow these steps to deploy Roma Backend:

### Step 1: Create Application in Dokploy
1. Log into your Dokploy dashboard.
2. Navigate to your **Project** and select **Create Application**.
3. Set the build type to **Dockerfile**.
4. Configure Git Repository pointing to your Roma repository, with Dockerfile path set to `Roma_backend/Dockerfile` (or build context set to `Roma_backend`).

### Step 2: Configure Network & Ports
- **Container Port**: Set to `8080` (`ASPNETCORE_HTTP_PORTS=8080`).
- Dokploy's Traefik will route incoming HTTPS traffic to port `8080` internally.

### Step 3: Configure Persistent Storage (CRITICAL for SQLite)
SQLite creates lock and journal files (`-wal` and `-shm`) alongside the database. Persistent volumes must be mounted to `/app/data`:
1. In Dokploy, go to the **Volumes / Mounts** tab of the application.
2. Add a Volume Mount:
   - **Host Path / Volume Name**: `roma_data` (or `/etc/dokploy/volumes/roma/data`)
   - **Mount Path**: `/app/data`
   - **Type**: Volume or Bind Mount.
3. *Note*: The Dockerfile automatically ensures the `/app/data` directory is owned by the unprivileged `app` user (`chown -R app:app /app/data`).

### Step 4: Configure Environment Variables in Dokploy
In the **Environment** tab of the application, paste:
```env
ASPNETCORE_ENVIRONMENT=Production
SWAGGER_ENABLED=false
CORS_ORIGINS=https://your-frontend-domain.com
DB_PATH=/app/data/roma.db
APP_TZ=America/Argentina/Buenos_Aires
EOD_TIME=23:55
NOTIFY_ENABLED=false
```

### Step 5: Configure Domain and SSL
1. Go to the **Domains** tab in Dokploy.
2. Enter your domain (e.g., `api.roma.yourdomain.com`).
3. Set the container port to `8080`.
4. Enable **Certificate (Let's Encrypt / HTTPS)**. Traefik will automatically handle SSL provisioning and renewal.

### Step 6: Healthcheck
Dokploy uses Docker's built-in healthcheck defined in the Dockerfile:
- Endpoint: `http://localhost:8080/health`
- Interval: `30s`, Timeout: `5s`, Retries: `3`.

---

## 🛡️ OWASP Top 10 Security Assessment & Hardening

Roma Backend has been hardened against the OWASP Top 10 (2021) standard:

### A01:2021 – Broken Access Control
- **Analysis**: Roma is designed as a single-tenant personal productivity service without internal multi-user authentication.
- **Implementation & Mitigations**:
  - Global **Soft Delete** enforcement (`is_deleted = 0`) prevents unauthorized data exposure or destructive physical purges.
  - **Dokploy Deployment Guard**: Roma should **not** be exposed publicly without reverse-proxy authentication. In Dokploy / Traefik, protect the route using **Basic Auth**, **OAuth2/ForwardAuth** (e.g. Authentik, Authelia, Cloudflare Zero Trust), or keep it inside an internal Docker network / private VPN.

### A02:2021 – Cryptographic Failures
- **Mitigation**:
  - Sensitive secrets (`TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`) are read exclusively from environment variables and never logged or serialized.
  - Production deployments use TLS/HTTPS terminated at Dokploy Traefik with automated certificate rotation.

### A03:2021 – Injection (SQL Injection)
- **Mitigation**:
  - **100% Parameterized Queries**: All database queries executed via Dapper utilize prepared parameter mappings (`@Date`, `@From`, `@To`, `@Id`, etc.).
  - Zero raw string concatenation or interpolation in SQL statements.

### A04:2021 – Insecure Design
- **Mitigation**:
  - Strict domain encapsulation and invariant validation (`DomainValidationException`).
  - Business rules (e.g. task transitions, date ranges, duplicate prevention) are enforced in domain and application logic prior to database interaction.

### A05:2021 – Security Misconfiguration
- **Mitigation**:
  - **Non-Root Execution**: Container runs under the unprivileged `app:app` user in the Alpine Linux runtime.
  - **OWASP Security Headers**: Injected into every HTTP response via middleware:
    - `X-Content-Type-Options: nosniff` (mitigates MIME-type confusion attacks).
    - `X-Frame-Options: DENY` (clickjacking defense).
    - `Referrer-Policy: strict-origin-when-cross-origin` (protects referrer privacy).
    - `Permissions-Policy: camera=(), microphone=(), geolocation=()` (disables unused browser capabilities).
  - **Environment Segregation**: Swagger UI and verbose stack traces are automatically disabled in `Production`.

### A06:2021 – Vulnerable and Outdated Components
- **Mitigation**:
  - Targets **.NET 10 LTS** on Alpine base images with minimal package surface area.
  - Dependencies are restricted to audited, core libraries (`Dapper`, `Microsoft.Data.Sqlite`, `Swashbuckle.AspNetCore`).

### A07:2021 – Identification and Authentication Failures
- **Mitigation**:
  - Delegated to the reverse proxy perimeter (Dokploy / Traefik / VPN) to prevent session hijacking and credential stuffing.

### A08:2021 – Software and Data Integrity Failures
- **Mitigation**:
  - SQLite WAL mode ensures ACID transactions with immediate durability.
  - Foreign key constraints enabled (`PRAGMA foreign_keys = ON`) to protect relational integrity.

### A09:2021 – Security Logging and Monitoring Failures
- **Mitigation**:
  - Structured console logging via `Microsoft.Extensions.Logging`.
  - Global `ExceptionHandlingMiddleware` logs internal errors with diagnostic context while returning sanitized, safe responses to clients.
  - Dedicated `/health` endpoint exposes real-time liveness checks for container orchestrators.

### A10:2021 – Server-Side Request Forgery (SSRF)
- **Mitigation**:
  - Outgoing HTTP calls are strictly restricted to the static official Telegram API (`https://api.telegram.org`) within `TelegramNotifier`.
  - No user-controlled endpoints or arbitrary webhooks are fetched.

---

## 🧪 Local Testing & Verification

### Running Tests
Execute the automated test suite from the repository:
```bash
cd Roma_backend
dotnet test
```

### Running the API locally
```bash
cd Roma_backend
dotnet run --project src/Roma.Api
```

### Building and Testing Docker Image locally
```bash
cd Roma_backend
docker build -t roma-backend:local .
docker run -d -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -v roma_data:/app/data \
  --name roma-test roma-backend:local
```
Verify the health endpoint:
```bash
curl http://localhost:8080/health
```
