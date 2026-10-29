/** Shapes returned by the learning API (backend/src/MsrEdu.Api/Endpoints). */

export interface CourseSummary {
  id: string
  slug: string
  title: string
  summary: string | null
  coverUrl: string | null
  isFree: boolean
  category: string | null
}

export type LessonKind = 'video' | 'document' | 'text' | 'quiz'

export interface LessonOutline {
  id: string
  slug: string
  title: string
  kind: LessonKind
  durationSeconds: number | null
  isPreview: boolean
  locked: boolean
}

export interface ModuleOutline {
  id: string | null
  title: string
  lessons: LessonOutline[]
}

export interface QuizOutline {
  id: string
  title: string
  lessonId: string | null
  questionCount: number
  isPreview: boolean
  locked: boolean
}

export interface CourseDetail {
  course: CourseSummary
  description: string | null
  hasAccess: boolean
  modules: ModuleOutline[]
  quizzes: QuizOutline[]
  completedLessonIds: string[]
}

export interface LessonContent {
  lessonId: string
  title: string
  body: string | null
  videoRef: string | null
}

export interface CourseProgress {
  courseId: string
  slug: string
  title: string
  coverUrl: string | null
  totalLessons: number
  completedLessons: number
  lastLesson: { id: string; title: string } | null
  lastActivityAt: string
  hasAccess: boolean
}

export interface AttemptSummary {
  id: string
  quizId: string
  quizTitle: string
  courseSlug: string
  submittedAt: string
  score: number
  maxScore: number
  passed: boolean
}

export interface MyLearning {
  courses: CourseProgress[]
  recentAttempts: AttemptSummary[]
}

export interface QuizDetail {
  id: string
  title: string
  description: string | null
  courseSlug: string
  courseTitle: string
  passScorePct: number
  timeLimitSeconds: number | null
  maxAttempts: number | null
  attemptsUsed: number
  questions: { id: string; prompt: string; marks: number; options: { id: string; label: string }[] }[]
}

export interface StartedAttempt {
  attemptId: string
  startedAt: string
  deadline: string | null
}

export interface AttemptResult {
  id: string
  quizId: string
  quizTitle: string
  courseSlug: string
  submittedAt: string
  score: number
  maxScore: number
  scorePct: number
  passed: boolean
  answers: {
    questionId: string
    prompt: string
    chosenOptionId: string | null
    correctOptionIds: string[]
    isCorrect: boolean
    explanation: string | null
  }[]
}

export const lessonPath = (slug: string, lessonId: string) => `/courses/${slug}/lessons/${lessonId}`
export const quizPath = (slug: string, quizId: string) => `/courses/${slug}/quizzes/${quizId}`

export const percent = (done: number, total: number) => (total > 0 ? Math.round((done / total) * 100) : 0)

export function formatDuration(seconds: number | null) {
  if (!seconds) return null
  const m = Math.round(seconds / 60)
  return m < 60 ? `${m} min` : `${Math.floor(m / 60)} h ${m % 60} min`
}

/** Lessons in syllabus order, flattened across modules. */
export const flatLessons = (d: CourseDetail) => d.modules.flatMap((m) => m.lessons)

/**
 * Turns a stored video reference into something the player can show.
 * YouTube and Google Drive links become embeds; anything else is played as a file URL.
 */
export function videoSource(ref: string): { kind: 'embed' | 'file'; src: string } | null {
  let url: URL
  try {
    url = new URL(ref)
  } catch {
    return null
  }
  if (url.protocol !== 'https:') return null
  const host = url.hostname.replace(/^www\./, '')
  if (host === 'youtu.be') return { kind: 'embed', src: `https://www.youtube-nocookie.com/embed/${url.pathname.slice(1)}` }
  if (host === 'youtube.com' || host === 'm.youtube.com') {
    const id = url.searchParams.get('v') ?? url.pathname.match(/^\/(?:embed|shorts)\/([\w-]+)/)?.[1]
    return id ? { kind: 'embed', src: `https://www.youtube-nocookie.com/embed/${id}` } : null
  }
  if (host === 'drive.google.com') {
    const id = url.pathname.match(/\/file\/d\/([\w-]+)/)?.[1] ?? url.searchParams.get('id')
    return id ? { kind: 'embed', src: `https://drive.google.com/file/d/${id}/preview` } : null
  }
  return { kind: 'file', src: url.toString() }
}

export interface LessonResource { id: string; name: string; mimeType: string | null; sizeBytes: number | null }

export function formatBytes(n: number | null) {
  if (n === null) return null
  const units = ['B', 'KB', 'MB', 'GB']
  let i = 0
  while (n >= 1024 && i < units.length - 1) {
    n /= 1024
    i++
  }
  return `${n < 10 && i > 0 ? n.toFixed(1) : Math.round(n)} ${units[i]}`
}
