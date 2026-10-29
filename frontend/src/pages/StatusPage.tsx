import { Link } from 'react-router-dom'
import { motion } from 'motion/react'
import { buttonClass } from '@/components/ui/buttonClass'

export function StatusPage({ code, title, body }: { code: string; title: string; body: string }) {
  return (
    <div className="grid min-h-[70dvh] place-items-center px-4 text-center">
      <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }}>
        <p className="text-gradient text-7xl font-extrabold">{code}</p>
        <h1 className="mt-4 text-2xl font-bold">{title}</h1>
        <p className="mt-2 text-muted">{body}</p>
        <Link to="/" className={buttonClass('primary', 'md', 'mt-8')}>
          Back home
        </Link>
      </motion.div>
    </div>
  )
}

export const NotFound = () => <StatusPage code="404" title="Page not found" body="That page does not exist or has moved." />
export const Forbidden = () => (
  <StatusPage code="403" title="No access" body="Your account does not have permission to open this page." />
)
