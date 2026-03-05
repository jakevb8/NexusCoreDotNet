# NexusCoreDotNet

Multi-tenant Resource Management SaaS built with ASP.NET Core 8 Razor Pages. Organizations track physical or digital assets, manage team members with role-based access, and view utilization analytics — all behind Firebase Authentication and an admin-approval workflow.

> **Sister repo:** [NexusCoreJS](https://github.com/jakevb8/NexusCore) — identical feature set built with Next.js 15 + NestJS + TurboRepo. Both repos share the same Neon PostgreSQL database.

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
| Hosting  | Railway (Docker) or Azure App Service                                 |

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
    "ProjectId": "your-firebase-project-id",
    "ApiKey": "AIza...",
    "AuthDomain": "your-project.firebaseapp.com",
    "StorageBucket": "your-project.appspot.com",
    "MessagingSenderId": "123456789",
    "AppId": "1:123:web:abc"
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

## EF Core Migrations

This project uses the same Neon PostgreSQL database as NexusCoreJS. The schema is managed by Prisma in the NexusCoreJS repo. If you need to run EF migrations independently:

```bash
# Add a migration
dotnet ef migrations add <MigrationName>

# Apply to database
dotnet ef database update

# Or with explicit connection string
dotnet ef database update --connection "Host=...;Database=...;Username=...;Password=..."
```

> **Note:** Since the schema is owned by Prisma, prefer running `prisma migrate deploy` from NexusCoreJS rather than EF migrations to avoid drift.

---

## Deployment

### Railway (recommended)

1. Push to GitHub
2. Create a new Railway project → Deploy from GitHub repo
3. Railway detects the `Dockerfile` or uses Nixpacks for .NET
4. Add environment variables:
   - `DATABASE_URL` — Neon connection string
   - `FIREBASE_PROJECT_ID`
   - Firebase web SDK keys as `Firebase__ApiKey`, `Firebase__AuthDomain`, etc.
   - `GOOGLE_APPLICATION_CREDENTIALS` or individual `FIREBASE_CLIENT_EMAIL` / `FIREBASE_PRIVATE_KEY`
   - `Resend__ApiKey`
   - `App__FrontendUrl` → your Railway public URL

### Azure App Service

```bash
dotnet publish -c Release -o ./publish
# Deploy ./publish via Azure CLI, GitHub Actions, or ZIP deploy
```

---

## Cross-Repo Parity

This repo and [NexusCoreJS](https://github.com/jakevb8/NexusCore) implement the same feature set. When changing business logic, API contracts, or UI behaviour in one repo, apply the equivalent change to the other. See `AGENTS.md` for details.

---

## License

MIT
