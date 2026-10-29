import { motion, useReducedMotion } from 'motion/react'

const blobs = [
  { className: 'bg-brand-500/35 size-[32rem] -left-32 -top-32', x: [0, 60, -20, 0], y: [0, 40, 80, 0] },
  { className: 'bg-accent-500/30 size-[28rem] -right-24 top-1/4', x: [0, -50, 20, 0], y: [0, 60, -30, 0] },
  { className: 'bg-fuchsia-500/20 size-[24rem] left-1/3 -bottom-32', x: [0, 40, -40, 0], y: [0, -50, 10, 0] },
]

/** Soft drifting gradient blobs. Static when the user prefers reduced motion. */
export function AnimatedBackground() {
  const reduce = useReducedMotion()
  return (
    <div aria-hidden className="pointer-events-none absolute inset-0 overflow-hidden">
      {blobs.map((b, i) => (
        <motion.div
          key={i}
          className={`absolute rounded-full blur-3xl ${b.className}`}
          animate={reduce ? undefined : { x: b.x, y: b.y }}
          transition={{ duration: 18 + i * 4, repeat: Infinity, ease: 'easeInOut' }}
        />
      ))}
      <div className="absolute inset-0 bg-[radial-gradient(circle_at_1px_1px,var(--color-border)_1px,transparent_0)] [background-size:28px_28px] opacity-60" />
    </div>
  )
}
