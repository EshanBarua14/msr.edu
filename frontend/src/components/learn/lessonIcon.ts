import { FileText, ListChecks, PlayCircle, Type } from 'lucide-react'
import type { LessonKind } from '@/lib/learning'

export const lessonIcon: Record<LessonKind, typeof PlayCircle> = { video: PlayCircle, document: FileText, text: Type, quiz: ListChecks }
