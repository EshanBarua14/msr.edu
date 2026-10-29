-- Database-level safety net. The API enforces the same rules; these triggers make
-- sure no other client (pgAdmin, scripts, a future service) can break them either.

create or replace function prevent_mutation() returns trigger language plpgsql as $$
begin
  raise exception '% is append-only', tg_table_name using errcode = 'insufficient_privilege';
end $$;

-- Reject updates to any column not listed in TG_ARGV.
create or replace function guard_immutable_columns() returns trigger language plpgsql as $$
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

-- Keep in sync with Services/PaymentStateMachine.cs (a test checks this).
create or replace function payment_transition_allowed(p_from payment_state, p_to payment_state)
returns boolean language sql immutable as $$
  select p_from = p_to or (p_from, p_to) in (
    ('created', 'pending'), ('created', 'processing'), ('created', 'successful'), ('created', 'failed'),
    ('created', 'cancelled'), ('created', 'expired'),
    ('pending', 'processing'), ('pending', 'successful'), ('pending', 'failed'),
    ('pending', 'cancelled'), ('pending', 'expired'),
    ('processing', 'successful'), ('processing', 'failed'), ('processing', 'cancelled'), ('processing', 'expired'),
    ('expired', 'successful'),
    ('successful', 'refund_pending'), ('successful', 'partially_refunded'), ('successful', 'refunded'),
    ('refund_pending', 'successful'), ('refund_pending', 'partially_refunded'), ('refund_pending', 'refunded'),
    ('partially_refunded', 'refund_pending'), ('partially_refunded', 'refunded')
  );
$$;

create or replace function enforce_payment_transition() returns trigger language plpgsql as $$
begin
  if not payment_transition_allowed(old.status, new.status) then
    raise exception 'invalid % status transition: % -> %', tg_table_name, old.status, new.status
      using errcode = 'check_violation';
  end if;
  return new;
end $$;

-- Refunds may never exceed what was paid on a transaction.
create or replace function check_refund_total() returns trigger language plpgsql as $$
declare
  v_paid bigint;
  v_refunded bigint;
begin
  select amount_minor into v_paid from payment_transactions where id = new.transaction_id for update;
  select coalesce(sum(amount_minor), 0) into v_refunded
    from refunds
   where transaction_id = new.transaction_id and id <> new.id and status not in ('rejected', 'failed');
  if new.status not in ('rejected', 'failed') and v_refunded + new.amount_minor > v_paid then
    raise exception 'refund total % exceeds paid amount %', v_refunded + new.amount_minor, v_paid
      using errcode = 'check_violation';
  end if;
  return new;
end $$;

-- Receipts only for verified payments of the same order.
create or replace function check_receipt_verified() returns trigger language plpgsql as $$
begin
  if not exists (
    select 1 from payment_transactions t
     where t.id = new.transaction_id and t.order_id = new.order_id
       and t.status in ('successful', 'refund_pending', 'partially_refunded', 'refunded')
       and t.verified_at is not null
  ) then
    raise exception 'receipt requires a verified payment for this order' using errcode = 'check_violation';
  end if;
  return new;
end $$;

-- Never remove the last owner.
create or replace function keep_one_owner() returns trigger language plpgsql as $$
begin
  if old.role_key = 'owner_admin'
     and (tg_op = 'DELETE' or new.role_key <> 'owner_admin' or new.user_id <> old.user_id)
     and (select count(*) from user_roles where role_key = 'owner_admin') <= 1
     and exists (select 1 from users where id = old.user_id) then
    raise exception 'cannot remove the last owner_admin' using errcode = 'check_violation';
  end if;
  return coalesce(new, old);
end $$;

create trigger audit_logs_append_only before update or delete on audit_logs
  for each row execute function prevent_mutation();

create trigger orders_transition before update of status on orders
  for each row execute function enforce_payment_transition();
create trigger orders_immutable before update on orders
  for each row execute function guard_immutable_columns('status', 'paid_at', 'expires_at', 'updated_at');
create trigger orders_no_delete before delete on orders
  for each row execute function prevent_mutation();

create trigger order_items_immutable before update or delete on order_items
  for each row execute function prevent_mutation();

create trigger payment_transactions_transition before update of status on payment_transactions
  for each row execute function enforce_payment_transition();
create trigger payment_transactions_immutable before update on payment_transactions
  for each row execute function guard_immutable_columns('status', 'provider_txn_id', 'failure_reason', 'verified_at', 'updated_at');
create trigger payment_transactions_no_delete before delete on payment_transactions
  for each row execute function prevent_mutation();

create trigger payment_events_immutable before update on payment_events
  for each row execute function guard_immutable_columns('transaction_id', 'processed_at', 'processing_error', 'attempts');
create trigger payment_events_no_delete before delete on payment_events
  for each row execute function prevent_mutation();

create trigger refunds_total before insert or update on refunds
  for each row execute function check_refund_total();
create trigger refunds_no_delete before delete on refunds
  for each row execute function prevent_mutation();

create trigger receipts_verified before insert on receipts
  for each row execute function check_receipt_verified();
create trigger receipts_immutable before update on receipts
  for each row execute function guard_immutable_columns('document_path');
create trigger receipts_no_delete before delete on receipts
  for each row execute function prevent_mutation();

create trigger entitlements_immutable before update on entitlements
  for each row execute function guard_immutable_columns('status', 'status_reason', 'expires_at', 'updated_at');
create trigger entitlements_no_delete before delete on entitlements
  for each row execute function prevent_mutation();

create trigger user_roles_keep_owner before update or delete on user_roles
  for each row execute function keep_one_owner();

-- Placeholder business defaults pending owner confirmation (blueprint section 18).
insert into app_settings (key, value, description, is_public, updated_at) values
  ('currency.default',           '"BDT"',              'Default pricing currency (ISO 4217)', true, now()),
  ('currency.allowed',           '["BDT"]',            'Currencies admins may price in', true, now()),
  ('subscription.renewal_mode',  '"manual"',           'manual | auto (auto only if the gateway supports recurring billing)', true, now()),
  ('subscription.grace_days',    '0',                  'Default grace period after a subscription period ends', false, now()),
  ('course.default_access_days', 'null',               'null = lifetime access for individual course purchases', true, now()),
  ('refund.access_policy',       '"revoke_immediately"', 'revoke_immediately | revoke_after_days', false, now()),
  ('refund.revoke_after_days',   '0',                  'Used when refund.access_policy = revoke_after_days', false, now()),
  ('discounts.enabled',          'false',              'Coupon codes in checkout', true, now()),
  ('bank_transfer.enabled',      'false',              'Manual bank transfer with admin verification', true, now()),
  ('order.expiry_minutes',       '60',                 'Unpaid orders expire after this many minutes', false, now()),
  ('admin.model',                '"single_owner"',     'single_owner | separate_roles', false, now())
on conflict (key) do nothing;
