import { useState } from 'react'
import { CheckCircle2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select, Toggle } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import { formatDateTime } from '@/lib/billing'
import { type SettingKind, settingForms, type SettingView, type SetupStatus } from '@/lib/ops'
import { useApi } from '@/lib/useApi'

function ValueInput({ id, kind, value, onChange, currencies }: {
  id: string; kind: SettingKind; value: unknown; onChange: (v: unknown) => void; currencies: string[]
}) {
  switch (kind.type) {
    case 'toggle':
      return <Toggle checked={value === true} onChange={onChange} label={value === true ? 'On' : 'Off'} />
    case 'choice':
      return <Select id={id} value={String(value)} onChange={(e) => onChange(e.target.value)}>{kind.options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</Select>
    case 'currency':
      return <Select id={id} value={String(value)} onChange={(e) => onChange(e.target.value)}>{currencies.map((c) => <option key={c}>{c}</option>)}</Select>
    case 'currencies':
      return <Input id={id} value={Array.isArray(value) ? value.join(', ') : ''} onChange={(e) => onChange(e.target.value.split(',').map((c) => c.trim().toUpperCase()).filter(Boolean))} />
    case 'number':
      return (
        <div className="flex items-center gap-2">
          <Input id={id} type="number" className="w-32" min={kind.min} max={kind.max} value={value == null ? '' : String(value)}
            onChange={(e) => onChange(e.target.value === '' ? (kind.nullable ? null : '') : Number(e.target.value))} />
          <span className="text-sm text-muted">{kind.unit}</span>
        </div>
      )
  }
}

function SettingRow({ s, currencies, onSaved }: { s: SettingView; currencies: string[]; onSaved: () => void }) {
  const form = settingForms[s.key]
  const [value, setValue] = useState<unknown>(s.value)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const dirty = JSON.stringify(value) !== JSON.stringify(s.value)
  const id = `setting-${s.key.replaceAll('.', '-')}`

  async function save() {
    setBusy(true)
    setError(null)
    try {
      await api(`/api/admin/settings/${s.key}`, { method: 'PUT', json: { value } })
      onSaved()
    } catch (e) {
      setError(problemText(e))
    }
    setBusy(false)
  }

  return (
    <li className="grid gap-3 border-b border-border px-5 py-4 last:border-0 md:grid-cols-[1fr_20rem_auto] md:items-center">
      <div>
        <label htmlFor={id} className="text-sm font-medium">{form.label}</label>
        <p className="text-xs text-muted">{s.description} · {s.updatedBy ? `changed by ${s.updatedBy} ${formatDateTime(s.updatedAt)}` : 'default value'}</p>
        {error && <p role="alert" className="mt-1 text-sm text-danger">{error}</p>}
      </div>
      <ValueInput id={id} kind={form.kind} value={value} onChange={setValue} currencies={currencies} />
      <div className="flex gap-2">
        <Button size="sm" onClick={save} disabled={!dirty || busy}>{busy && <Spinner className="size-4" />}Save</Button>
        {dirty && <Button size="sm" variant="ghost" onClick={() => { setValue(s.value); setError(null) }}>Undo</Button>}
      </div>
    </li>
  )
}

export default function SettingsPage() {
  const [state, reload] = useApi<SettingView[]>('/api/admin/settings')
  const [setup, reloadSetup] = useApi<SetupStatus>('/api/admin/setup')
  const [confirming, setConfirming] = useState(false)
  const editable = (state.data ?? []).filter((s) => settingForms[s.key])
  const currencies = (state.data?.find((s) => s.key === 'currency.allowed')?.value as string[] | undefined) ?? ['BDT']

  async function confirm() {
    setConfirming(true)
    await api('/api/admin/settings/confirm-defaults', { method: 'POST' })
    setConfirming(false)
    reloadSetup()
  }

  return (
    <>
      <PageHeader title="App settings" description="Business rules the app follows. Changes apply immediately and are recorded in the audit log." />
      {setup.status === 'ok' && (
        setup.data.defaultsConfirmed ? (
          <p className="mb-4 flex items-center gap-2 text-sm text-success"><CheckCircle2 className="size-4" />You have confirmed these settings.</p>
        ) : (
          <Card className="mb-5 border-warning/40 bg-warning/5">
            <h2 className="font-semibold">These are placeholder defaults</h2>
            <p className="mt-1 text-sm text-muted">Review each value below, change what's wrong, then confirm. Until then the admin overview keeps this on its setup checklist.</p>
            <Button className="mt-3" size="sm" onClick={confirm} disabled={confirming}>{confirming && <Spinner className="size-4" />}Confirm settings</Button>
          </Card>
        )
      )}
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load settings.</p>}
      {editable.length > 0 && (
        <Card className="p-0"><ul>{editable.map((s) => <SettingRow key={`${s.key}:${s.updatedAt}`} s={s} currencies={currencies} onSaved={reload} />)}</ul></Card>
      )}
    </>
  )
}
