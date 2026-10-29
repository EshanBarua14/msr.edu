import { Link } from 'react-router-dom'
import { CheckCircle2, CircleAlert } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import type { DriveStatus } from '@/lib/admin'
import { useApi } from '@/lib/useApi'

export default function DrivePage() {
  const [state] = useApi<DriveStatus>('/api/admin/drive/status')
  return (
    <>
      <PageHeader title="Google Drive" description="Course files are synced from Drive folders and served through MSR.edu, never as Drive links." />
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.data && (
        <Card className="max-w-2xl">
          {state.data.configured ? (
            <div className="flex gap-3">
              <CheckCircle2 className="size-5 shrink-0 text-success" />
              <div className="text-sm">
                <p className="font-semibold">Connected</p>
                <p className="mt-1 text-muted">Share each course folder with this service account as a <b>Viewer</b>:</p>
                <p className="mt-2 rounded-lg bg-surface-2 px-3 py-2 font-mono text-xs select-all">{state.data.accountEmail}</p>
                <p className="mt-3 text-muted">Then open a course, go to <Link to="/admin/courses" className="text-brand-600 underline">Courses</Link> → Resources, paste the folder link and sync.</p>
              </div>
            </div>
          ) : (
            <div className="flex gap-3">
              <CircleAlert className="size-5 shrink-0 text-warning" />
              <div className="text-sm">
                <p className="font-semibold">Not connected</p>
                <ol className="mt-2 list-decimal space-y-1 pl-5 text-muted">
                  <li>In Google Cloud Console, enable the Google Drive API.</li>
                  <li>Create a service account and download a JSON key.</li>
                  <li>On the API server, set the key as the secret <span className="font-mono">GoogleDrive:ServiceAccountJson</span> (see README) and restart.</li>
                </ol>
              </div>
            </div>
          )}
        </Card>
      )}
    </>
  )
}
