import { lazy, Suspense, type ReactNode } from 'react'
import { createBrowserRouter, Navigate } from 'react-router-dom'
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

const s = (node: ReactNode) => <Suspense fallback={<FullPageSpinner />}>{node}</Suspense>

const Login = lazy(() => import('@/pages/Login'))
const AuthCallback = lazy(() => import('@/pages/AuthCallback'))
const PublicCourses = lazy(() => import('@/pages/PublicCourses'))
const Pricing = lazy(() => import('@/pages/Pricing'))
const CourseDetail = lazy(() => import('@/pages/learn/CourseDetail'))
const LessonPlayer = lazy(() => import('@/pages/learn/LessonPlayer'))
const QuizPage = lazy(() => import('@/pages/learn/QuizPage'))
const Dashboard = lazy(() => import('@/pages/app/Dashboard'))
const MyCourses = lazy(() => import('@/pages/app/MyCourses'))
const QuizResults = lazy(() => import('@/pages/app/QuizResults'))
const AttemptPage = lazy(() => import('@/pages/app/AttemptPage'))
const AdminOverview = lazy(() => import('@/pages/admin/Overview'))
const CourseList = lazy(() => import('@/pages/admin/content/CourseList'))
const CourseEditorPage = lazy(() => import('@/pages/admin/content/CourseEditorPage'))
const LessonEditorPage = lazy(() => import('@/pages/admin/content/LessonEditorPage'))
const QuizList = lazy(() => import('@/pages/admin/content/QuizList'))
const QuizEditorPage = lazy(() => import('@/pages/admin/content/QuizEditorPage'))
const DrivePage = lazy(() => import('@/pages/admin/content/DrivePage'))
const SandboxPay = lazy(() => import('@/pages/checkout/SandboxPay'))
const CheckoutReturn = lazy(() => import('@/pages/checkout/CheckoutReturn'))
const Membership = lazy(() => import('@/pages/app/Membership'))
const Transactions = lazy(() => import('@/pages/app/Transactions'))
const ReceiptPage = lazy(() => import('@/pages/app/ReceiptPage'))
const PricingPage = lazy(() => import('@/pages/admin/finance/PricingPage'))
const PlansPage = lazy(() => import('@/pages/admin/finance/PlansPage'))
const OrdersPage = lazy(() => import('@/pages/admin/finance/OrdersPage'))
const OrderDetailPage = lazy(() => import('@/pages/admin/finance/OrderDetailPage'))
const EntitlementsPage = lazy(() => import('@/pages/admin/finance/EntitlementsPage'))

/** Admin sections that are built; the rest show a placeholder. Detail pages share their section's roles. */
const adminScreens: Record<string, { index: ReactNode; detail?: [string, ReactNode][] }> = {
  courses: { index: s(<CourseList />), detail: [[':id', s(<CourseEditorPage />)]] },
  modules: { index: <Navigate to="/admin/courses" replace />, detail: [['/admin/lessons/:id', s(<LessonEditorPage />)]] },
  quizzes: { index: s(<QuizList />), detail: [[':id', s(<QuizEditorPage />)]] },
  resources: { index: <Navigate to="/admin/courses" replace /> },
  drive: { index: s(<DrivePage />) },
  pricing: { index: s(<PricingPage />) },
  plans: { index: s(<PlansPage />) },
  orders: { index: s(<OrdersPage />), detail: [[':id', s(<OrderDetailPage />)]] },
  pending: { index: <Navigate to="/admin/orders?status=pending,processing" replace /> },
  refunds: { index: <Navigate to="/admin/orders?status=refund_pending,partially_refunded,refunded" replace /> },
  receipts: { index: <Navigate to="/admin/orders?status=successful,partially_refunded,refunded" replace /> },
  entitlements: { index: s(<EntitlementsPage />) },
}

export const router = createBrowserRouter([
  {
    element: <PublicLayout />,
    children: [
      { index: true, element: <Landing /> },
      { path: 'courses', element: s(<PublicCourses />) },
      { path: 'courses/:slug', element: s(<CourseDetail />) },
      // Free previews open without signing in; the API decides what each viewer may see.
      { path: 'courses/:slug/lessons/:lessonId', element: s(<LessonPlayer />) },
      { path: 'courses/:slug/quizzes/:quizId', element: <RequireAuth />, children: [{ index: true, element: s(<QuizPage />) }] },
      { path: 'pricing', element: s(<Pricing />) },
      {
        path: 'checkout',
        element: <RequireAuth />,
        children: [
          { path: 'sandbox/:txnId', element: s(<SandboxPay />) },
          { path: 'return/:reference', element: s(<CheckoutReturn />) },
        ],
      },
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
          { path: 'courses', element: s(<MyCourses />) },
          { path: 'quizzes', element: s(<QuizResults />) },
          { path: 'quizzes/:attemptId', element: s(<AttemptPage />) },
          { path: 'subscription', element: s(<Membership />) },
          { path: 'transactions', element: s(<Transactions />) },
          { path: 'receipts', element: s(<Transactions receiptsOnly />) },
          { path: 'receipts/:number', element: s(<ReceiptPage />) },
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
              ...adminPages.map((p) => {
                const section = p.to.replace('/admin/', '')
                const screen = adminScreens[section]
                return {
                  element: <RequireRole anyOf={p.roles} />,
                  children: [
                    { path: section, element: screen?.index ?? <Placeholder title={p.label} /> },
                    ...(screen?.detail ?? []).map(([path, element]) => ({
                      path: path.startsWith('/') ? path.replace('/admin/', '') : `${section}/${path}`,
                      element,
                    })),
                  ],
                }
              }),
            ],
          },
        ],
      },
    ],
  },
])
