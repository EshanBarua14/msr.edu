-- MSR.edu core: profiles, roles, settings, audit log, shared helpers.

create extension if not exists pgcrypto;

-- ---------------------------------------------------------------------------
-- Shared helpers
-- ---------------------------------------------------------------------------

create or replace function public.set_updated_at()
returns trigger language plpgsql as $$
begin
  new.updated_at := now();
  return new;
end $$;

-- ---------------------------------------------------------------------------
-- Profiles (1:1 with auth.users)
-- ---------------------------------------------------------------------------

create table public.profiles (
  id          uuid primary key references auth.users (id) on delete cascade,
  email       text,
  full_name   text,
  avatar_url  text,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now()
);

create trigger profiles_updated_at before update on public.profiles
  for each row execute function public.set_updated_at();

-- ---------------------------------------------------------------------------
-- Roles. owner_admin implies every other role in has_role().
-- ---------------------------------------------------------------------------

create table public.roles (
  key          text primary key check (key ~ '^[a-z_]+$'),
  name         text not null,
  description  text
);

insert into public.roles (key, name, description) values
  ('owner_admin',     'Owner / Admin',            'Full system configuration and access management'),
  ('content_manager', 'Content Manager',          'Manage courses, lessons and learning materials'),
  ('finance_manager', 'Finance / Payment Manager', 'Review transactions, refunds and financial reports'),
  ('support_staff',   'Support Staff',            'Limited user and transaction lookup for support'),
  ('learner',         'Learner',                  'Default role for every registered user');

create table public.user_roles (
  user_id     uuid not null references public.profiles (id) on delete cascade,
  role_key    text not null references public.roles (key),
  granted_by  uuid references public.profiles (id),
  granted_at  timestamptz not null default now(),
  primary key (user_id, role_key)
);

create or replace function public.has_role(p_role text, p_user uuid default auth.uid())
returns boolean
language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.user_roles
    where user_id = p_user and role_key in (p_role, 'owner_admin')
  );
$$;

create or replace function public.is_staff(p_user uuid default auth.uid())
returns boolean
language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.user_roles
    where user_id = p_user
      and role_key in ('owner_admin', 'content_manager', 'finance_manager', 'support_staff')
  );
$$;

-- New auth user -> profile + learner role.
create or replace function public.handle_new_user()
returns trigger language plpgsql security definer set search_path = public as $$
begin
  insert into public.profiles (id, email, full_name, avatar_url)
  values (
    new.id,
    new.email,
    coalesce(new.raw_user_meta_data ->> 'full_name', new.raw_user_meta_data ->> 'name'),
    new.raw_user_meta_data ->> 'avatar_url'
  )
  on conflict (id) do nothing;

  insert into public.user_roles (user_id, role_key)
  values (new.id, 'learner')
  on conflict do nothing;
  return new;
end $$;

create trigger on_auth_user_created after insert on auth.users
  for each row execute function public.handle_new_user();

-- ---------------------------------------------------------------------------
-- App settings: business policy lives here, not in code.
-- Defaults are placeholders pending owner confirmation (blueprint section 18).
-- ---------------------------------------------------------------------------

create table public.app_settings (
  key          text primary key,
  value        jsonb not null,
  description  text,
  is_public    boolean not null default false,
  updated_at   timestamptz not null default now(),
  updated_by   uuid references public.profiles (id)
);

create trigger app_settings_updated_at before update on public.app_settings
  for each row execute function public.set_updated_at();

insert into public.app_settings (key, value, description, is_public) values
  ('currency.default',            '"BDT"',        'Default pricing currency (ISO 4217)', true),
  ('currency.allowed',            '["BDT"]',      'Currencies admins may price in', true),
  ('subscription.renewal_mode',   '"manual"',     'manual | auto (auto only if the gateway supports recurring billing)', true),
  ('subscription.grace_days',     '0',            'Default grace period after a subscription period ends', false),
  ('course.default_access_days',  'null',         'null = lifetime access for individual course purchases', true),
  ('refund.access_policy',        '"revoke_immediately"', 'revoke_immediately | revoke_after_days', false),
  ('refund.revoke_after_days',    '0',            'Used when refund.access_policy = revoke_after_days', false),
  ('discounts.enabled',           'false',        'Coupon codes in checkout', true),
  ('bank_transfer.enabled',       'false',        'Manual bank transfer with admin verification', true),
  ('order.expiry_minutes',        '60',           'Unpaid orders expire after this many minutes', false),
  ('admin.model',                 '"single_owner"', 'single_owner | separate_roles', false);

-- ---------------------------------------------------------------------------
-- Audit log (append-only). Written by triggers and server functions.
-- ---------------------------------------------------------------------------

create table public.audit_logs (
  id           bigint generated always as identity primary key,
  actor_id     uuid,
  action       text not null,
  entity_type  text not null,
  entity_id    text,
  before       jsonb,
  after        jsonb,
  created_at   timestamptz not null default now()
);

create index audit_logs_entity_idx on public.audit_logs (entity_type, entity_id);
create index audit_logs_created_idx on public.audit_logs (created_at desc);

create or replace function public.prevent_mutation()
returns trigger language plpgsql as $$
begin
  raise exception '% is append-only', tg_table_name using errcode = 'insufficient_privilege';
end $$;

create trigger audit_logs_append_only before update or delete on public.audit_logs
  for each row execute function public.prevent_mutation();

-- Generic row audit trigger for sensitive tables.
create or replace function public.audit_row()
returns trigger language plpgsql security definer set search_path = public as $$
declare
  v_id text;
begin
  v_id := coalesce(
    (case when tg_op = 'DELETE' then to_jsonb(old) else to_jsonb(new) end) ->> 'id',
    (case when tg_op = 'DELETE' then to_jsonb(old) else to_jsonb(new) end) ->> 'key'
  );
  insert into public.audit_logs (actor_id, action, entity_type, entity_id, before, after)
  values (
    auth.uid(),
    lower(tg_op),
    tg_table_name,
    v_id,
    case when tg_op in ('UPDATE', 'DELETE') then to_jsonb(old) end,
    case when tg_op in ('INSERT', 'UPDATE') then to_jsonb(new) end
  );
  return coalesce(new, old);
end $$;

create trigger user_roles_audit after insert or update or delete on public.user_roles
  for each row execute function public.audit_row();
create trigger app_settings_audit after insert or update or delete on public.app_settings
  for each row execute function public.audit_row();
