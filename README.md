# MSR.edu

Paid learning platform: free previews, individual course purchases and subscription memberships, with quizzes, progress tracking and an admin console.

**Stack:** React 19 · TypeScript · Vite · Tailwind CSS v4 · Motion · React Router · Supabase (Auth with Google, Postgres + RLS, Storage).

## Status

Blueprint phases 2–3 (foundation, authentication, core interface).

| Area | State |
|---|---|
| Design system, routing, landing page | Done |
| Animated Google sign-in, auth callback, role-aware routing | Done |
| Learner dashboard and admin console shells | Done (data views are placeholders) |
| Database schema, RLS, entitlement checks | Done, tested |
| Courses, Drive, quizzes (phase 4) | Not started |
| Checkout, gateway integration (phases 5–6) | Not started; no gateway chosen |

## Local development

```bash
npm install
cp .env.example .env.local   # optional; see below
npm run dev
```

Without Supabase settings the app runs in **local preview mode** (dev builds only): the sign-in page offers buttons to preview the app as a learner, owner admin, content manager or finance manager. Preview mode is compiled out of production builds.

Scripts: `npm run lint`, `npm run typecheck`, `npm test`, `npm run build`.

## Supabase setup

1. Create a Supabase project. Put its URL and **anon** key in `.env.local` (and in the hosting provider's env vars).
2. Apply migrations: `supabase link --project-ref <ref>` then `supabase db push`.
3. **Auth → Providers → Google:** enable, add the Google OAuth client ID/secret (Google Cloud console, authorized redirect URI `https://<ref>.supabase.co/auth/v1/callback`).
4. **Auth → URL configuration:** site URL = your app URL; add `http://localhost:5173/auth/callback` and `https://<your-domain>/auth/callback` to redirect URLs.
5. Sign in once with the owner's Google account, then make it the owner in the SQL editor:
   ```sql
   insert into public.user_roles (user_id, role_key)
   select id, 'owner_admin' from auth.users where email = 'owner@example.com';
   ```

Never put the service-role key, payment gateway secrets, Google Drive or AI credentials in `VITE_*` variables or in the repo. They belong in Supabase Edge Function secrets.

## Database tests

`scripts/test-db.sh` applies every migration to a throwaway PostgreSQL database (with small Supabase stubs) and runs the access-control and payment-integrity tests in `supabase/tests/`. CI runs it on every PR.

```bash
DATABASE_URL=postgres://postgres:postgres@localhost:5432/postgres scripts/test-db.sh
```

## Security model

- Route guards in the UI are convenience only. **Row Level Security is the enforcement.**
- Lesson metadata (syllabus) is public for published courses; lesson bodies (`lesson_contents`), resources and quiz questions are readable only when `can_view_lesson` / `can_view_resource` / `can_view_quiz` allow it: free preview → valid purchase → active subscription covering the course → staff.
- Answer keys (`question_keys`, `question_correct_options`) and Drive file IDs (`resource_sources`) are staff/server only.
- Clients cannot write orders, transactions, refunds, receipts, subscriptions or entitlements. Server functions (service role) do, after verifying payments with the gateway.
- Entitlements are created only by `grant_order_entitlements(order)`, which requires a `successful` order with a verified transaction matching amount and currency, and is idempotent (safe on duplicate webhooks). It is not callable by browsers.
- Order amounts and line-item price snapshots are immutable; status changes follow a fixed state machine; financial rows cannot be deleted; refunds cannot exceed the paid amount; receipts require a verified payment.
- Price, plan, discount, role, entitlement and payment changes are written to an append-only `audit_logs` table.

## Business settings (pending confirmation)

These placeholder defaults live in the `app_settings` table and can be changed without code changes:

| Setting | Default |
|---|---|
| Currency | BDT only |
| Subscription renewal | Manual |
| Individual course access | Lifetime (`access_days` per product overrides) |
| Refund access policy | Revoke immediately |
| Discount codes | Off |
| Bank transfer | Off (manual admin verification flow when enabled) |
| Admin model | Single owner-admin (roles for content, finance and support already exist) |
