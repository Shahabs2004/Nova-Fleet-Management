import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { api } from '../lib/api'
import type {
  ConnectionTestResult,
  DatabaseSettings,
  DirectorySettings,
  OperationsSettings,
  SettingsSaveResult,
  SmsConnectionSettings,
} from '../lib/types'

const SMS_MODES = ['Simulation', 'Wsdl', 'Api']
const LOG_LEVELS = ['Critical', 'Error', 'Warning', 'Information', 'Debug', 'Trace']
const PAPER_SIZES = ['A4', 'A5', 'Letter']

const EMPTY_DIRECTORY: DirectorySettings = {
  enabled: false,
  host: '',
  port: 636,
  useSsl: true,
  domain: '',
  baseDn: '',
  bindDn: '',
  timeoutSeconds: 15,
  searchFilter: '(&(objectClass=user)(sAMAccountName={0}))',
  hasBindPassword: false,
}

const EMPTY_SMS: SmsConnectionSettings = {
  enabled: false,
  mode: 'Simulation',
  endpoint: '',
  organization: '',
  username: '',
  senderNumber: '',
  timeoutSeconds: 20,
  retryMinutes: 5,
  maxAttempts: 5,
  hasPassword: false,
}

const EMPTY_OPERATIONS: OperationsSettings = { logLevel: 'Information', pdfFontPath: '', reportPaperSize: 'A4' }

const message = (error: unknown) => (error as Error)?.message ?? 'عملیات ناموفق بود.'

export function ConnectionsSettingsTab() {
  return (
    <div className="space-y-4">
      <DatabaseCard />
      <DirectoryCard />
      <SmsCard />
      <OperationsCard />
    </div>
  )
}

function Card({
  title,
  hint,
  children,
  onSave,
  saveLabel,
  onTest,
  testLabel,
  busy,
  testing,
  note,
  result,
  error,
}: {
  title: string
  hint?: string
  children: React.ReactNode
  onSave: () => void
  saveLabel: string
  onTest?: () => void
  testLabel?: string
  busy: boolean
  testing?: boolean
  note?: SettingsSaveResult | null
  result?: ConnectionTestResult | null
  error?: string | null
}) {
  return (
    <section className="card p-6">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h3 className="text-sm font-bold text-emerald-950">{title}</h3>
          {hint && <p className="mt-1 text-[11px] leading-5 text-slate-400">{hint}</p>}
        </div>
        <div className="flex gap-2">
          {onTest && (
            <button type="button" className="btn-ghost btn-sm" onClick={onTest} disabled={busy || testing}>
              {testing ? 'در حال آزمون…' : (testLabel ?? 'تست اتصال')}
            </button>
          )}
          <button type="button" className="btn-primary btn-sm" onClick={onSave} disabled={busy}>
            {busy ? 'در حال ذخیره…' : saveLabel}
          </button>
        </div>
      </div>

      {children}

      {note && (
        <p
          className={`mt-4 rounded-xl px-4 py-3 text-xs leading-6 ring-1 ${
            note.restartRequired ? 'bg-amber-50 text-amber-900 ring-amber-200' : 'bg-emerald-50 text-emerald-900 ring-emerald-100'
          }`}
        >
          {note.hint ?? (note.restartRequired ? 'این تغییر پس از راه‌اندازی مجدد سرویس اعمال می‌شود.' : 'تنظیمات ذخیره شد و بی‌درنگ اعمال می‌شود.')}
        </p>
      )}
      {result && <TestResult result={result} />}
      {error && <p className="mt-4 rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</p>}
    </section>
  )
}

function TestResult({ result }: { result: ConnectionTestResult }) {
  const tone = result.success ? 'bg-emerald-50 text-emerald-900 ring-emerald-100' : 'bg-rose-50 text-rose-800 ring-rose-100'
  return (
    <div className={`mt-4 rounded-xl px-4 py-3 text-xs leading-6 ring-1 ${tone}`}>
      <p className="font-bold">{result.message}</p>
      {result.detail && <p className="mt-1 opacity-90">{result.detail}</p>}
    </div>
  )
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="label">{label}</label>
      {children}
      {hint && <p className="mt-1 text-[11px] leading-5 text-slate-400">{hint}</p>}
    </div>
  )
}

function Text({
  label,
  value,
  onChange,
  hint,
  type = 'text',
}: {
  label: string
  value: string
  onChange: (value: string) => void
  hint?: string
  type?: string
}) {
  return (
    <Field label={label} hint={hint}>
      <input className="input" type={type} dir="ltr" value={value} onChange={(e) => onChange(e.target.value)} />
    </Field>
  )
}

function NumberField({ label, value, onChange, hint }: { label: string; value: number; onChange: (value: number) => void; hint?: string }) {
  return (
    <Field label={label} hint={hint}>
      <input
        className="input num"
        type="number"
        dir="ltr"
        value={Number.isFinite(value) ? value : 0}
        onChange={(e) => onChange(Number(e.target.value))}
      />
    </Field>
  )
}

function Choice({ label, value, options, onChange }: { label: string; value: string; options: string[]; onChange: (value: string) => void }) {
  return (
    <Field label={label}>
      <select className="input" value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    </Field>
  )
}

function Switch({ label, checked, onChange, hint }: { label: string; checked: boolean; onChange: (value: boolean) => void; hint?: string }) {
  return (
    <label className="flex cursor-pointer items-start gap-3 rounded-xl bg-slate-50 px-4 py-3 ring-1 ring-slate-200">
      <input type="checkbox" className="mt-1 h-4 w-4 accent-emerald-700" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>
        <span className="block text-sm font-semibold text-slate-700">{label}</span>
        {hint && <span className="mt-0.5 block text-[11px] leading-5 text-slate-500">{hint}</span>}
      </span>
    </label>
  )
}

function Busy({ text }: { text: string }) {
  return <p className="py-8 text-center text-slate-400">{text}</p>
}

function DatabaseCard() {
  const query = useQuery({
    queryKey: ['connections-database'],
    queryFn: () => api.get<DatabaseSettings>('/api/settings/connections/database'),
  })
  const [value, setValue] = useState('')
  const [note, setNote] = useState<SettingsSaveResult | null>(null)
  const [test, setTest] = useState<ConnectionTestResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (query.data?.connectionString) setValue(query.data.connectionString)
  }, [query.data])

  const save = useMutation({
    mutationFn: () =>
      api.put<SettingsSaveResult>('/api/settings/connections/database', {
        connectionString: value.trim(),
        isConfigured: query.data?.isConfigured ?? false,
        source: query.data?.source ?? '',
      }),
    onSuccess: (result) => {
      setNote(result)
      setError(null)
      void query.refetch()
    },
    onError: (e: unknown) => setError(message(e)),
  })

  const runTest = useMutation({
    mutationFn: () => api.post<ConnectionTestResult>('/api/settings/connections/database/test'),
    onSuccess: setTest,
    onError: (e: unknown) => setError(message(e)),
  })

  if (query.isLoading) return <Busy text="در حال خواندن اتصال پایگاه داده…" />

  return (
    <Card
      title="پایگاه داده"
      hint={`منبع فعال در حال استفاده: ${query.data?.source ?? '—'}`}
      onSave={() => save.mutate()}
      saveLabel="ذخیره رشته اتصال"
      onTest={() => runTest.mutate()}
      testing={runTest.isPending}
      busy={save.isPending}
      note={note}
      result={test}
      error={error}
    >
      <Field
        label="ConnectionString"
        hint="رمز در مقداری که اینجا نمایش داده می‌شود پاک شده است؛ برای ذخیره، رشته کامل را همراه با رمز بنویسید. پس از ذخیره، سرویس باید یک‌بار restart شود."
      >
        <textarea className="input min-h-24 font-mono text-xs" dir="ltr" value={value} onChange={(e) => setValue(e.target.value)} />
      </Field>
    </Card>
  )
}

function DirectoryCard() {
  const query = useQuery({
    queryKey: ['connections-directory'],
    queryFn: () => api.get<DirectorySettings>('/api/settings/connections/directory'),
  })
  const [draft, setDraft] = useState<DirectorySettings>(EMPTY_DIRECTORY)
  const [bindPassword, setBindPassword] = useState('')
  const [sample, setSample] = useState('')
  const [note, setNote] = useState<SettingsSaveResult | null>(null)
  const [test, setTest] = useState<ConnectionTestResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (query.data) setDraft(query.data)
  }, [query.data])

  const save = useMutation({
    mutationFn: () =>
      api.put<SettingsSaveResult>('/api/settings/connections/directory', {
        ...draft,
        bindPassword: bindPassword || null,
      }),
    onSuccess: (result) => {
      setNote(result)
      setBindPassword('')
      setError(null)
      void query.refetch()
    },
    onError: (e: unknown) => setError(message(e)),
  })

  const runTest = useMutation({
    mutationFn: () => {
      const trimmed = sample.trim()
      return api.post<ConnectionTestResult>(
        `/api/settings/connections/directory/test${trimmed ? `?sampleUsername=${encodeURIComponent(trimmed)}` : ''}`,
      )
    },
    onSuccess: setTest,
    onError: (e: unknown) => setError(message(e)),
  })

  if (query.isLoading) return <Busy text="در حال خواندن تنظیمات دامنه…" />

  const set = <K extends keyof DirectorySettings>(key: K) => (value: DirectorySettings[K]) => setDraft({ ...draft, [key]: value })

  return (
    <Card
      title="دامنه (Active Directory)"
      hint="ورود کاربران با حساب دامنه. رمز حساب Bind هرگز به این صفحه برنمی‌گردد."
      onSave={() => save.mutate()}
      saveLabel="ذخیره تنظیمات دامنه"
      onTest={() => runTest.mutate()}
      testing={runTest.isPending}
      busy={save.isPending}
      note={note}
      result={test}
      error={error}
    >
      <div className="space-y-4">
        <Switch label="ورود با حساب دامنه فعال باشد" checked={draft.enabled} onChange={set('enabled')} />
        <div className="grid gap-4 sm:grid-cols-3">
          <Text label="سرور دامنه" value={draft.host} onChange={set('host')} />
          <NumberField label="پورت" value={draft.port} onChange={set('port')} />
          <NumberField label="تایم‌اوت (ثانیه)" value={draft.timeoutSeconds} onChange={set('timeoutSeconds')} />
        </div>
        <Switch label="اتصال امن LDAPS" checked={draft.useSsl} onChange={set('useSsl')} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Text label="نام دامنه" value={draft.domain} onChange={set('domain')} />
          <Text label="BaseDn" value={draft.baseDn} onChange={set('baseDn')} />
          <Text label="حساب Bind" value={draft.bindDn} onChange={set('bindDn')} />
          <Text
            label="رمز حساب Bind"
            value={bindPassword}
            onChange={setBindPassword}
            type="password"
            hint={draft.hasBindPassword ? 'رمز ذخیره شده است؛ برای تغییر، مقدار جدید را وارد کنید.' : 'هنوز رمزی ذخیره نشده است.'}
          />
        </div>
        <Text label="فیلتر جست‌وجو" value={draft.searchFilter} onChange={set('searchFilter')} hint="باید شامل {0} باشد." />
        <Text
          label="نام کاربری نمونه برای تست"
          value={sample}
          onChange={setSample}
          hint="با خالی گذاشتن، جست‌وجوی نمونه روی کل BaseDn اجرا می‌شود."
        />
      </div>
    </Card>
  )
}

function SmsCard() {
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: ['connections-sms'],
    queryFn: () => api.get<SmsConnectionSettings>('/api/settings/connections/sms'),
  })
  const [draft, setDraft] = useState<SmsConnectionSettings>(EMPTY_SMS)
  const [password, setPassword] = useState('')
  const [mobile, setMobile] = useState('')
  const [note, setNote] = useState<SettingsSaveResult | null>(null)
  const [balance, setBalance] = useState<ConnectionTestResult | null>(null)
  const [sendNote, setSendNote] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (query.data) setDraft(query.data)
  }, [query.data])

  const save = useMutation({
    mutationFn: () => api.put<SettingsSaveResult>('/api/settings/connections/sms', { ...draft, password: password || null }),
    onSuccess: (result) => {
      setNote(result)
      setPassword('')
      setError(null)
      void query.refetch()
    },
    onError: (e: unknown) => setError(message(e)),
  })

  const runBalance = useMutation({
    mutationFn: () => api.post<ConnectionTestResult>('/api/settings/connections/sms/test'),
    onSuccess: setBalance,
    onError: (e: unknown) => setError(message(e)),
  })

  const sendTest = useMutation({
    mutationFn: () => api.post<{ id: string }>('/api/sms/test', { mobile: mobile.trim(), body: 'آزمون اتصال سامانه مدیریت ناوگان.' }),
    onSuccess: () => {
      setSendNote('پیامک آزمایشی در صف ثبت شد و پس از پردازش صف ارسال می‌شود.')
      setError(null)
      void queryClient.invalidateQueries({ queryKey: ['sms'] })
      void queryClient.invalidateQueries({ queryKey: ['sms-summary'] })
    },
    onError: (e: unknown) => setError(message(e)),
  })

  if (query.isLoading) return <Busy text="در حال خواندن تنظیمات پیامک…" />

  const set = <K extends keyof SmsConnectionSettings>(key: K) => (value: SmsConnectionSettings[K]) => setDraft({ ...draft, [key]: value })

  return (
    <Card
      title="سرویس پیامک"
      hint="سوییچ سراسری خاموش باشد، هیچ پیامکی به سرویس‌دهنده نمی‌رود و پیامک‌ها فقط در صف می‌مانند."
      onSave={() => save.mutate()}
      saveLabel="ذخیره تنظیمات پیامک"
      onTest={() => runBalance.mutate()}
      testLabel="تست اعتبار درگاه"
      testing={runBalance.isPending}
      busy={save.isPending}
      note={note}
      result={balance}
      error={error}
    >
      <div className="space-y-4">
        <Switch label="سامانه پیامک فعال باشد" checked={draft.enabled} onChange={set('enabled')} />
        <div className="grid gap-4 sm:grid-cols-2">
          <Choice label="روش ارسال" value={draft.mode} options={SMS_MODES} onChange={set('mode')} />
          <Text label="شماره فرستنده" value={draft.senderNumber ?? ''} onChange={set('senderNumber')} />
        </div>
        {draft.mode !== 'Simulation' && (
          <div className="grid gap-4 sm:grid-cols-2">
            <Text label="آدرس سرویس" value={draft.endpoint ?? ''} onChange={set('endpoint')} hint="با http یا https شروع شود." />
            <Text label="سازمان/خوشه سرویس‌دهنده" value={draft.organization ?? ''} onChange={set('organization')} />
            <Text label="شناسه کاربری" value={draft.username ?? ''} onChange={set('username')} />
            <Text
              label="رمز عبور"
              value={password}
              onChange={setPassword}
              type="password"
              hint={draft.hasPassword ? 'رمز ذخیره شده است؛ برای تغییر، مقدار جدید را وارد کنید.' : 'هنوز رمزی ذخیره نشده است.'}
            />
          </div>
        )}
        <div className="grid gap-4 sm:grid-cols-3">
          <NumberField label="تایم‌اوت (ثانیه)" value={draft.timeoutSeconds} onChange={set('timeoutSeconds')} />
          <NumberField label="فاصله تلاش مجدد (دقیقه)" value={draft.retryMinutes} onChange={set('retryMinutes')} />
          <NumberField label="حداکثر تعداد تلاش" value={draft.maxAttempts} onChange={set('maxAttempts')} />
        </div>
        <div className="flex flex-wrap items-end gap-3 rounded-xl bg-slate-50 px-4 py-3 ring-1 ring-slate-200">
          <div className="min-w-52 flex-1">
            <Text label="شماره برای پیامک آزمایشی" value={mobile} onChange={setMobile} />
          </div>
          <button type="button" className="btn-ghost btn-sm" onClick={() => sendTest.mutate()} disabled={sendTest.isPending || !mobile.trim()}>
            {sendTest.isPending ? 'در حال ثبت…' : 'ارسال آزمایشی'}
          </button>
        </div>
        {sendNote && <p className="rounded-xl bg-sky-50 px-4 py-3 text-xs leading-6 text-sky-900 ring-1 ring-sky-100">{sendNote}</p>}
      </div>
    </Card>
  )
}

function OperationsCard() {
  const query = useQuery({
    queryKey: ['connections-operations'],
    queryFn: () => api.get<OperationsSettings>('/api/settings/connections/operations'),
  })
  const [draft, setDraft] = useState<OperationsSettings>(EMPTY_OPERATIONS)
  const [note, setNote] = useState<SettingsSaveResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (query.data) setDraft(query.data)
  }, [query.data])

  const save = useMutation({
    mutationFn: () => api.put<SettingsSaveResult>('/api/settings/connections/operations', draft),
    onSuccess: setNote,
    onError: (e: unknown) => setError(message(e)),
  })

  if (query.isLoading) return <Busy text="در حال خواندن تنظیمات عملیات…" />

  const set = <K extends keyof OperationsSettings>(key: K) => (value: OperationsSettings[K]) => setDraft({ ...draft, [key]: value })

  return (
    <Card
      title="عملیات"
      hint="سطح لاگ و مسیر فونت با restart سرویس اعمال می‌شوند؛ اندازه کاغذ گزارش بی‌درنگ معتبر است."
      onSave={() => save.mutate()}
      saveLabel="ذخیره تنظیمات عملیات"
      busy={save.isPending}
      note={note}
      error={error}
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <Choice label="سطح لاگ" value={draft.logLevel} options={LOG_LEVELS} onChange={set('logLevel')} />
        <Choice label="اندازه کاغذ گزارش" value={draft.reportPaperSize} options={PAPER_SIZES} onChange={set('reportPaperSize')} />
      </div>
      <div className="mt-4">
        <Text
          label="مسیر فونت فارسی PDF"
          value={draft.pdfFontPath ?? ''}
          onChange={set('pdfFontPath')}
          hint="مسیر کامل؛ با خالی گذاشتن، فونت پیش‌فرض ویندوز به کار می‌رود."
        />
      </div>
    </Card>
  )
}
