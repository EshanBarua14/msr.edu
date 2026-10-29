import { lazy, Suspense, type ReactNode } from 'react'
import { createBrowserRouter } from 'react-router-dom'
import { RequireAuth, RequireRole } from '@/auth/guards'
import { STAFF_ROLES } from '@/auth/roles'
import { FullPageSpinner } from '@/components/ui/Spinner'
import { AdminLayout } from '@/layouts/AdminLayout'
import { adminPages } from '@/layouts/adminNav'
import { LearnerLayout } from '@/layouts/LearnerLayout'
import { PublicLayout } from '@/layouts/PublicLayout'
import Landing from '@/pages/Landing'
import Placeholder from '@/pages/Placeholder'
import { Forbidden, NotFound } from '@/pages/StatusPage'

const Login = lazy(() => import('@/pages/Login'))
const AuthCallback = lazy(() => import('@/pages/AuthCallback'))
const PublicCourses = lazy(() => import('@/pages/PublicCourses'))
const Pricing = lazy(() => import('@/pages/Pricing'))
const Dashboard = lazy(() => import('@/pages/app/Dashboard'))
const AdminOverview = lazy(() => import('@/pages/admin/Overview'))

const s = (node: ReactNode) => <Suspense fallback={<FullPageSpinner />}>{node}</Suspense>

const learnerPlaceholders = [
  { path: 'courses', title: 'My courses', phase: 'Phase 4' },
  { path: 'quizzes', title: 'Quiz results', phase: 'Phase 4' },
  { path: 'subscription', title: 'Membership', phase: 'Phase 5' },
  { path: 'transactions', title: 'Transactions', phase: 'Phase 5' },
  { path: 'receipts', title: 'Receipts', phase: 'Phase 6' },
]

export const router = createBrowserRouter([
  {
    element: <PublicLayout />,
    children: [
      { index: true, element: <Landing /> },
      { path: 'courses', element: s(<PublicCourses />) },
      { path: 'pricing', element: s(<Pricing />) },
      ...['terms', 'privacy', 'refunds'].map((path) => ({
        path,
        element: (
          <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6">
            <Placeholder title={{ terms: 'Terms of use', privacy: 'Privacy policy', refunds: 'Refund policy' }[path]!} />
          </div>
        ),
      })),
      { path: 'forbidden', element: <Forbidden /> },
      { path: '*', element: <NotFound /> },
    ],
  },
  { path: 'login', element: s(<Login />) },
  { path: 'auth/callback', element: s(<AuthCallback />) },
  {
    element: <RequireAuth />,
    children: [
      {
        path: 'app',
        element: <LearnerLayout />,
        children: [
          { index: true, element: s(<Dashboard />) },
          ...learnerPlaceholders.map((p) => ({ path: p.path, element: <Placeholder title={p.title} phase={p.phase} /> })),
        ],
      },
      {
        element: <RequireRole anyOf={STAFF_ROLES} />,
        children: [
          {
            path: 'admin',
            element: <AdminLayout />,
            children: [
              { index: true, element: s(<AdminOverview />) },
              ...adminPages.map((p) => ({
                path: p.to.replace('/admin/', ''),
                element: <RequireRole anyOf={p.roles} />,
                children: [{ index: true, element: <Placeholder title={p.label} /> }],
              })),
            ],
          },
        ],
      },
    ],
  },
])
