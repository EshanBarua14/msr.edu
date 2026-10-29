-- MSR.edu access control: entitlement checks, Row Level Security, storage.
-- UI route guards are convenience only; these policies are the enforcement.

-- ---------------------------------------------------------------------------
-- Entitlement checks
-- ---------------------------------------------------------------------------

create or replace function public.plan_covers_course(p_plan uuid, p_course uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (
    select 1 from public.subscription_plans sp
     where sp.id = p_plan
       and (
         sp.all_courses
         or exists (select 1 from public.plan_courses pc where pc.plan_id = sp.id and pc.course_id = p_course)
         or exists (
           select 1 from public.plan_categories pcat
             join public.courses c on c.category_id = pcat.category_id
            where pcat.plan_id = sp.id and c.id = p_course
         )
       )
  );
$$;

-- Paid access via purchase, subscription or explicit grant. Pending or failed
-- payments never create entitlements, so they can never satisfy this check.
create or replace function public.has_course_access(p_course uuid, p_user uuid default auth.uid())
returns boolean language sql stable security definer set search_path = public as $$
  select p_user is not null and exists (
    select 1 from public.entitlements e
     where e.user_id = p_user
       and e.status = 'active'
       and e.starts_at <= now()
       and (e.expires_at is null or e.expires_at > now())
       and (e.course_id = p_course or (e.plan_id is not null and public.plan_covers_course(e.plan_id, p_course)))
  );
$$;

create or replace function public.course_is_published(p_course uuid)
returns boolean language sql stable security definer set search_path = public as $$
  select exists (select 1 from public.courses where id = p_course and status = 'published');
$$;

-- Access priority (blueprint 2.D): preview -> purchase -> subscription -> staff/grant.
create or replace function public.can_view_lesson(p_lesson uuid, p_user uuid default auth.uid())
returns boolean language sql stable security definer set search_path = public as $$
  select coalesce((
    select
      (l.status = 'published' and c.status = 'published' and (
         l.is_preview
         or (p_user is not null and c.is_free)
         or public.has_course_access(c.id, p_user)
      ))
      or (p_user is not null and public.is_staff(p_user))
    from public.lessons l join public.courses c on c.id = l.course_id
    where l.id = p_lesson
  ), false);
$$;

create or replace function public.can_view_resource(p_resource uuid, p_user uuid default auth.uid())
returns boolean language sql stable security definer set search_path = public as $$
  select coalesce((
    select
      (r.status = 'published' and c.status = 'published' and (
         r.is_preview
         or (r.lesson_id is not null and public.can_view_lesson(r.lesson_id, p_user))
         or (p_user is not null and c.is_free)
         or public.has_course_access(c.id, p_user)
      ))
      or (p_user is not null and public.is_staff(p_user))
    from public.resources r join public.courses c on c.id = r.course_id
    where r.id = p_resource
  ), false);
$$;

create or replace function public.can_view_quiz(p_quiz uuid, p_user uuid default auth.uid())
returns boolean language sql stable security definer set search_path = public as $$
  select coalesce((
    select
      (q.status = 'published' and c.status = 'published' and (
         q.is_preview
         or (p_user is not null and c.is_free)
         or public.has_course_access(c.id, p_user)
      ))
      or (p_user is not null and public.is_staff(p_user))
    from public.quizzes q join public.courses c on c.id = q.course_id
    where q.id = p_quiz
  ), false);
$$;

-- ---------------------------------------------------------------------------
-- Server-only: grant entitlements for a verified order. Idempotent: replaying
-- it (e.g. duplicate webhooks) never creates a second entitlement.
-- ---------------------------------------------------------------------------

create or replace function public.grant_order_entitlements(p_order uuid)
returns int language plpgsql security definer set search_path = public as $$
declare
  v_order public.orders;
  v_item record;
  v_plan public.subscription_plans;
  v_sub_id uuid;
  v_start timestamptz;
  v_end timestamptz;
  v_count int := 0;
  v_rows int;
begin
  select * into v_order from public.orders where id = p_order for update;
  if not found then
    raise exception 'order % not found', p_order;
  end if;
  if v_order.status <> 'successful' then
    raise exception 'order % is %, entitlements require a successful verified payment', v_order.reference, v_order.status
      using errcode = 'check_violation';
  end if;
  if not exists (
    select 1 from public.payment_transactions t
     where t.order_id = p_order and t.status = 'successful' and t.verified_at is not null
       and t.amount_minor = v_order.total_minor and t.currency = v_order.currency
  ) then
    raise exception 'order % has no verified transaction matching amount and currency', v_order.reference
      using errcode = 'check_violation';
  end if;

  for v_item in
    select oi.id, p.kind, p.course_id, p.plan_id, p.access_days
      from public.order_items oi join public.products p on p.id = oi.product_id
     where oi.order_id = p_order
  loop
    if exists (select 1 from public.entitlements where source_order_item_id = v_item.id) then
      continue;
    end if;

    if v_item.kind = 'course' then
      insert into public.entitlements (user_id, course_id, source, source_order_item_id, starts_at, expires_at)
      values (
        v_order.user_id, v_item.course_id, 'purchase', v_item.id, now(),
        case when v_item.access_days is null then null else now() + make_interval(days => v_item.access_days) end
      )
      on conflict (source_order_item_id) do nothing;
    else
      select * into v_plan from public.subscription_plans where id = v_item.plan_id;
      -- Renewals extend from the end of the user's latest active period.
      select greatest(now(), coalesce(max(current_period_end), now())) into v_start
        from public.subscriptions
       where user_id = v_order.user_id and plan_id = v_plan.id and status in ('active', 'grace');
      v_end := v_start + case v_plan.billing_period
        when 'month' then interval '1 month'
        when 'year' then interval '1 year'
        else make_interval(days => v_plan.period_days) end;

      insert into public.subscriptions (user_id, plan_id, status, current_period_start, current_period_end, grace_until, source_order_id)
      values (v_order.user_id, v_plan.id, 'active', v_start, v_end,
              v_end + make_interval(days => v_plan.grace_days), p_order)
      returning id into v_sub_id;

      insert into public.entitlements (user_id, plan_id, source, source_order_item_id, subscription_id, starts_at, expires_at)
      values (v_order.user_id, v_plan.id, 'subscription', v_item.id, v_sub_id, v_start,
              v_end + make_interval(days => v_plan.grace_days))
      on conflict (source_order_item_id) do nothing;
    end if;
    get diagnostics v_rows = row_count;
    v_count := v_count + v_rows;
  end loop;
  return v_count;
end $$;

revoke execute on function public.grant_order_entitlements(uuid) from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- Row Level Security
-- ---------------------------------------------------------------------------

do $$
declare t text;
begin
  foreach t in array array[
    'profiles', 'roles', 'user_roles', 'app_settings', 'audit_logs',
    'categories', 'courses', 'modules', 'lessons', 'lesson_contents', 'resources', 'resource_sources',
    'enrollments', 'lesson_progress', 'quizzes', 'questions', 'question_options', 'question_keys',
    'question_correct_options', 'quiz_attempts', 'quiz_answers',
    'subscription_plans', 'plan_courses', 'plan_categories', 'products', 'discounts',
    'orders', 'order_items', 'payment_transactions', 'payment_events', 'refunds', 'receipts',
    'bank_payment_submissions', 'reconciliation_records', 'subscriptions', 'entitlements'
  ] loop
    execute format('alter table public.%I enable row level security', t);
  end loop;
end $$;

-- Identity -------------------------------------------------------------------

create policy "profiles: read own or staff" on public.profiles for select
  using (id = auth.uid() or public.has_role('support_staff'));
create policy "profiles: update own" on public.profiles for update
  using (id = auth.uid()) with check (id = auth.uid());

create policy "roles: readable" on public.roles for select using (true);

create policy "user_roles: read own or owner" on public.user_roles for select
  using (user_id = auth.uid() or public.has_role('owner_admin'));
create policy "user_roles: owner manages" on public.user_roles for all
  using (public.has_role('owner_admin')) with check (public.has_role('owner_admin'));

-- Never remove the last owner.
create or replace function public.keep_one_owner()
returns trigger language plpgsql security definer set search_path = public as $$
begin
  if old.role_key = 'owner_admin'
     and (tg_op = 'DELETE' or new.role_key <> 'owner_admin' or new.user_id <> old.user_id)
     and (select count(*) from public.user_roles where role_key = 'owner_admin') <= 1 then
    raise exception 'cannot remove the last owner_admin' using errcode = 'check_violation';
  end if;
  return coalesce(new, old);
end $$;
create trigger user_roles_keep_owner before update or delete on public.user_roles
  for each row execute function public.keep_one_owner();

create policy "app_settings: public or staff read" on public.app_settings for select
  using (is_public or public.is_staff());
create policy "app_settings: owner writes" on public.app_settings for all
  using (public.has_role('owner_admin')) with check (public.has_role('owner_admin'));

create policy "audit_logs: finance/owner read" on public.audit_logs for select
  using (public.has_role('finance_manager'));

-- Learning content ------------------------------------------------------------

create policy "categories: readable" on public.categories for select using (true);
create policy "categories: content writes" on public.categories for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "courses: published or staff" on public.courses for select
  using (status = 'published' or public.is_staff());
create policy "courses: content writes" on public.courses for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "modules: published course or staff" on public.modules for select
  using (public.course_is_published(course_id) or public.is_staff());
create policy "modules: content writes" on public.modules for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

-- Lesson metadata (titles, preview flags) is public for the syllabus.
create policy "lessons: published or staff" on public.lessons for select
  using ((status = 'published' and public.course_is_published(course_id)) or public.is_staff());
create policy "lessons: content writes" on public.lessons for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "lesson_contents: entitled" on public.lesson_contents for select
  using (public.can_view_lesson(lesson_id));
create policy "lesson_contents: content writes" on public.lesson_contents for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "resources: entitled" on public.resources for select
  using (public.can_view_resource(id));
create policy "resources: content writes" on public.resources for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "resource_sources: content staff" on public.resource_sources for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "enrollments: own" on public.enrollments for select
  using (user_id = auth.uid() or public.has_role('support_staff'));
create policy "enrollments: start own" on public.enrollments for insert
  with check (user_id = auth.uid() and public.course_is_published(course_id));
create policy "enrollments: touch own" on public.enrollments for update
  using (user_id = auth.uid()) with check (user_id = auth.uid());

create policy "lesson_progress: own" on public.lesson_progress for select
  using (user_id = auth.uid() or public.has_role('support_staff'));
create policy "lesson_progress: record own" on public.lesson_progress for insert
  with check (user_id = auth.uid() and public.can_view_lesson(lesson_id));
create policy "lesson_progress: update own" on public.lesson_progress for update
  using (user_id = auth.uid()) with check (user_id = auth.uid() and public.can_view_lesson(lesson_id));

create policy "quizzes: published or staff" on public.quizzes for select
  using ((status = 'published' and public.course_is_published(course_id)) or public.is_staff());
create policy "quizzes: content writes" on public.quizzes for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "questions: approved and entitled" on public.questions for select
  using ((review_status = 'approved' and public.can_view_quiz(quiz_id)) or public.has_role('content_manager'));
create policy "questions: content writes" on public.questions for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "question_options: entitled" on public.question_options for select
  using (
    public.has_role('content_manager') or exists (
      select 1 from public.questions q
       where q.id = question_id and q.review_status = 'approved' and public.can_view_quiz(q.quiz_id)
    )
  );
create policy "question_options: content writes" on public.question_options for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

create policy "question_keys: content staff" on public.question_keys for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));
create policy "question_correct_options: content staff" on public.question_correct_options for all
  using (public.has_role('content_manager')) with check (public.has_role('content_manager'));

-- Attempts are created and graded by a server function (no client writes).
create policy "quiz_attempts: own or staff" on public.quiz_attempts for select
  using (user_id = auth.uid() or public.has_role('content_manager') or public.has_role('support_staff'));
create policy "quiz_answers: own or staff" on public.quiz_answers for select
  using (exists (
    select 1 from public.quiz_attempts a
     where a.id = attempt_id
       and (a.user_id = auth.uid() or public.has_role('content_manager') or public.has_role('support_staff'))
  ));

-- Commerce: catalogue -----------------------------------------------------------

create policy "subscription_plans: active or staff" on public.subscription_plans for select
  using (status = 'active' or public.is_staff());
create policy "subscription_plans: finance writes" on public.subscription_plans for all
  using (public.has_role('finance_manager')) with check (public.has_role('finance_manager'));

create policy "plan_courses: readable" on public.plan_courses for select using (true);
create policy "plan_courses: finance writes" on public.plan_courses for all
  using (public.has_role('finance_manager')) with check (public.has_role('finance_manager'));
create policy "plan_categories: readable" on public.plan_categories for select using (true);
create policy "plan_categories: finance writes" on public.plan_categories for all
  using (public.has_role('finance_manager')) with check (public.has_role('finance_manager'));

create policy "products: active or staff" on public.products for select
  using (status = 'active' or public.is_staff());
create policy "products: finance writes" on public.products for all
  using (public.has_role('finance_manager')) with check (public.has_role('finance_manager'));

create policy "discounts: finance" on public.discounts for all
  using (public.has_role('finance_manager')) with check (public.has_role('finance_manager'));

-- Commerce: records (read-only to clients; server functions write) ---------------

create policy "orders: own or finance/support" on public.orders for select
  using (user_id = auth.uid() or public.has_role('finance_manager') or public.has_role('support_staff'));

create policy "order_items: via order" on public.order_items for select
  using (exists (
    select 1 from public.orders o where o.id = order_id
       and (o.user_id = auth.uid() or public.has_role('finance_manager') or public.has_role('support_staff'))
  ));

create policy "payment_transactions: via order" on public.payment_transactions for select
  using (exists (
    select 1 from public.orders o where o.id = order_id
       and (o.user_id = auth.uid() or public.has_role('finance_manager') or public.has_role('support_staff'))
  ));

create policy "payment_events: finance" on public.payment_events for select
  using (public.has_role('finance_manager'));

create policy "refunds: via order" on public.refunds for select
  using (exists (
    select 1 from public.orders o where o.id = order_id
       and (o.user_id = auth.uid() or public.has_role('finance_manager') or public.has_role('support_staff'))
  ));

create policy "receipts: via order" on public.receipts for select
  using (exists (
    select 1 from public.orders o where o.id = order_id
       and (o.user_id = auth.uid() or public.has_role('finance_manager') or public.has_role('support_staff'))
  ));

create policy "reconciliation: finance" on public.reconciliation_records for select
  using (public.has_role('finance_manager'));

create policy "subscriptions: own or staff" on public.subscriptions for select
  using (user_id = auth.uid() or public.has_role('finance_manager') or public.has_role('support_staff'));

create policy "entitlements: own or staff" on public.entitlements for select
  using (user_id = auth.uid() or public.has_role('finance_manager') or public.has_role('support_staff'));

-- Learners may submit bank-transfer details for their own open order; review
-- (approve/reject + entitlement grant) happens in a server function.
create policy "bank submissions: own or finance read" on public.bank_payment_submissions for select
  using (user_id = auth.uid() or public.has_role('finance_manager'));
create policy "bank submissions: submit for own open order" on public.bank_payment_submissions for insert
  with check (
    user_id = auth.uid()
    and status = 'submitted' and reviewed_by is null and reviewed_at is null
    and exists (
      select 1 from public.orders o
       where o.id = order_id and o.user_id = auth.uid() and o.status in ('created', 'pending')
    )
    and (proof_path is null or proof_path like auth.uid()::text || '/%')
  );

-- ---------------------------------------------------------------------------
-- Private storage for bank-transfer proofs: <user_id>/<order_id>/<file>
-- ---------------------------------------------------------------------------

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('payment-proofs', 'payment-proofs', false, 5242880, array['image/jpeg', 'image/png', 'image/webp', 'application/pdf'])
on conflict (id) do nothing;

create policy "payment-proofs: owner upload" on storage.objects for insert to authenticated
  with check (bucket_id = 'payment-proofs' and (storage.foldername(name))[1] = auth.uid()::text);
create policy "payment-proofs: owner or finance read" on storage.objects for select to authenticated
  using (bucket_id = 'payment-proofs'
         and ((storage.foldername(name))[1] = auth.uid()::text or public.has_role('finance_manager')));
