import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { motion } from 'motion/react'
import { ArrowLeft, Clock, ListChecks } from 'lucide-react'
import { AccessPrompt } from '@/components/learn/AccessPrompt'
import { AttemptReview } from '@/components/learn/AttemptReview'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { FullPageSpinner, Spinner } from '@/components/ui/Spinner'
import { api, ApiError } from '@/lib/api'
import type { AttemptResult, QuizDetail, StartedAttempt } from '@/lib/learning'
import { useApi } from '@/lib/useApi'

function useCountdown(deadline: string | null) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (!deadline) return
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [deadline])
  return deadline ? Math.max(0, Math.floor((Date.parse(deadline) - now) / 1000)) : null
}

export default function QuizPage() {
  const { slug = '', quizId = '' } = useParams()
  const [state, reload] = useApi<QuizDetail>(`/api/quizzes/${encodeURIComponent(quizId)}`)
  const [attempt, setAttempt] = useState<StartedAttempt | null>(null)
  const [answers, setAnswers] = useState<Record<string, string>>({})
  const [result, setResult] = useState<AttemptResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const left = useCountdown(attempt?.deadline ?? null)

  async function start() {
    setBusy(true)
    setError(null)
    try {
      setAttempt(await api<StartedAttempt>(`/api/quizzes/${quizId}/attempts`, { method: 'POST' }))
      setAnswers({})
      setResult(null)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not start the quiz.')
    } finally {
      setBusy(false)
    }
  }

  async function submit() {
    if (!attempt) return
    setBusy(true)
    setError(null)
    try {
      const body = { answers: Object.entries(answers).map(([questionId, optionId]) => ({ questionId, optionId })) }
      setResult(await api<AttemptResult>(`/api/quiz-attempts/${attempt.attemptId}/submit`, { json: body }))
      setAttempt(null)
      reload()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not submit. Your answers are still here, try again.')
    } finally {
      setBusy(false)
    }
  }

  // Submit what we have when time runs out; the server also enforces the deadline.
  useEffect(() => {
    if (left === 0 && attempt && !busy) void submit()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [left])

  if (state.status === 'loading' && !state.data) return <FullPageSpinner />
  if (state.status === 'error') return <AccessPrompt status={state.error.status} courseSlug={slug} />

  const quiz = state.data!
  const attemptsLeft = quiz.maxAttempts === null ? null : Math.max(0, quiz.maxAttempts - quiz.attemptsUsed)
  const answered = Object.keys(answers).length

  return (
    <div className="mx-auto max-w-3xl px-4 py-8 sm:px-6">
      <Link to={`/courses/${quiz.courseSlug}`} className="inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
        <ArrowLeft className="size-4" /> {quiz.courseTitle}
      </Link>
      <h1 className="mt-4 text-2xl font-bold tracking-tight sm:text-3xl">{quiz.title}</h1>
      {quiz.description && <p className="mt-2 text-muted">{quiz.description}</p>}
      {error && <p role="alert" className="mt-4 text-sm text-danger">{error}</p>}

      {result && (
        <motion.div initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} className="mt-6">
          <AttemptReview result={result} quiz={quiz} />
          {attemptsLeft !== 0 && (
            <Button variant="secondary" className="mt-6" onClick={start} disabled={busy}>
              Try again
            </Button>
          )}
        </motion.div>
      )}

      {!result && !attempt && (
        <Card className="mt-6">
          <ul className="grid gap-2 text-sm sm:grid-cols-2">
            <li className="flex items-center gap-2"><ListChecks className="size-4 text-muted" /> {quiz.questions.length} questions</li>
            <li className="flex items-center gap-2"><ListChecks className="size-4 text-muted" /> Pass mark {quiz.passScorePct}%</li>
            {quiz.timeLimitSeconds && (
              <li className="flex items-center gap-2"><Clock className="size-4 text-muted" /> {Math.round(quiz.timeLimitSeconds / 60)} minutes</li>
            )}
            {attemptsLeft !== null && <li className="flex items-center gap-2">{attemptsLeft} attempts left</li>}
          </ul>
          <Button className="mt-6" onClick={start} disabled={busy || attemptsLeft === 0 || quiz.questions.length === 0}>
            {busy && <Spinner className="size-4" />}
            {quiz.attemptsUsed > 0 ? 'Start new attempt' : 'Start quiz'}
          </Button>
        </Card>
      )}

      {attempt && (
        <div className="mt-6 flex flex-col gap-4">
          {left !== null && (
            <div className="sticky top-20 z-10 flex justify-end">
              <span className={`rounded-full px-3 py-1 text-sm font-semibold tabular-nums shadow ${left < 60 ? 'bg-danger text-white' : 'bg-surface'}`}>
                <Clock className="mr-1 inline size-4" />
                {Math.floor(left / 60)}:{String(left % 60).padStart(2, '0')}
              </span>
            </div>
          )}
          {quiz.questions.map((q, i) => (
            <Card key={q.id}>
              <fieldset>
                <legend className="font-medium">
                  {i + 1}. {q.prompt}
                </legend>
                <div className="mt-3 flex flex-col gap-2">
                  {q.options.map((o) => (
                    <label
                      key={o.id}
                      className={`flex cursor-pointer items-center gap-3 rounded-xl border px-4 py-3 text-sm transition-colors ${
                        answers[q.id] === o.id ? 'border-brand-500 bg-brand-500/8' : 'border-border hover:bg-surface-2'
                      }`}
                    >
                      <input
                        type="radio"
                        name={q.id}
                        value={o.id}
                        checked={answers[q.id] === o.id}
                        onChange={() => setAnswers((a) => ({ ...a, [q.id]: o.id }))}
                        className="accent-brand-600"
                      />
                      {o.label}
                    </label>
                  ))}
                </div>
              </fieldset>
            </Card>
          ))}
          <div className="flex items-center justify-between gap-3">
            <span className="text-sm text-muted">
              {answered} of {quiz.questions.length} answered
            </span>
            <Button onClick={submit} disabled={busy}>
              {busy && <Spinner className="size-4" />}
              Submit answers
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
