import { CheckCircle2, XCircle } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { ProgressBar } from '@/components/ui/ProgressBar'
import type { AttemptResult, QuizDetail } from '@/lib/learning'

/** Graded result from the server. Option labels come from the quiz when we have it. */
export function AttemptReview({ result, quiz }: { result: AttemptResult; quiz?: QuizDetail }) {
  const labels = new Map(quiz?.questions.flatMap((q) => q.options.map((o) => [o.id, o.label] as const)))
  return (
    <div className="flex flex-col gap-4">
      <Card className={result.passed ? 'border-success/40' : 'border-danger/40'}>
        <div className="flex items-center gap-3">
          {result.passed ? <CheckCircle2 className="size-8 text-success" /> : <XCircle className="size-8 text-danger" />}
          <div className="flex-1">
            <p className="text-lg font-semibold">{result.passed ? 'Passed' : 'Not passed yet'}</p>
            <p className="text-sm text-muted tabular-nums">
              {result.score} / {result.maxScore} marks · {result.scorePct}%
            </p>
          </div>
        </div>
        <ProgressBar className="mt-4" value={result.scorePct} label="Score" />
      </Card>
      {result.answers.map((a, i) => (
        <Card key={a.questionId}>
          <div className="flex gap-3">
            {a.isCorrect ? <CheckCircle2 className="mt-0.5 size-5 shrink-0 text-success" /> : <XCircle className="mt-0.5 size-5 shrink-0 text-danger" />}
            <div className="flex-1">
              <p className="font-medium">
                {i + 1}. {a.prompt}
              </p>
              {labels.size > 0 && (
                <dl className="mt-2 grid gap-1 text-sm">
                  <div className="flex gap-2">
                    <dt className="text-muted">Your answer:</dt>
                    <dd>{a.chosenOptionId ? labels.get(a.chosenOptionId) : 'No answer'}</dd>
                  </div>
                  {!a.isCorrect && (
                    <div className="flex gap-2">
                      <dt className="text-muted">Correct:</dt>
                      <dd>{a.correctOptionIds.map((id) => labels.get(id)).join(', ')}</dd>
                    </div>
                  )}
                </dl>
              )}
              {a.explanation && <p className="mt-3 rounded-xl bg-surface-2 p-3 text-sm whitespace-pre-line">{a.explanation}</p>}
            </div>
          </div>
        </Card>
      ))}
    </div>
  )
}
