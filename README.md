# NexusCoreDotNet

Multi-tenant Resource Management SaaS built with ASP.NET Core 8 Razor Pages. Organizations track physical or digital assets, manage team members with role-based access, view utilization analytics, and browse real-time Kafka asset status change events — all behind Firebase Authentication and an admin-approval workflow.

> **Sister repos:**
>
> - [NexusCoreJS](https://github.com/jakevb8/NexusCore) — identical feature set, Next.js 15 + NestJS + TurboRepo (shares the same Neon database)
> - [NexusCoreAndroid](https://github.com/jakevb8/NexusCoreAndroid) — Jetpack Compose Android client
> - [NexusCoreReact](https://github.com/jakevb8/NexusCoreReact) — Expo React Native cross-platform client
> - [NexusCoreIOS](https://github.com/jakevb8/NexusCoreIOS) — SwiftUI iOS native client

**Live demo:** https://nexuscoredotnet-production.up.railway.app

---

## Tech Stack

| Layer    | Technology                                                            |
| -------- | --------------------------------------------------------------------- |
| UI + API | ASP.NET Core 8 Razor Pages (server-rendered, no separate API project) |
| Database | PostgreSQL on Neon (serverless), Entity Framework Core 8 + Npgsql     |
| Auth     | Firebase Authentication — **Google sign-in only**                     |
| Email    | Resend HTTP API (`onboarding@resend.dev`)                             |
| Caching  | `IMemoryCache` (5-minute TTL for reports)                             |
| CSV      | CsvHelper                                                             |
| UI libs  | Bootstrap 5 (CDN), Bootstrap Icons, Chart.js (CDN)                    |
| Hosting  | Railway (Docker)                                                      |

---

## Features

- **Multi-tenancy** — every resource is scoped to an organization; `organizationId` is sourced from the verified session cookie, never the request body
- **RBAC** — four-level role hierarchy: `SUPERADMIN > ORG_MANAGER > ASSET_MANAGER > VIEWER`
- **Admin approval flow** — new organizations auto-approve if daily approvals < 5 and total active orgs < 50; otherwise start as `PENDING`
- **Asset management** — full CRUD with status tracking (`AVAILABLE / IN_USE / MAINTENANCE / RETIRED`), CSV bulk-import, and a 100-asset trial limit
- **Audit log** — every mutating action is recorded synchronously with before/after diffs
- **Reports & analytics** — utilization rate and asset-by-status breakdown with a 5-minute in-memory cache
- **Team invites** — ORG_MANAGERs invite members by email (via Resend); invites expire after 7 days; copy-link fallback in the UI
- **Remove members** — ORG_MANAGERs can remove team members; self-removal and SUPERADMIN removal are blocked
- **Events page** — reads the `kafka_events` table (written by the NexusCoreJS Kafka consumer) and displays paginated asset status change history with 10-second auto-refresh
- **Rate limiting** — 300 req/15 min global; 5 req/hour per IP on registration/invite endpoints

---

## Local Development

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- A [Firebase project](https://console.firebase.google.com) with **Google sign-in** enabled
- A [Neon](https://neon.tech) PostgreSQL database (free tier works)
- A Firebase service account JSON (for Admin SDK token verification)

### 1. Clone

```bash
git clone https://github.com/jakevb8/NexusCoreDotNet.git
cd NexusCoreDotNet
```

### 2. Configure environment

Create `appsettings.Development.json` (gitignored):

```json
{
  "Firebase": {
    "ProjectId": "your-firebase-project-id"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=ep-xxx.neon.tech;Database=neondb;Username=neondb_owner;Password=xxx;SSL Mode=Require;Trust Server Certificate=true"
  },
  "Resend": {
    "ApiKey": "re_xxxx"
  },
  "App": {
    "FrontendUrl": "http://localhost:5000"
  }
}
```

Set the Firebase service account credentials:

```bash
export GOOGLE_APPLICATION_CREDENTIALS="/path/to/serviceaccount.json"
```

### 3. Run

```bash
dotnet run
# App starts on http://localhost:5000
```

### 4. Bootstrap the first SUPERADMIN

The first user to register becomes `ORG_MANAGER` of a `PENDING` organization (unless auto-approval fires). To approve the org and promote yourself to SUPERADMIN, run this SQL in your Neon console:

```sql
UPDATE organizations
SET status = 'ACTIVE'
WHERE id = (SELECT "organizationId" FROM users WHERE email = 'your@email.com');

UPDATE users
SET role = 'SUPERADMIN'
WHERE email = 'your@email.com';
```

---

## Schema Management

This project shares the same Neon PostgreSQL database as NexusCoreJS. The schema is owned and migrated by Prisma in the NexusCoreJS repo. This project maps to the same tables via EF Core's `OnModelCreating` configuration — it does not run EF migrations against the production database.

---

## Deployment

### Railway (recommended)

1. Push to GitHub
2. Create a new Railway project → Deploy from GitHub repo → Railway detects the `Dockerfile`
3. Add environment variables (no surrounding quotes):
   - `DATABASE_URL` — Neon connection string (`postgresql://...`)
   - `FIREBASE_PROJECT_ID`
   - `FIREBASE_CLIENT_EMAIL`
   - `FIREBASE_PRIVATE_KEY` — private key with literal `\n` for newlines
   - `Resend__ApiKey`
   - `App__FrontendUrl` → your Railway public URL

---

## REST API

In addition to Razor Pages, the app exposes a REST API at `/api/v1/*` for the mobile clients. All routes require a Firebase Bearer token (`Authorization: Bearer <token>`).

| Method | Path                      | Role required  | Description                                |
| ------ | ------------------------- | -------------- | ------------------------------------------ |
| GET    | `/api/v1/assets`          | VIEWER+        | List assets (paginated + search)           |
| POST   | `/api/v1/assets`          | ASSET_MANAGER+ | Create an asset                            |
| PUT    | `/api/v1/assets/{id}`     | ASSET_MANAGER+ | Update an asset                            |
| DELETE | `/api/v1/assets/{id}`     | ASSET_MANAGER+ | Delete an asset                            |
| POST   | `/api/v1/assets/import`   | ASSET_MANAGER+ | Bulk CSV import                            |
| GET    | `/api/v1/users`           | ORG_MANAGER+   | List org members                           |
| POST   | `/api/v1/users/invite`    | ORG_MANAGER+   | Invite a member by email                   |
| PATCH  | `/api/v1/users/{id}/role` | ORG_MANAGER+   | Change a member's role                     |
| DELETE | `/api/v1/users/{id}`      | ORG_MANAGER+   | Remove a member                            |
| GET    | `/api/v1/reports`         | VIEWER+        | Asset utilization + status breakdown       |
| GET    | `/api/v1/events`          | VIEWER+        | Paginated Kafka asset status change events |

---

## Cross-Repo Parity

This repo and [NexusCoreJS](https://github.com/jakevb8/NexusCore) implement the same feature set. When changing business logic, API contracts, or UI behaviour in one repo, apply the equivalent change to the other. See `AGENTS.md` for the full rule set.

---

## License

MIT
