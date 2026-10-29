import { Link } from 'react-router-dom'
import { motion, useReducedMotion, type Variants } from 'motion/react'
import { ArrowRight, BadgeCheck, BookOpenCheck, CreditCard, Eye, ListChecks, LineChart, ShieldCheck, Sparkles, Smartphone } from 'lucide-react'
import { AnimatedBackground } from '@/components/AnimatedBackground'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'

const features = [
  { icon: Eye, title: 'Free previews', body: 'Try introductory lessons, sample notes and quiz questions before you pay.' },
  { icon: BookOpenCheck, title: 'Buy a single course', body: 'Pay once for the course you need, with clear access terms.' },
  { icon: BadgeCheck, title: 'Or join as a member', body: 'Monthly and yearly memberships unlock every eligible course.' },
  { icon: ListChecks, title: 'Quizzes that teach', body: 'Timed quizzes with explanations, scores and attempt history.' },
  { icon: LineChart, title: 'Track your progress', body: 'Pick up where you left off and see how far you have come.' },
  { icon: Smartphone, title: 'Install on any device', body: 'Works in the browser and installs like an app on your phone.' },
]

const payments = ['bKash', 'Nagad', 'Visa / Mastercard', 'Bank transfer']

const container: Variants = { hidden: {}, show: { transition: { staggerChildren: 0.08 } } }
const item: Variants = { hidden: { opacity: 0, y: 16 }, show: { opacity: 1, y: 0, transition: { duration: 0.5, ease: 'easeOut' } } }

export default function Landing() {
  const reduce = useReducedMotion()
  const initial = reduce ? 'show' : 'hidden'
  return (
    <>
      <section className="relative isolate overflow-hidden">
        <AnimatedBackground />
        <motion.div
          variants={container}
          initial={initial}
          animate="show"
          className="relative mx-auto flex max-w-4xl flex-col items-center px-4 pt-20 pb-24 text-center sm:px-6 sm:pt-28 sm:pb-32"
        >
          <motion.span variants={item} className="inline-flex items-center gap-2 rounded-full border border-border bg-surface/70 px-4 py-1.5 text-sm font-medium text-muted backdrop-blur">
            <Sparkles className="size-4 text-brand-500" /> Learn at your own pace
          </motion.span>
          <motion.h1 variants={item} className="mt-6 text-4xl font-extrabold tracking-tight text-balance sm:text-6xl">
            Learning that <span className="text-gradient">moves with you</span>
          </motion.h1>
          <motion.p variants={item} className="mt-6 max-w-2xl text-lg text-pretty text-muted">
            Structured courses, practice quizzes and progress tracking in one place. Start with free previews, then buy a course or become a member.
          </motion.p>
          <motion.div variants={item} className="mt-10 flex flex-col gap-3 sm:flex-row">
            <Link to="/login" className={buttonClass('primary', 'lg')}>
              Get started free <ArrowRight className="size-4" />
            </Link>
            <Link to="/courses" className={buttonClass('secondary', 'lg')}>
              Browse courses
            </Link>
          </motion.div>
        </motion.div>
      </section>

      <section className="mx-auto max-w-6xl px-4 py-20 sm:px-6">
        <div className="mx-auto max-w-2xl text-center">
          <h2 className="text-3xl font-bold tracking-tight">Everything you need to learn well</h2>
          <p className="mt-3 text-muted">Pay only for what you use. Paid lessons stay locked until your payment is verified.</p>
        </div>
        <motion.div
          variants={container}
          initial={initial}
          whileInView="show"
          viewport={{ once: true, margin: '-80px' }}
          className="mt-12 grid gap-5 sm:grid-cols-2 lg:grid-cols-3"
        >
          {features.map((f) => (
            <motion.div key={f.title} variants={item}>
              <Card className="h-full transition-transform duration-300 hover:-translate-y-1">
                <span className="grid size-11 place-items-center rounded-2xl bg-gradient-to-br from-brand-500 to-accent-500 text-white">
                  <f.icon className="size-5" />
                </span>
                <h3 className="mt-4 font-semibold">{f.title}</h3>
                <p className="mt-1.5 text-sm text-muted">{f.body}</p>
              </Card>
            </motion.div>
          ))}
        </motion.div>
      </section>

      <section className="mx-auto max-w-6xl px-4 pb-24 sm:px-6">
        <Card className="relative overflow-hidden p-8 sm:p-12">
          <div className="grid items-center gap-8 lg:grid-cols-2">
            <div>
              <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">Pay the way you already do</h2>
              <p className="mt-3 text-muted">
                Checkout runs through a secure payment provider. MSR.edu never sees or stores your card details.
              </p>
              <ul className="mt-6 flex flex-wrap gap-2">
                {payments.map((p) => (
                  <li key={p} className="rounded-full border border-border bg-surface-2 px-4 py-1.5 text-sm font-medium">
                    {p}
                  </li>
                ))}
              </ul>
            </div>
            <div className="flex flex-col gap-3">
              {[
                { icon: ShieldCheck, text: 'Access unlocks only after the payment is verified' },
                { icon: CreditCard, text: 'Receipts and full transaction history in your account' },
                { icon: BadgeCheck, text: 'Clear refund policy and access terms before you pay' },
              ].map((r) => (
                <div key={r.text} className="flex items-center gap-3 rounded-2xl bg-surface-2 p-4 text-sm font-medium">
                  <r.icon className="size-5 shrink-0 text-success" /> {r.text}
                </div>
              ))}
            </div>
          </div>
        </Card>
      </section>
    </>
  )
}
