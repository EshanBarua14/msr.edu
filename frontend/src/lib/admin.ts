import type { PublishStatus } from './content'
import type { LessonKind } from './learning'

/** Shapes from backend/src/MsrEdu.Api/Endpoints/AdminContentEndpoints.cs. */

export interface Category { id: string; slug: string; name: string; description: string | null; position: number }

export interface CourseRow {
  id: string; slug: string; title: string; status: PublishStatus; isFree: boolean; category: string | null
  lessons: number; quizzes: number; pendingReview: number; updatedAt: string
}

export interface CourseFields {
  slug: string; title: string; summary: string | null; description: string | null; coverUrl: string | null
  categoryId: string | null; status: PublishStatus; isFree: boolean; position: number
}

export interface ModuleDto { id: string; title: string; position: number }
export interface LessonRow {
  id: string; moduleId: string | null; slug: string; title: string; kind: LessonKind; durationSeconds: number | null
  isPreview: boolean; status: PublishStatus; position: number
}
export interface QuizRow { id: string; title: string; lessonId: string | null; status: PublishStatus; isPreview: boolean; questions: number; pendingReview: number }

export interface CourseEditor extends CourseFields {
  id: string; modules: ModuleDto[]; lessons: LessonRow[]; quizzes: QuizRow[]
}

export interface LessonFields {
  moduleId: string | null; slug: string; title: string; summary: string | null; kind: LessonKind
  durationSeconds: number | null; isPreview: boolean; status: PublishStatus; body: string | null; videoRef: string | null
}
export interface LessonEditor extends LessonFields { id: string; courseId: string; position: number }

export interface QuizFields {
  title: string; description: string | null; lessonId: string | null; passScorePct: number
  timeLimitSeconds: number | null; maxAttempts: number | null; isPreview: boolean; status: PublishStatus
}
export interface OptionDto { id: string | null; label: string; isCorrect: boolean }
export interface QuestionDto {
  id: string; prompt: string; explanation: string | null; marks: number; position: number; aiGenerated: boolean
  reviewStatus: 'draft' | 'approved' | 'rejected'; reviewedAt: string | null; options: OptionDto[]
}
export interface QuizEditor extends QuizFields {
  id: string; courseId: string; courseTitle: string; attempts: number; questions: QuestionDto[]
}
export interface QuizListRow { id: string; title: string; courseId: string; courseTitle: string; status: PublishStatus; questions: number; pendingReview: number }

/** Problem details from the API carry a human-readable `detail`. */
export function problemText(e: unknown, fallback = 'Something went wrong.') {
  const body = (e as { body?: { detail?: string } })?.body
  return body?.detail ?? fallback
}

export interface DriveStatus { configured: boolean; accountEmail: string | null }
export interface AdminResource {
  id: string; name: string; mimeType: string | null; sizeBytes: number | null; lessonId: string | null
  isPreview: boolean; status: PublishStatus; syncedAt: string | null; driveModifiedAt: string | null
}
export interface CourseResources { driveFolderId: string | null; driveSyncedAt: string | null; resources: AdminResource[] }
export interface SyncResult { added: number; updated: number; archived: number; skipped: number }
