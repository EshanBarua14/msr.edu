-- Access-control and payment-integrity tests. Run via scripts/test-db.sh.
\set ON_ERROR_STOP 1
set client_min_messages = warning;

create or replace function pg_temp.ok(cond boolean, msg text) returns void language plpgsql as $$
begin
  if cond is not true then raise exception 'FAIL: %', msg; end if;
  raise notice 'ok - %', msg;
end $$;

-- Expect a statement to fail. Runs it in a subtransaction.
create or replace function pg_temp.fails(stmt text, msg text) returns void language plpgsql as $$
begin
  begin
    execute stmt;
  exception when others then
    raise notice 'ok - % (%)', msg, sqlerrm;
    return;
  end;
  raise exception 'FAIL: expected error: %', msg;
end $$;

create or replace function pg_temp.as_user(p uuid) returns void language plpgsql as $$
begin
  perform set_config('request.jwt.claim.sub', coalesce(p::text, ''), false);
  if p is null then execute 'set role anon'; else execute 'set role authenticated'; end if;
end $$;

create or replace function pg_temp.as_admin() returns void language plpgsql as $$
begin
  execute 'reset role';
  perform set_config('request.jwt.claim.sub', '', false);
end $$;

set client_min_messages = notice;

-- ---------------------------------------------------------------- fixtures
insert into auth.users (id, email, raw_user_meta_data) values
  ('00000000-0000-0000-0000-00000000000a', 'owner@x.test', '{"full_name":"Owner"}'),
  ('00000000-0000-0000-0000-00000000000b', 'learner@x.test', '{"name":"Lena Learner"}'),
  ('00000000-0000-0000-0000-00000000000c', 'other@x.test', '{}');
insert into public.user_roles (user_id, role_key) values ('00000000-0000-0000-0000-00000000000a', 'owner_admin');

select pg_temp.ok((select full_name from public.profiles where id = '00000000-0000-0000-0000-00000000000b') = 'Lena Learner',
  'signup creates profile from Google metadata');
select pg_temp.ok(exists (select 1 from public.user_roles where user_id = '00000000-0000-0000-0000-00000000000b' and role_key = 'learner'),
  'signup grants learner role');

insert into public.courses (id, slug, title, status) values
  ('10000000-0000-0000-0000-000000000001', 'algebra', 'Algebra', 'published'),
  ('10000000-0000-0000-0000-000000000002', 'draft-course', 'Draft', 'draft');
insert into public.lessons (id, course_id, slug, title, is_preview, status) values
  ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'intro', 'Intro', true, 'published'),
  ('20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 'paid', 'Paid lesson', false, 'published');
insert into public.lesson_contents (lesson_id, body) values
  ('20000000-0000-0000-0000-000000000001', 'free body'),
  ('20000000-0000-0000-0000-000000000002', 'paid body');
insert into public.quizzes (id, course_id, title, status) values
  ('30000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Quiz 1', 'published');
insert into public.questions (id, quiz_id, prompt, review_status) values
  ('31000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', '2+2?', 'approved');
insert into public.question_options (id, question_id, label) values
  ('32000000-0000-0000-0000-000000000001', '31000000-0000-0000-0000-000000000001', '4');
insert into public.question_correct_options (option_id, question_id) values
  ('32000000-0000-0000-0000-000000000001', '31000000-0000-0000-0000-000000000001');

insert into public.products (id, kind, course_id, name, price_minor, status) values
  ('40000000-0000-0000-0000-000000000001', 'course', '10000000-0000-0000-0000-000000000001', 'Algebra', 150000, 'active');
insert into public.subscription_plans (id, slug, name, price_minor, billing_period, all_courses, status, grace_days) values
  ('50000000-0000-0000-0000-000000000001', 'monthly', 'Monthly', 50000, 'month', true, 'active', 3);
insert into public.products (id, kind, plan_id, name, price_minor, status) values
  ('40000000-0000-0000-0000-000000000002', 'subscription_plan', '50000000-0000-0000-0000-000000000001', 'Monthly', 50000, 'active');

-- ---------------------------------------------------------------- anonymous visitor
select pg_temp.as_user(null);
select pg_temp.ok((select count(*) from public.courses) = 1, 'anon sees only published courses');
select pg_temp.ok((select count(*) from public.lessons) = 2, 'anon sees lesson syllabus');
select pg_temp.ok((select array_agg(body) from public.lesson_contents) = array['free body'], 'anon reads only preview content');
select pg_temp.ok((select count(*) from public.questions) = 0, 'anon cannot read paid quiz questions');
select pg_temp.fails($$insert into public.courses (slug, title) values ('x', 'x')$$, 'anon cannot create courses');

-- ---------------------------------------------------------------- learner, no purchase
select pg_temp.as_user('00000000-0000-0000-0000-00000000000b');
select pg_temp.ok((select count(*) from public.lesson_contents) = 1, 'learner without purchase reads only preview');
select pg_temp.ok(not public.has_course_access('10000000-0000-0000-0000-000000000001'), 'no access before purchase');
select pg_temp.fails($$insert into public.user_roles (user_id, role_key) values ('00000000-0000-0000-0000-00000000000b', 'owner_admin')$$,
  'learner cannot self-assign owner_admin');
select pg_temp.fails($$insert into public.entitlements (user_id, course_id, source, granted_by, grant_reason)
  values ('00000000-0000-0000-0000-00000000000b', '10000000-0000-0000-0000-000000000001', 'manual_grant', '00000000-0000-0000-0000-00000000000b', 'x')$$,
  'learner cannot grant themselves access');
select pg_temp.fails($$insert into public.orders (user_id, currency, subtotal_minor, total_minor, status)
  values ('00000000-0000-0000-0000-00000000000b', 'BDT', 1, 1, 'successful')$$, 'learner cannot create orders directly');
select pg_temp.fails($$select public.grant_order_entitlements('00000000-0000-0000-0000-000000000000')$$,
  'learner cannot call grant_order_entitlements');
select pg_temp.ok((select count(*) from public.question_correct_options) = 0, 'learner cannot read answer keys');
select pg_temp.ok((select count(*) from public.app_settings where not is_public) = 0, 'learner cannot read private settings');
update public.products set price_minor = 1 where id = '40000000-0000-0000-0000-000000000001';
select pg_temp.as_admin();
select pg_temp.ok((select price_minor from public.products where id = '40000000-0000-0000-0000-000000000001') = 150000,
  'learner price tampering has no effect');

-- ---------------------------------------------------------------- course purchase (server side)
insert into public.orders (id, user_id, currency, subtotal_minor, total_minor, status) values
  ('60000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-00000000000b', 'BDT', 150000, 150000, 'pending');
insert into public.order_items (id, order_id, product_id, unit_price_minor, line_total_minor, currency, product_snapshot) values
  ('61000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001',
   150000, 150000, 'BDT', '{"name":"Algebra"}');
insert into public.payment_transactions (id, order_id, provider, method, amount_minor, currency, status) values
  ('62000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001', 'sandbox', 'bkash', 150000, 'BDT', 'pending');

select pg_temp.ok(public.current_price_minor('40000000-0000-0000-0000-000000000001') = 150000, 'server price lookup');
select pg_temp.ok(length((select reference from public.orders where id = '60000000-0000-0000-0000-000000000001')) > 10, 'order reference generated');
select pg_temp.fails($$select public.grant_order_entitlements('60000000-0000-0000-0000-000000000001')$$,
  'pending order cannot grant access');
select pg_temp.fails($$update public.orders set total_minor = 1 where id = '60000000-0000-0000-0000-000000000001'$$,
  'order amounts are immutable');
select pg_temp.fails($$update public.order_items set unit_price_minor = 1 where id = '61000000-0000-0000-0000-000000000001'$$,
  'order item snapshot is immutable');
select pg_temp.fails($$update public.payment_transactions set status = 'successful' where id = '62000000-0000-0000-0000-000000000001'$$,
  'successful transaction requires verification + provider txn id');
select pg_temp.fails($$insert into public.receipts (order_id, transaction_id) values
  ('60000000-0000-0000-0000-000000000001', '62000000-0000-0000-0000-000000000001')$$, 'no receipt before verification');

-- Mismatched amount: success status alone is not enough.
update public.orders set status = 'successful', paid_at = now() where id = '60000000-0000-0000-0000-000000000001';
select pg_temp.fails($$select public.grant_order_entitlements('60000000-0000-0000-0000-000000000001')$$,
  'order without a verified matching transaction cannot grant access');

update public.payment_transactions set status = 'successful', provider_txn_id = 'TXN-1', verified_at = now()
 where id = '62000000-0000-0000-0000-000000000001';
select pg_temp.ok(public.grant_order_entitlements('60000000-0000-0000-0000-000000000001') = 1, 'verified payment grants entitlement');
select pg_temp.ok(public.grant_order_entitlements('60000000-0000-0000-0000-000000000001') = 0, 'replayed grant is a no-op (idempotent)');
select pg_temp.ok((select count(*) from public.entitlements where user_id = '00000000-0000-0000-0000-00000000000b') = 1,
  'exactly one entitlement after duplicate processing');
insert into public.receipts (order_id, transaction_id) values
  ('60000000-0000-0000-0000-000000000001', '62000000-0000-0000-0000-000000000001');

insert into public.payment_events (provider, provider_event_id, event_type) values ('sandbox', 'EVT-1', 'payment.succeeded');
select pg_temp.fails($$insert into public.payment_events (provider, provider_event_id, event_type) values ('sandbox', 'EVT-1', 'payment.succeeded')$$,
  'duplicate webhook event rejected');
select pg_temp.fails($$insert into public.payment_transactions (order_id, provider, method, provider_txn_id, amount_minor, currency)
  values ('60000000-0000-0000-0000-000000000001', 'sandbox', 'bkash', 'TXN-1', 150000, 'BDT')$$, 'duplicate gateway transaction id rejected');
select pg_temp.fails($$update public.orders set status = 'pending' where id = '60000000-0000-0000-0000-000000000001'$$,
  'successful order cannot move back to pending');
select pg_temp.fails($$delete from public.payment_transactions$$, 'transactions cannot be deleted');

select pg_temp.as_user('00000000-0000-0000-0000-00000000000b');
select pg_temp.ok(public.has_course_access('10000000-0000-0000-0000-000000000001'), 'access after verified purchase');
select pg_temp.ok((select count(*) from public.lesson_contents) = 2, 'learner reads paid content after purchase');
select pg_temp.ok((select count(*) from public.questions) = 1, 'learner reads quiz after purchase');
select pg_temp.ok((select count(*) from public.orders) = 1 and (select count(*) from public.receipts) = 1, 'learner sees own order and receipt');
-- No update policy: the write silently affects 0 rows.
update public.entitlements set status = 'active', expires_at = now() + interval '100 years';
select pg_temp.as_admin();
select pg_temp.ok((select expires_at from public.entitlements where user_id = '00000000-0000-0000-0000-00000000000b') is null,
  'learner entitlement update has no effect');

select pg_temp.as_user('00000000-0000-0000-0000-00000000000c');
select pg_temp.ok((select count(*) from public.orders) = 0, 'other users cannot see the order');
select pg_temp.ok((select count(*) from public.lesson_contents) = 1, 'other users still only see preview');
select pg_temp.fails($$insert into public.bank_payment_submissions (order_id, user_id, bank_name, transfer_reference, amount_minor, currency, transferred_at)
  values ('60000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-00000000000c', 'B', 'R', 1, 'BDT', now())$$,
  'cannot submit bank proof for someone else''s order');
select pg_temp.as_admin();

-- ---------------------------------------------------------------- refunds and revocation
insert into public.refunds (order_id, transaction_id, amount_minor, currency, reason) values
  ('60000000-0000-0000-0000-000000000001', '62000000-0000-0000-0000-000000000001', 100000, 'BDT', 'partial');
select pg_temp.fails($$insert into public.refunds (order_id, transaction_id, amount_minor, currency, reason) values
  ('60000000-0000-0000-0000-000000000001', '62000000-0000-0000-0000-000000000001', 60000, 'BDT', 'too much')$$,
  'refunds cannot exceed the amount paid');
select pg_temp.fails($$update public.entitlements set status = 'revoked' where user_id = '00000000-0000-0000-0000-00000000000b'$$,
  'revocation requires a reason');
update public.entitlements set status = 'revoked', status_reason = 'refunded' where user_id = '00000000-0000-0000-0000-00000000000b';
select pg_temp.ok(not public.has_course_access('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-00000000000b'),
  'revoked entitlement removes access');

-- ---------------------------------------------------------------- subscription purchase + renewal
insert into public.orders (id, user_id, currency, subtotal_minor, total_minor, status) values
  ('60000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-00000000000c', 'BDT', 50000, 50000, 'pending');
insert into public.order_items (order_id, product_id, unit_price_minor, line_total_minor, currency, product_snapshot) values
  ('60000000-0000-0000-0000-000000000002', '40000000-0000-0000-0000-000000000002', 50000, 50000, 'BDT', '{}');
insert into public.payment_transactions (order_id, provider, method, provider_txn_id, amount_minor, currency) values
  ('60000000-0000-0000-0000-000000000002', 'sandbox', 'nagad', 'TXN-2', 50000, 'BDT');
update public.payment_transactions set status = 'successful', verified_at = now() where provider_txn_id = 'TXN-2';
update public.orders set status = 'successful' where id = '60000000-0000-0000-0000-000000000002';
select pg_temp.ok(public.grant_order_entitlements('60000000-0000-0000-0000-000000000002') = 1, 'subscription grants plan entitlement');
select pg_temp.ok(public.has_course_access('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-00000000000c'),
  'all-courses plan covers course');

-- Renewal: second order extends from the end of the current period.
insert into public.orders (id, user_id, currency, subtotal_minor, total_minor, status) values
  ('60000000-0000-0000-0000-000000000003', '00000000-0000-0000-0000-00000000000c', 'BDT', 50000, 50000, 'pending');
insert into public.order_items (order_id, product_id, unit_price_minor, line_total_minor, currency, product_snapshot) values
  ('60000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000002', 50000, 50000, 'BDT', '{}');
insert into public.payment_transactions (order_id, provider, method, provider_txn_id, amount_minor, currency, status, verified_at) values
  ('60000000-0000-0000-0000-000000000003', 'sandbox', 'card', 'TXN-3', 50000, 'BDT', 'successful', now());
update public.orders set status = 'successful' where id = '60000000-0000-0000-0000-000000000003';
select public.grant_order_entitlements('60000000-0000-0000-0000-000000000003');
select pg_temp.ok(
  (select max(current_period_end) from public.subscriptions where user_id = '00000000-0000-0000-0000-00000000000c')
    > now() + interval '55 days',
  'renewal stacks onto the existing period');

-- Expiry is enforced.
insert into public.entitlements (user_id, course_id, source, granted_by, grant_reason, starts_at, expires_at) values
  ('00000000-0000-0000-0000-00000000000b', '10000000-0000-0000-0000-000000000001', 'manual_grant',
   '00000000-0000-0000-0000-00000000000a', 'trial', now() - interval '2 days', now() - interval '1 day');
select pg_temp.ok(not public.has_course_access('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-00000000000b'),
  'expired entitlement grants no access');
select pg_temp.fails($$update public.entitlements set starts_at = now() where source = 'manual_grant'$$,
  'entitlement scope and start are immutable');

-- ---------------------------------------------------------------- admin safety
select pg_temp.fails($$delete from public.audit_logs$$, 'audit log is append-only');
select pg_temp.ok((select count(*) from public.audit_logs where entity_type = 'entitlements') >= 3, 'entitlement changes are audited');
select pg_temp.fails($$delete from public.user_roles where role_key = 'owner_admin'$$, 'last owner cannot be removed');

select pg_temp.as_user('00000000-0000-0000-0000-00000000000a');
select pg_temp.ok((select count(*) from public.orders) = 3, 'owner sees all orders');
select pg_temp.ok((select count(*) from public.courses) = 2, 'owner sees draft courses');
select pg_temp.as_admin();

\echo 'all access and payment tests passed'
