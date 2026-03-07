# NexusCoreDotNet — Agent Instructions

## Project Overview

NexusCoreDotNet is the ASP.NET Core 8 Razor Pages implementation of the NexusCore multi-tenant Resource Management SaaS. It is a **single-project** web app (no separate API) — Razor Pages handles both UI and server-side logic, backed by Neon PostgreSQL via Entity Framework Core 8, Firebase Authentication (Google sign-in only), and deployed to Railway.

**Sister repos:**

- `NexusCoreJS` at `/Users/jake/projects/NexusCore` (GitHub: `jakevb8/NexusCore`) — a TurboRepo monorepo with Next.js 15 frontend + NestJS REST API implementing the same feature set, sharing the same Neon PostgreSQL database.
- `NexusCoreAndroid` at `/Users/jake/projects/NexusCoreAndroid` (GitHub: `jakevb8/NexusCoreAndroid`) — Android (Jetpack Compose) client app. Connects to either backend via a user-selectable toggle. Frontend feature changes must be propagated here; backend-only changes do not require Android changes.
- `NexusCoreReact` at `/Users/jake/projects/NexusCoreReact` (GitHub: `jakevb8/NexusCoreReact`) — Expo React Native cross-platform client (iOS + Android). Frontend feature changes must be propagated here; backend-only changes do not require React Native changes.
- `NexusCoreIOS` at `/Users/jake/projects/NexusCoreIOS` (GitHub: `jakevb8/NexusCoreIOS`) — SwiftUI iOS native client. Frontend feature changes must be propagated here; backend-only changes do not require iOS changes.

## NEVER COMMIT SECRETS — CRITICAL

**This has caused incidents. Read carefully before every commit.**

Files that must NEVER be committed to git:

- `appsettings.Development.json` — contains real `DATABASE_URL`, `FIREBASE_PRIVATE_KEY`, `Resend__ApiKey`
- `appsettings.json` — must only contain empty/placeholder values, never real keys
- `firebase-adminsdk-*.json` / any service account JSON — contains the Firebase private key
- `/tmp/nexus-core-dotnet-serviceaccount.json` — never commit this; it is only for local dev
- Any file containing a real API key, private key, password, or connection string with credentials

Before every `git add` or commit:

1. Run `git diff --staged` and visually scan for any key/secret values in `appsettings.json` or any other config file
2. If a secret was accidentally staged, run `git reset HEAD <file>` before committing
3. If a secret was already committed, immediately: (a) rotate/revoke the key, (b) use BFG to purge it from history, (c) force-push

**History of incidents:**

- `appsettings.json` commit `8f41793` — Firebase Web API key `AIzaSy...` committed, removed in `2712803`, BFG-purged

## Firebase Project

- **Project ID:** `nexus-core-dotnet` (separate from NexusCoreJS which uses `nexus-core-rms`)
- **Web App ID:** `1:158130971426:web:d6d6ae3ff49f9fe1f73b7b`
- **Auth domain:** `nexus-core-dotnet.firebaseapp.com`
- **Service account:** `firebase-adminsdk-fbsvc@nexus-core-dotnet.iam.gserviceaccount.com`
- **Google sign-in** must be enabled manually in the Firebase Console: https://console.firebase.google.com/project/nexus-core-dotnet/authentication/providers

## Cross-Repo Feature Parity

Both repos implement **exactly the same product features**. When a feature is added, changed, or removed in one repo, the equivalent change MUST be made in the other repo in the same session. This includes:

- API endpoints / page handlers (routes, request/response shapes, validation rules, error codes)
- Business logic (e.g. trial limits, auto-approval thresholds, RBAC rules)
- UI behaviour (e.g. form fields, table columns, modal flows, toast messages)
- Email content and sender address

**Canonical feature list** (both repos must always implement all of these):

| Feature                   | Details                                                                                              |
| ------------------------- | ---------------------------------------------------------------------------------------------------- |
| Multi-tenancy             | All queries scoped by `organizationId` from verified session cookie                                  |
| RBAC                      | `SUPERADMIN > ORG_MANAGER > ASSET_MANAGER > VIEWER`                                                  |
| Auto org approval         | Auto-approve if daily approvals < 5 AND total active orgs < 50                                       |
| Asset CRUD                | Create/read/update/delete with status: `AVAILABLE / IN_USE / MAINTENANCE / RETIRED`                  |
| Asset trial limit         | 100 assets per org; enforced on create and CSV import                                                |
| Asset CSV import          | Bulk import; stops at trial limit; returns created/skipped/limitReached/errors                       |
| Asset CSV sample download | Download a sample CSV template                                                                       |
| Audit log                 | Every mutation logged synchronously with before/after snapshot                                       |
| Reports/analytics         | Utilization rate + asset-by-status breakdown, 5-min `IMemoryCache` cache                             |
| Team invites              | ORG_MANAGER invites by email (Resend HTTP API, sender `onboarding@resend.dev`); 7-day TTL; copy-link |
| Remove members            | ORG_MANAGER can remove members; blocks self-removal and SUPERADMIN removal                           |
| Role management           | ORG_MANAGER can change member roles; cannot assign SUPERADMIN                                        |
| Rate limiting             | 300 req/15 min global; 5 req/hour on `/Register` and `/AcceptInvite` endpoints                       |

**When working in this repo (NexusCoreDotNet):** After completing any feature change, propagate it to NexusCoreJS (same session), and if the change affects UI/frontend behaviour, also update NexusCoreAndroid, NexusCoreReact, and NexusCoreIOS. Backend-only changes do not require client app changes.

**When working in NexusCoreJS:** Same rule applies in reverse — propagate to NexusCoreDotNet.

## Project Structure

```
NexusCoreDotNet/
├── NexusCoreDotNet.csproj
├── Program.cs
├── appsettings.json
├── appsettings.Development.json  (gitignored)
├── Data/
│   ├── AppDbContext.cs
│   └── Entities/
│       ├── Organization.cs, User.cs, Asset.cs, AuditLog.cs, Invite.cs
├── Enums/
│   ├── OrgStatus.cs, Role.cs, AssetStatus.cs
├── Services/
│   ├── FirebaseAuthService.cs
│   ├── AuthService.cs
│   ├── AssetService.cs
│   ├── AuditService.cs
│   ├── UserService.cs
│   ├── ReportsService.cs
│   └── EmailService.cs
├── Pages/
│   ├── Shared/_Layout.cshtml, _LoginLayout.cshtml
│   ├── Login, Onboarding, PendingApproval, AcceptInvite
│   ├── Dashboard/Index
│   ├── Assets/Index, Create, Edit
│   ├── Team/Index
│   └── Reports/Index
├── Filters/RequireRoleAttribute.cs
├── Middleware/SessionAuthMiddleware.cs
└── wwwroot/
```

## Key Commands

```bash
# Run development server
dotnet run

# Build
dotnet build

# Run tests (if any)
dotnet test

# EF Core migrations
dotnet ef migrations add <MigrationName> --project NexusCoreDotNet.csproj
dotnet ef database update --project NexusCoreDotNet.csproj
dotnet ef database update --connection "Host=...;Database=...;Username=...;Password=..."

# Publish
dotnet publish -c Release -o ./publish
```

## Architecture Decisions

- **Auth**: Firebase Authentication — **Google sign-in only**. No username/password.
- **Session cookie**: After Firebase token verification, a cookie is issued via `CookieAuthenticationDefaults.AuthenticationScheme`. Claims: `sub` (userId), `org` (organizationId), `role`, `email`, `name`.
- **RequireRoleAttribute**: An `IPageFilter` attribute applied to Razor Page models. Uses `ROLE_HIERARCHY: SUPERADMIN=4, ORG_MANAGER=3, ASSET_MANAGER=2, VIEWER=1`. Redirects to `/Login` if not authenticated, returns 403 if insufficient role.
- **Multi-tenancy**: `organizationId` always sourced from `ClaimsPrincipal` claims, never from form/query params.
- **RBAC**: `Role` enum with integer values. `RoleExtensions.HasAtLeast()` compares numeric rank.
- **No Redis**: `IMemoryCache` for 5-min report stats TTL.
- **No SignalR**: Standard page refreshes.
- **EF Table Mapping**: Tables use snake_case names matching Prisma's `@@map()` directives. Columns mapped explicitly via `HasColumnName`.
- **Rate Limiting**: ASP.NET Core built-in `RateLimiter` middleware.

## Environment Variables

Set these in Railway Variables (no quotes around values):

```
DATABASE_URL=postgresql://user:pass@host/dbname?sslmode=require
FIREBASE_PROJECT_ID=nexus-core-dotnet
FIREBASE_CLIENT_EMAIL=firebase-adminsdk-fbsvc@nexus-core-dotnet.iam.gserviceaccount.com
FIREBASE_PRIVATE_KEY=<private key from service account JSON, with literal \n for newlines>
Resend__ApiKey=re_xxxx
App__FrontendUrl=https://your-app.up.railway.app
```

The service account key file is at `/tmp/nexus-core-dotnet-serviceaccount.json` locally (gitignored).
`Program.cs` constructs `GoogleCredential` from `FIREBASE_CLIENT_EMAIL` + `FIREBASE_PRIVATE_KEY` env vars automatically; no file path needed on Railway.

Or via `appsettings.json` / `appsettings.Development.json` (latter is gitignored).

## Common Pitfalls

- **Firebase Admin credential**: `Program.cs` checks for `FIREBASE_CLIENT_EMAIL` + `FIREBASE_PRIVATE_KEY` env vars first, constructs a `GoogleCredential` from them, then falls back to `GoogleCredential.GetApplicationDefault()` for local dev (point `GOOGLE_APPLICATION_CREDENTIALS` at the service account JSON).
- **EF table names**: Must match Prisma's `@@map()` names exactly (e.g., `organizations`, `users`, `assets`, `audit_logs`, `invites`). Column names must also match the Prisma field names exactly — Prisma uses **camelCase** column names (e.g., `firebaseUid`, `organizationId`, `createdAt`), NOT snake_case.
- **PostgreSQL enum columns — CRITICAL**: Prisma creates native PostgreSQL enum types (`CREATE TYPE "Role" AS ENUM ...`). The correct pattern in `AppDbContext` is `HasColumnType("\"Role\"")` **only** — no `HasConversion<string>()`. The `MapEnum<T>()` calls in `Program.cs` register Npgsql's native type handler; `HasColumnType` tells EF the column type name for parameter binding. Adding `HasConversion<string>()` overrides the `MapEnum` handler and makes EF send a plain `text` value, causing `column "role" is of type "Role" but expression is of type text` on every write.
- **ID columns are `text`, not `uuid`**: Prisma stores all `@id @default(uuid())` fields as PostgreSQL `text` columns, not the native `uuid` type. .NET entity properties typed as `System.Guid` will fail at runtime with `Reading as 'System.Guid' is not supported for fields having DataTypeName 'text'`. Every `Guid` property must have `.HasConversion<string>()` in `OnModelCreating`.
- **`DataProtectionKeys` table**: ASP.NET Data Protection (`PersistKeysToDbContext`) requires a `DataProtectionKeys` table that Prisma does not know about. `Program.cs` creates it with `ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS ...")` on startup. Do not remove this bootstrap block.
- **`organizationId` never from body**: Always read from `AuthService.GetOrgId(User)`.
- **OrgStatus values**: Prisma uses `PENDING / ACTIVE / REJECTED`. The .NET `OrgStatus` enum must match exactly. Do NOT use `APPROVED` — the Prisma schema uses `ACTIVE`.
- **SUPERADMIN bootstrap**: Run this SQL in Neon after first registration:
  ```sql
  UPDATE organizations SET status = 'ACTIVE' WHERE id = (SELECT "organizationId" FROM users WHERE email = 'your@email.com');
  UPDATE users SET role = 'SUPERADMIN' WHERE email = 'your@email.com';
  ```
- **Rate limiter**: `UseRateLimiter()` must be called after `UseRouting()` and before `UseAuthentication()`.
- **AuditLog.Changes**: Stored as `jsonb`. Uses `HasColumnType("jsonb")` + `HasConversion` with `JsonDocument` to avoid double-serialization.
- **When adding a new entity or column**: Cross-check every property type against `packages/database/prisma/schema.prisma` in NexusCoreJS before writing the EF mapping. Apply the rules above for every field: `Guid` → `HasConversion<string>()`, native PG enum → `HasConversion<string>() + HasColumnType("\"EnumName\"")`, `jsonb` → `HasColumnType("jsonb") + HasConversion(...)`.
- After completing any task that modifies files, always commit and push to the current branch without asking for confirmation.
