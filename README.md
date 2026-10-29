# MSR.edu

Paid learning platform: free previews, individual course purchases and subscription memberships, with quizzes, progress tracking and an admin console.

| Part | Stack | Folder |
|---|---|---|
| Frontend | React 19, TypeScript, Vite, Tailwind CSS v4, Motion | `frontend/` |
| Backend | ASP.NET Core (.NET 10) minimal API, EF Core | `backend/` |
| Database | PostgreSQL 16+ (manage with pgAdmin) | `backend/src/MsrEdu.Api/Data/Migrations`, `backend/database/schema.sql` |
| Auth | Google OAuth on the API, HttpOnly session cookie | `backend/src/MsrEdu.Api/Auth` |

## Prerequisites (Windows)

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22](https://nodejs.org)
- [PostgreSQL 16+ with pgAdmin](https://www.postgresql.org/download/windows/)
- Git

## First-time setup

```powershell
git clone https://github.com/EshanBarua14/msr.edu E:\mrs.edu
cd E:\mrs.edu
```

### 1. Database (pgAdmin)

1. In pgAdmin, create a database named `msr_edu` (owner `postgres`).
2. Create the schema, either:
   - **from the API** (recommended): `cd backend`, then `dotnet tool restore` and
     `dotnet ef database update -p src/MsrEdu.Api`, or
   - **from pgAdmin**: open the Query Tool on `msr_edu`, load `backend/database/schema.sql` and run it.
     The script is idempotent, so it is safe to re-run after pulling new migrations.

The default development connection string is in `backend/src/MsrEdu.Api/appsettings.Development.json`
(`Host=localhost;Port=5432;Database=msr_edu;Username=postgres;Password=postgres`). If your password differs,
override it without editing the file:

```powershell
cd backend/src/MsrEdu.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=msr_edu;Username=postgres;Password=YOUR_PASSWORD"
```

### 2. Google sign-in

1. Google Cloud Console → APIs & Services → Credentials → **Create OAuth client ID** (Web application).
2. Authorised redirect URIs:
   - `http://localhost:5173/api/auth/google/callback` (development, through the Vite proxy)
   - `https://YOUR-DOMAIN/api/auth/google/callback` (production)
3. Store the credentials as user secrets (never in the repo):

```powershell
cd backend/src/MsrEdu.Api
dotnet user-secrets set "Authentication:Google:ClientId" "xxxx.apps.googleusercontent.com"
dotnet user-secrets set "Authentication:Google:ClientSecret" "xxxx"
dotnet user-secrets set "Auth:OwnerEmails:0" "you@gmail.com"
```

`Auth:OwnerEmails` makes that Google account the **owner admin** the first time it signs in. The owner assigns other roles (`PUT /api/admin/users/{id}/roles`).

Until Google is configured, development builds show **Development: sign in as** buttons on the login page (learner, owner, content, finance). They use `POST /api/auth/dev-login`, which the API refuses to enable outside the Development environment.

### 3. Run

Two terminals:

```powershell
# API on http://localhost:5080
cd backend/src/MsrEdu.Api
dotnet run

# Frontend on http://localhost:5173 (proxies /api to the API)
cd frontend
npm install
npm run dev
```

Open http://localhost:5173.

## Tests

```powershell
# Frontend: lint, types, unit tests, build
cd frontend; npm run lint; npm run typecheck; npm test; npm run build

# Backend: integration tests against a real PostgreSQL (each test class creates and drops its own database)
cd backend
$env:MSR_TEST_DB = "Host=localhost;Port=5432;Username=postgres;Password=postgres"
dotnet test
```

CI runs both on every pull request. It also checks that migrations match the model and that `backend/database/schema.sql` is current.

## Database changes

```powershell
cd backend
dotnet ef migrations add <Name> -p src/MsrEdu.Api -o Data/Migrations
dotnet ef database update -p src/MsrEdu.Api
dotnet ef migrations script --idempotent -p src/MsrEdu.Api -o database/schema.sql   # keep the pgAdmin script current
```

Database triggers (state machine, immutability, refund cap, receipt checks, last-owner guard) live in `Data/Sql/*.sql` and are applied by the `IntegrityGuards` migration.

## Production build

The API serves the built frontend from the same origin (no CORS, simple cookies):

```powershell
cd frontend; npm ci; npm run build
Copy-Item -Recurse -Force dist\* ..\backend\src\MsrEdu.Api\wwwroot\
cd ..\backend; dotnet publish src/MsrEdu.Api -c Release -o out
```

On the server, set `ConnectionStrings__Default`, `Authentication__Google__ClientId`, `Authentication__Google__ClientSecret` and `Auth__OwnerEmails__0` as environment variables. Behind a reverse proxy, forward `X-Forwarded-Proto` and `X-Forwarded-Host`.

## Security model

- **The API is the only gatekeeper.** The browser never talks to the database. Route guards in React only shape navigation.
- **Session:** after Google sign-in the API issues an HttpOnly, SameSite=Lax cookie (Secure in production) that holds only the user id. Roles are read from the database on every request, so revoking a role takes effect immediately.
- **CSRF:** every state-changing `/api` call must send `X-MSR-CSRF: 1`. Browsers can't add that header cross-site without CORS, and CORS is disabled.
- **Content access** (`Services/AccessService.cs`) is checked in this order: free preview → free course (signed in) → active course purchase → active subscription covering the course (all courses, listed courses or category) → staff. Otherwise paid lesson content returns 401 or 402.
- **Entitlements** are created only by `EntitlementService.GrantForOrderAsync`. It requires a successful order with a verified transaction matching the amount and currency, locks the order row, and is idempotent, so duplicate or concurrent webhooks are safe.
- **Database guards** apply even to pgAdmin or scripts: the payment status state machine, immutable order amounts and line snapshots, no deletes on financial rows, refunds ≤ amount paid, receipts only for verified payments, an append-only audit log, and the last owner can't be removed.
- **Audit:** changes to roles, settings, plans, prices, discounts, orders, transactions, refunds, subscriptions and entitlements are written to `audit_logs` along with the user who made them.
- **Secrets:** Google, payment gateway and AI credentials live in user secrets or server environment variables, never in the repo or the frontend.

## Business settings (pending confirmation)

These are seeded in the `app_settings` table. Change them there; no code changes are needed.

| Setting | Default |
|---|---|
| Currency | BDT only |
| Subscription renewal | Manual |
| Individual course access | Lifetime (`products.access_days` overrides) |
| Refund access policy | Revoke immediately |
| Discount codes | Off |
| Bank transfer | Off |
| Admin model | Single owner-admin (content, finance and support roles already exist) |
