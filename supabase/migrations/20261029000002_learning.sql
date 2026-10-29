-- MSR.edu learning content: courses, modules, lessons, resources, progress, quizzes.
-- Lesson *metadata* is public for published courses; lesson *content* lives in
-- lesson_contents and is gated by can_view_lesson().

create type public.publish_status as enum ('draft', 'published', 'archived');
create type public.lesson_kind as enum ('video', 'document', 'text', 'quiz');

create table public.categories (
  id          uuid primary key default gen_random_uuid(),
  slug        text not null unique check (slug ~ '^[a-z0-9-]+$'),
  name        text not null,
  description text,
  position    int not null default 0,
  created_at  timestamptz not null default now()
);

create table public.courses (
  id            uuid primary key default gen_random_uuid(),
  slug          text not null unique check (slug ~ '^[a-z0-9-]+$'),
  title         text not null,
  summary       text,
  description   text,
  cover_url     text,
  category_id   uuid references public.categories (id) on delete set null,
  status        public.publish_status not null default 'draft',
  -- Entire course free for signed-in users (no purchase needed).
  is_free       boolean not null default false,
  position      int not null default 0,
  published_at  timestamptz,
  created_by    uuid references public.profiles (id),
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now()
);
create index courses_category_idx on public.courses (category_id);
create trigger courses_updated_at before update on public.courses
  for each row execute function public.set_updated_at();

create table public.modules (
  id          uuid primary key default gen_random_uuid(),
  course_id   uuid not null references public.courses (id) on delete cascade,
  title       text not null,
  position    int not null default 0,
  created_at  timestamptz not null default now()
);
create index modules_course_idx on public.modules (course_id, position);

create table public.lessons (
  id          uuid primary key default gen_random_uuid(),
  course_id   uuid not null references public.courses (id) on delete cascade,
  module_id   uuid references public.modules (id) on delete set null,
  slug        text not null check (slug ~ '^[a-z0-9-]+$'),
  title       text not null,
  summary     text,
  kind        public.lesson_kind not null default 'text',
  duration_seconds int check (duration_seconds >= 0),
  -- Free preview: viewable by anyone, including anonymous visitors.
  is_preview  boolean not null default false,
  status      public.publish_status not null default 'draft',
  position    int not null default 0,
  created_at  timestamptz not null default now(),
  updated_at  timestamptz not null default now(),
  unique (course_id, slug)
);
create index lessons_module_idx on public.lessons (module_id, position);
create trigger lessons_updated_at before update on public.lessons
  for each row execute function public.set_updated_at();

-- Protected lesson body. One row per lesson.
create table public.lesson_contents (
  lesson_id   uuid primary key references public.lessons (id) on delete cascade,
  body        text,
  video_ref   text,
  updated_at  timestamptz not null default now()
);
create trigger lesson_contents_updated_at before update on public.lesson_contents
  for each row execute function public.set_updated_at();

-- Drive-backed resources. Files are streamed through a server function after
-- an access check; drive_file_id must never be enough to fetch content.
create table public.resources (
  id             uuid primary key default gen_random_uuid(),
  course_id      uuid not null references public.courses (id) on delete cascade,
  lesson_id      uuid references public.lessons (id) on delete set null,
  name           text not null,
  mime_type      text,
  size_bytes     bigint check (size_bytes >= 0),
  is_preview     boolean not null default false,
  -- Sync and publish are separate actions.
  synced_at      timestamptz,
  status         public.publish_status not null default 'draft',
  created_at     timestamptz not null default now()
);
create index resources_lesson_idx on public.resources (lesson_id);

-- Drive linkage is staff/server-only so file IDs never reach learners.
create table public.resource_sources (
  resource_id        uuid primary key references public.resources (id) on delete cascade,
  drive_file_id      text not null unique,
  drive_modified_at  timestamptz,
  checksum           text
);

create table public.enrollments (
  user_id     uuid not null references public.profiles (id) on delete cascade,
  course_id   uuid not null references public.courses (id) on delete cascade,
  started_at  timestamptz not null default now(),
  last_lesson_id uuid references public.lessons (id) on delete set null,
  last_activity_at timestamptz not null default now(),
  primary key (user_id, course_id)
);

create table public.lesson_progress (
  user_id      uuid not null references public.profiles (id) on delete cascade,
  lesson_id    uuid not null references public.lessons (id) on delete cascade,
  progress_pct smallint not null default 0 check (progress_pct between 0 and 100),
  completed_at timestamptz,
  updated_at   timestamptz not null default now(),
  primary key (user_id, lesson_id)
);
create trigger lesson_progress_updated_at before update on public.lesson_progress
  for each row execute function public.set_updated_at();

-- ---------------------------------------------------------------------------
-- Quizzes. Grading happens server-side. Correct answers and explanations live
-- in question_keys / question_correct_options, which learners cannot read.
-- ---------------------------------------------------------------------------

create table public.quizzes (
  id                 uuid primary key default gen_random_uuid(),
  course_id          uuid not null references public.courses (id) on delete cascade,
  lesson_id          uuid references public.lessons (id) on delete set null,
  title              text not null,
  description        text,
  pass_score_pct     smallint not null default 60 check (pass_score_pct between 0 and 100),
  time_limit_seconds int check (time_limit_seconds > 0),
  max_attempts       int check (max_attempts > 0),
  is_preview         boolean not null default false,
  status             public.publish_status not null default 'draft',
  created_at         timestamptz not null default now(),
  updated_at         timestamptz not null default now()
);
create index quizzes_course_idx on public.quizzes (course_id);
create trigger quizzes_updated_at before update on public.quizzes
  for each row execute function public.set_updated_at();

create type public.question_review_status as enum ('draft', 'approved', 'rejected');

create table public.questions (
  id             uuid primary key default gen_random_uuid(),
  quiz_id        uuid not null references public.quizzes (id) on delete cascade,
  prompt         text not null,
  marks          numeric(6,2) not null default 1 check (marks > 0),
  position       int not null default 0,
  -- AI-generated questions start as draft and must be approved before use.
  ai_generated   boolean not null default false,
  review_status  public.question_review_status not null default 'draft',
  reviewed_by    uuid references public.profiles (id),
  reviewed_at    timestamptz,
  created_at     timestamptz not null default now()
);
create index questions_quiz_idx on public.questions (quiz_id, position);

create table public.question_options (
  id           uuid primary key default gen_random_uuid(),
  question_id  uuid not null references public.questions (id) on delete cascade,
  label        text not null,
  position     int not null default 0,
  unique (id, question_id)
);
create index question_options_question_idx on public.question_options (question_id, position);

create table public.question_keys (
  question_id  uuid primary key references public.questions (id) on delete cascade,
  explanation  text
);

create table public.question_correct_options (
  option_id    uuid primary key,
  question_id  uuid not null references public.questions (id) on delete cascade,
  foreign key (option_id, question_id) references public.question_options (id, question_id) on delete cascade
);

create table public.quiz_attempts (
  id            uuid primary key default gen_random_uuid(),
  quiz_id       uuid not null references public.quizzes (id) on delete cascade,
  user_id       uuid not null references public.profiles (id) on delete cascade,
  started_at    timestamptz not null default now(),
  submitted_at  timestamptz,
  score         numeric(8,2),
  max_score     numeric(8,2),
  passed        boolean
);
create index quiz_attempts_user_idx on public.quiz_attempts (user_id, quiz_id);

create table public.quiz_answers (
  attempt_id   uuid not null references public.quiz_attempts (id) on delete cascade,
  question_id  uuid not null references public.questions (id) on delete cascade,
  option_id    uuid references public.question_options (id) on delete set null,
  is_correct   boolean,
  primary key (attempt_id, question_id)
);
