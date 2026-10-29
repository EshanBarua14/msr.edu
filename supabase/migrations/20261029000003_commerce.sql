-- MSR.edu commerce: plans, products, orders, payments, refunds, entitlements.
-- Gateway-agnostic: `provider` is free text until a gateway is chosen.
-- All money is integer minor units (paisa for BDT). Clients never write to
-- financial tables; server functions using the service role do.

create type public.payment_state as enum (
  'created', 'pending', 'processing', 'successful', 'failed', 'cancelled', 'expired',
  'refund_pending', 'partially_refunded', 'refunded'
);
create type public.payment_method as enum ('bkash', 'nagad', 'card', 'bank_transfer', 'other');
create type public.billing_period as enum ('month', 'year', 'custom_days');
create type public.renewal_mode as enum ('manual', 'auto');
create type public.sale_status as enum ('active', 'inactive', 'archived');
create type public.product_kind as enum ('course', 'subscription_plan');
create type public.subscription_status as enum ('pending', 'active', 'grace', 'expired', 'cancelled');
create type public.entitlement_source as enum ('purchase', 'subscription', 'manual_grant', 'promotion');
create type public.entitlement_status as enum ('active', 'expired', 'suspended', 'revoked');
create type public.refund_status as enum ('requested', 'approved', 'rejected', 'processing', 'completed', 'failed');
create type public.review_status as enum ('submitted', 'approved', 'rejected');
create type public.reconciliation_status as enum ('open', 'investigating', 'resolved', 'ignored');
create type public.discount_kind as enum ('percent', 'fixed');

create domain public.currency_code as char(3) check (value ~ '^[A-Z]{3}$');
create domain public.money_minor as bigint check (value >= 0);

-- ---------------------------------------------------------------------------
-- Guards
-- ---------------------------------------------------------------------------

-- Allowed state transitions for orders and payment transactions.
create or replace function public.payment_transition_allowed(p_from public.payment_state, p_to public.payment_state)
returns boolean language sql immutable as $$
  select p_from = p_to or (p_from, p_to) in (
    ('created', 'pending'), ('created', 'processing'), ('created', 'successful'), ('created', 'failed'),
    ('created', 'cancelled'), ('created', 'expired'),
    ('pending', 'processing'), ('pending', 'successful'), ('pending', 'failed'),
    ('pending', 'cancelled'), ('pending', 'expired'),
    ('processing', 'successful'), ('processing', 'failed'), ('processing', 'cancelled'), ('processing', 'expired'),
    -- A verified payment that lands after the order expired is still honoured (and reconciled).
    ('expired', 'successful'),
    ('successful', 'refund_pending'), ('successful', 'partially_refunded'), ('successful', 'refunded'),
    ('refund_pending', 'successful'), ('refund_pending', 'partially_refunded'), ('refund_pending', 'refunded'),
    ('partially_refunded', 'refund_pending'), ('partially_refunded', 'refunded')
  );
$$;

create or replace function public.enforce_payment_transition()
returns trigger language plpgsql as $$
begin
  if not public.payment_transition_allowed(old.status, new.status) then
    raise exception 'invalid % status transition: % -> %', tg_table_name, old.status, new.status
      using errcode = 'check_violation';
  end if;
  return new;
end $$;

-- Reject updates to any column not listed in TG_ARGV.
create or replace function public.guard_immutable_columns()
returns trigger language plpgsql as $$
declare
  v_old jsonb := to_jsonb(old);
  v_new jsonb := to_jsonb(new);
  v_col text;
begin
  foreach v_col in array tg_argv loop
    v_old := v_old - v_col;
    v_new := v_new - v_col;
  end loop;
  if v_old is distinct from v_new then
    raise exception '% has immutable columns; only (%) may change', tg_table_name, array_to_string(tg_argv, ', ')
      using errcode = 'insufficient_privilege';
  end if;
  return new;
end $$;

-- ---------------------------------------------------------------------------
-- Catalogue
-- ---------------------------------------------------------------------------

create table public.subscription_plans (
  id              uuid primary key default gen_random_uuid(),
  slug            text not null unique check (slug ~ '^[a-z0-9-]+$'),
  name            text not null,
  description     text,
  price_minor     public.money_minor not null,
  currency        public.currency_code not null default 'BDT',
  billing_period  public.billing_period not null,
  period_days     int check (period_days > 0),
  renewal_mode    public.renewal_mode not null default 'manual',
  grace_days      int not null default 0 check (grace_days >= 0),
  -- true = every published course; otherwise plan_courses / plan_categories.
  all_courses     boolean not null default false,
  available_from  timestamptz,
  available_until timestamptz,
  status          public.sale_status not null default 'inactive',
  position        int not null default 0,
  created_at      timestamptz not null default now(),
  updated_at      timestamptz not null default now(),
  check (billing_period <> 'custom_days' or period_days is not null),
  check (available_until is null or available_from is null or available_until > available_from)
);
create trigger subscription_plans_updated_at before update on public.subscription_plans
  for each row execute function public.set_updated_at();

create table public.plan_courses (
  plan_id    uuid not null references public.subscription_plans (id) on delete cascade,
  course_id  uuid not null references public.courses (id) on delete cascade,
  primary key (plan_id, course_id)
);

create table public.plan_categories (
  plan_id      uuid not null references public.subscription_plans (id) on delete cascade,
  category_id  uuid not null references public.categories (id) on delete cascade,
  primary key (plan_id, category_id)
);

-- Purchasable items. One product per course or plan.
create table public.products (
  id                   uuid primary key default gen_random_uuid(),
  kind                 public.product_kind not null,
  course_id            uuid unique references public.courses (id) on delete restrict,
  plan_id              uuid unique references public.subscription_plans (id) on delete restrict,
  name                 text not null,
  price_minor          public.money_minor not null,
  currency             public.currency_code not null default 'BDT',
  promo_price_minor    public.money_minor,
  promo_starts_at      timestamptz,
  promo_ends_at        timestamptz,
  -- null = lifetime access (course products only).
  access_days          int check (access_days > 0),
  status               public.sale_status not null default 'inactive',
  created_at           timestamptz not null default now(),
  updated_at           timestamptz not null default now(),
  check ((kind = 'course' and course_id is not null and plan_id is null)
      or (kind = 'subscription_plan' and plan_id is not null and course_id is null and access_days is null)),
  check (promo_price_minor is null or promo_price_minor < price_minor),
  check (promo_ends_at is null or promo_starts_at is null or promo_ends_at > promo_starts_at)
);
create trigger products_updated_at before update on public.products
  for each row execute function public.set_updated_at();

-- Authoritative current price. Checkout must use this, never a browser value.
create or replace function public.current_price_minor(p_product uuid, p_at timestamptz default now())
returns bigint language sql stable as $$
  select case
    when promo_price_minor is not null
     and (promo_starts_at is null or promo_starts_at <= p_at)
     and (promo_ends_at is null or promo_ends_at > p_at)
    then promo_price_minor else price_minor end
  from public.products where id = p_product;
$$;

create table public.discounts (
  id               uuid primary key default gen_random_uuid(),
  code             text not null unique check (code = upper(code) and code ~ '^[A-Z0-9_-]{3,32}$'),
  kind             public.discount_kind not null,
  value            bigint not null check (value > 0),
  currency         public.currency_code,
  starts_at        timestamptz,
  ends_at          timestamptz,
  max_redemptions  int check (max_redemptions > 0),
  redemptions      int not null default 0 check (redemptions >= 0),
  active           boolean not null default false,
  created_at       timestamptz not null default now(),
  check (kind <> 'percent' or value <= 100),
  check (kind <> 'fixed' or currency is not null),
  check (max_redemptions is null or redemptions <= max_redemptions)
);

-- ---------------------------------------------------------------------------
-- Orders and payments
-- ---------------------------------------------------------------------------

create table public.orders (
  id              uuid primary key default gen_random_uuid(),
  reference       text not null unique
                    default ('MSR-' || to_char(now() at time zone 'utc', 'YYMMDD') || '-' || upper(encode(gen_random_bytes(4), 'hex'))),
  user_id         uuid not null references public.profiles (id) on delete restrict,
  status          public.payment_state not null default 'created',
  currency        public.currency_code not null,
  subtotal_minor  public.money_minor not null,
  discount_minor  public.money_minor not null default 0,
  total_minor     public.money_minor not null,
  discount_id     uuid references public.discounts (id),
  discount_code   text,
  expires_at      timestamptz,
  paid_at         timestamptz,
  created_at      timestamptz not null default now(),
  updated_at      timestamptz not null default now(),
  check (total_minor = subtotal_minor - discount_minor)
);
create index orders_user_idx on public.orders (user_id, created_at desc);
create index orders_status_idx on public.orders (status) where status in ('created', 'pending', 'processing', 'refund_pending');

create trigger orders_updated_at before update on public.orders
  for each row execute function public.set_updated_at();
create trigger orders_transition before update of status on public.orders
  for each row execute function public.enforce_payment_transition();
create trigger orders_immutable before update on public.orders
  for each row execute function public.guard_immutable_columns('status', 'paid_at', 'expires_at', 'updated_at');
create trigger orders_no_delete before delete on public.orders
  for each row execute function public.prevent_mutation();

-- Line items carry an immutable snapshot of what was sold and at what price.
create table public.order_items (
  id                uuid primary key default gen_random_uuid(),
  order_id          uuid not null references public.orders (id) on delete restrict,
  product_id        uuid not null references public.products (id) on delete restrict,
  quantity          int not null default 1 check (quantity = 1),
  unit_price_minor  public.money_minor not null,
  line_total_minor  public.money_minor not null,
  currency          public.currency_code not null,
  product_snapshot  jsonb not null,
  created_at        timestamptz not null default now(),
  check (line_total_minor = unit_price_minor * quantity),
  unique (order_id, product_id)
);
create trigger order_items_immutable before update or delete on public.order_items
  for each row execute function public.prevent_mutation();

create table public.payment_transactions (
  id                uuid primary key default gen_random_uuid(),
  order_id          uuid not null references public.orders (id) on delete restrict,
  provider          text not null check (provider ~ '^[a-z0-9_]+$'),
  method            public.payment_method not null,
  provider_txn_id   text,
  amount_minor      public.money_minor not null,
  currency          public.currency_code not null,
  status            public.payment_state not null default 'created',
  failure_reason    text,
  verified_at       timestamptz,
  created_at        timestamptz not null default now(),
  updated_at        timestamptz not null default now(),
  unique (provider, provider_txn_id),
  check (status <> 'successful' or (verified_at is not null and provider_txn_id is not null))
);
create index payment_transactions_order_idx on public.payment_transactions (order_id);

create trigger payment_transactions_updated_at before update on public.payment_transactions
  for each row execute function public.set_updated_at();
create trigger payment_transactions_transition before update of status on public.payment_transactions
  for each row execute function public.enforce_payment_transition();
create trigger payment_transactions_immutable before update on public.payment_transactions
  for each row execute function public.guard_immutable_columns('status', 'provider_txn_id', 'failure_reason', 'verified_at', 'updated_at');
create trigger payment_transactions_no_delete before delete on public.payment_transactions
  for each row execute function public.prevent_mutation();

-- Callback / webhook history. (provider, provider_event_id) makes processing idempotent.
-- Store a redacted payload only: no card data, tokens or secrets.
create table public.payment_events (
  id                 bigint generated always as identity primary key,
  provider           text not null,
  provider_event_id  text not null,
  transaction_id     uuid references public.payment_transactions (id),
  event_type         text not null,
  signature_valid    boolean,
  payload_redacted   jsonb,
  received_at        timestamptz not null default now(),
  processed_at       timestamptz,
  processing_error   text,
  attempts           int not null default 0,
  unique (provider, provider_event_id)
);
create trigger payment_events_immutable before update on public.payment_events
  for each row execute function public.guard_immutable_columns('transaction_id', 'processed_at', 'processing_error', 'attempts');
create trigger payment_events_no_delete before delete on public.payment_events
  for each row execute function public.prevent_mutation();

create table public.refunds (
  id                  uuid primary key default gen_random_uuid(),
  order_id            uuid not null references public.orders (id) on delete restrict,
  transaction_id      uuid not null references public.payment_transactions (id) on delete restrict,
  amount_minor        public.money_minor not null check (amount_minor > 0),
  currency            public.currency_code not null,
  reason              text not null,
  status              public.refund_status not null default 'requested',
  is_manual           boolean not null default false,
  provider_refund_id  text,
  requested_by        uuid references public.profiles (id),
  approved_by         uuid references public.profiles (id),
  approved_at         timestamptz,
  completed_at        timestamptz,
  created_at          timestamptz not null default now(),
  updated_at          timestamptz not null default now(),
  unique (transaction_id, provider_refund_id)
);
create trigger refunds_updated_at before update on public.refunds
  for each row execute function public.set_updated_at();
create trigger refunds_no_delete before delete on public.refunds
  for each row execute function public.prevent_mutation();

-- Refunds may never exceed what was paid on a transaction.
create or replace function public.check_refund_total()
returns trigger language plpgsql as $$
declare
  v_paid bigint;
  v_refunded bigint;
begin
  select amount_minor into v_paid from public.payment_transactions where id = new.transaction_id for update;
  select coalesce(sum(amount_minor), 0) into v_refunded
    from public.refunds
   where transaction_id = new.transaction_id and id <> new.id and status not in ('rejected', 'failed');
  if new.status not in ('rejected', 'failed') and v_refunded + new.amount_minor > v_paid then
    raise exception 'refund total % exceeds paid amount %', v_refunded + new.amount_minor, v_paid
      using errcode = 'check_violation';
  end if;
  return new;
end $$;
create trigger refunds_total before insert or update on public.refunds
  for each row execute function public.check_refund_total();

create sequence public.receipt_seq;
create table public.receipts (
  id              uuid primary key default gen_random_uuid(),
  receipt_number  text not null unique
                    default ('R-' || to_char(now() at time zone 'utc', 'YYYY') || '-' || lpad(nextval('public.receipt_seq')::text, 6, '0')),
  order_id        uuid not null unique references public.orders (id) on delete restrict,
  transaction_id  uuid not null references public.payment_transactions (id) on delete restrict,
  issued_at       timestamptz not null default now(),
  document_path   text
);
create trigger receipts_immutable before update on public.receipts
  for each row execute function public.guard_immutable_columns('document_path');
create trigger receipts_no_delete before delete on public.receipts
  for each row execute function public.prevent_mutation();

-- Only issue receipts for verified payments.
create or replace function public.check_receipt_verified()
returns trigger language plpgsql as $$
begin
  if not exists (
    select 1 from public.payment_transactions t
     where t.id = new.transaction_id and t.order_id = new.order_id
       and t.status in ('successful', 'refund_pending', 'partially_refunded', 'refunded')
       and t.verified_at is not null
  ) then
    raise exception 'receipt requires a verified payment for this order' using errcode = 'check_violation';
  end if;
  return new;
end $$;
create trigger receipts_verified before insert on public.receipts
  for each row execute function public.check_receipt_verified();

create table public.bank_payment_submissions (
  id                  uuid primary key default gen_random_uuid(),
  order_id            uuid not null references public.orders (id) on delete restrict,
  user_id             uuid not null references public.profiles (id) on delete restrict,
  bank_name           text not null,
  transfer_reference  text not null,
  amount_minor        public.money_minor not null,
  currency            public.currency_code not null,
  transferred_at      timestamptz not null,
  -- Object path in the private `payment-proofs` bucket.
  proof_path          text,
  status              public.review_status not null default 'submitted',
  reviewed_by         uuid references public.profiles (id),
  reviewed_at         timestamptz,
  review_note         text,
  created_at          timestamptz not null default now()
);
create index bank_payment_submissions_status_idx on public.bank_payment_submissions (status, created_at);

create table public.reconciliation_records (
  id              uuid primary key default gen_random_uuid(),
  order_id        uuid references public.orders (id),
  transaction_id  uuid references public.payment_transactions (id),
  issue           text not null,
  details         jsonb,
  status          public.reconciliation_status not null default 'open',
  resolved_by     uuid references public.profiles (id),
  resolved_at     timestamptz,
  resolution_note text,
  created_at      timestamptz not null default now()
);
create index reconciliation_open_idx on public.reconciliation_records (created_at) where status in ('open', 'investigating');

-- ---------------------------------------------------------------------------
-- Subscriptions and entitlements
-- ---------------------------------------------------------------------------

create table public.subscriptions (
  id                    uuid primary key default gen_random_uuid(),
  user_id               uuid not null references public.profiles (id) on delete restrict,
  plan_id               uuid not null references public.subscription_plans (id) on delete restrict,
  status                public.subscription_status not null default 'pending',
  current_period_start  timestamptz,
  current_period_end    timestamptz,
  grace_until           timestamptz,
  cancel_at_period_end  boolean not null default false,
  source_order_id       uuid references public.orders (id),
  provider_subscription_id text,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),
  check (current_period_end is null or current_period_end > current_period_start)
);
create index subscriptions_user_idx on public.subscriptions (user_id, status);
create trigger subscriptions_updated_at before update on public.subscriptions
  for each row execute function public.set_updated_at();

create table public.entitlements (
  id                    uuid primary key default gen_random_uuid(),
  user_id               uuid not null references public.profiles (id) on delete restrict,
  -- Exactly one scope: a single course, or a plan (resolved via plan coverage).
  course_id             uuid references public.courses (id) on delete restrict,
  plan_id               uuid references public.subscription_plans (id) on delete restrict,
  source                public.entitlement_source not null,
  source_order_item_id  uuid unique references public.order_items (id),
  subscription_id       uuid references public.subscriptions (id),
  granted_by            uuid references public.profiles (id),
  grant_reason          text,
  starts_at             timestamptz not null default now(),
  -- null = no expiry (lifetime).
  expires_at            timestamptz,
  status                public.entitlement_status not null default 'active',
  status_reason         text,
  created_at            timestamptz not null default now(),
  updated_at            timestamptz not null default now(),
  check ((course_id is null) <> (plan_id is null)),
  check (expires_at is null or expires_at > starts_at),
  check (source <> 'purchase' or source_order_item_id is not null),
  check (source <> 'subscription' or (subscription_id is not null and plan_id is not null)),
  check (source not in ('manual_grant', 'promotion') or (granted_by is not null and grant_reason is not null)),
  check (status in ('active', 'expired') or status_reason is not null)
);
create index entitlements_user_idx on public.entitlements (user_id) where status = 'active';
create unique index entitlements_subscription_period_idx on public.entitlements (subscription_id, starts_at)
  where subscription_id is not null;
create trigger entitlements_updated_at before update on public.entitlements
  for each row execute function public.set_updated_at();
create trigger entitlements_immutable before update on public.entitlements
  for each row execute function public.guard_immutable_columns('status', 'status_reason', 'expires_at', 'updated_at');
create trigger entitlements_no_delete before delete on public.entitlements
  for each row execute function public.prevent_mutation();

-- ---------------------------------------------------------------------------
-- Audit sensitive commerce changes (price changes, grants, refunds, ...).
-- ---------------------------------------------------------------------------

create trigger subscription_plans_audit after insert or update or delete on public.subscription_plans
  for each row execute function public.audit_row();
create trigger products_audit after insert or update or delete on public.products
  for each row execute function public.audit_row();
create trigger discounts_audit after insert or update or delete on public.discounts
  for each row execute function public.audit_row();
create trigger orders_audit after update on public.orders
  for each row execute function public.audit_row();
create trigger payment_transactions_audit after insert or update on public.payment_transactions
  for each row execute function public.audit_row();
create trigger refunds_audit after insert or update on public.refunds
  for each row execute function public.audit_row();
create trigger entitlements_audit after insert or update on public.entitlements
  for each row execute function public.audit_row();
create trigger subscriptions_audit after insert or update on public.subscriptions
  for each row execute function public.audit_row();
create trigger bank_payment_submissions_audit after update on public.bank_payment_submissions
  for each row execute function public.audit_row();
create trigger reconciliation_records_audit after insert or update on public.reconciliation_records
  for each row execute function public.audit_row();
