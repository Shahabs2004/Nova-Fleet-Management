import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { ConnectionsSettingsTab } from '../components/ConnectionsSettings'
import { EmptyState, Modal, PageHeader, Spinner } from '../components/ui'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import { formatFaDateTime } from '../lib/date'
import { LOCATION_STATUS } from '../lib/labels'
import type { LocationDto, OrgUnitDto, SettingsBundle } from '../lib/types'

type Tab = 'organization' | 'rules' | 'template' | 'split-reasons' | 'org-units' | 'locations' | 'connections'

export function SettingsPage() {
  const { can } = useAuth()
  const tabs: { key: Tab; title: string; visible: boolean }[] = [
    { key: 'organization', title: 'سازمان', visible: can('settings.system.manage') },
    { key: 'rules', title: 'قواعد مأموریت', visible: can('settings.system.manage') },
    { key: 'template', title: 'قالب حکم', visible: can('settings.mission-template.manage') || can('settings.system.manage') },
    { key: 'split-reasons', title: 'دلایل تقسیم', visible: can('settings.mission-split-reasons.manage') || can('settings.system.manage') },
    { key: 'org-units', title: 'واحدهای سازمانی', visible: can('settings.system.manage') },
    { key: 'locations', title: 'مکان‌ها', visible: can('locations.manage') },
    { key: 'connections', title: 'اتصال سرویس‌ها', visible: can('settings.connections.manage') },
  ]
  const available = tabs.filter((item) => item.visible)
  const [tab, setTab] = useState<Tab>(available[0]?.key ?? 'locations')

  return (
    <div>
      <PageHeader title="تنظیمات" subtitle="پیکربندی سازمان، قواعد سامانه و داده‌های پایه" />

      {available.length === 0 && <EmptyState title="دسترسی ندارید" hint="برای مشاهده تنظیمات به مجوز مدیریت سامانه نیاز است." />}

      {available.length > 1 && (
        <div className="mb-4 flex flex-wrap gap-1 rounded-xl bg-slate-100/80 p-1 text-sm font-medium">
          {available.map((item) => (
            <button
              key={item.key}
              className={`rounded-lg px-4 py-2 transition-colors ${tab === item.key ? 'bg-white text-emerald-900 shadow-sm' : 'text-slate-500'}`}
              onClick={() => setTab(item.key)}
            >
              {item.title}
            </button>
          ))}
        </div>
      )}

      {tab === 'organization' && <OrganizationTab />}
      {tab === 'rules' && <RulesTab />}
      {tab === 'template' && <TemplateTab />}
      {tab === 'split-reasons' && <SplitReasonsTab />}
      {tab === 'org-units' && <OrgUnitsTab />}
      {tab === 'locations' && <LocationsTab />}
      {tab === 'connections' && <ConnectionsSettingsTab />}
    </div>
  )
}

function ErrorBox({ error }: { error: unknown }) {
  return <div className="card p-6 text-sm font-medium text-rose-700">{(error as Error)?.message ?? 'خطا در دریافت اطلاعات.'}</div>
}

function useSettings() {
  return useQuery({ queryKey: ['settings'], queryFn: () => api.get<SettingsBundle>('/api/settings') })
}

function OrganizationTab() {
  const queryClient = useQueryClient()
  const settings = useSettings()
  const [name, setName] = useState('')
  const [logo, setLogo] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const logoInputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (settings.data) {
      setName(settings.data.organization.name)
      setLogo(settings.data.organization.logoDataUrl)
    }
  }, [settings.data])

  const save = useMutation({
    mutationFn: () => api.put('/api/settings/organization', { name: name.trim(), logoDataUrl: logo }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['settings'] }),
    onError: (e: unknown) => setError((e as Error).message),
  })

  if (settings.isLoading) return <Spinner />
  if (settings.isError) return <ErrorBox error={settings.error} />

  const pickLogo = (file: File | undefined) => {
    if (!file) return
    if (file.size > 512 * 1024) {
      setError('حجم لوگو باید کمتر از ۵۱۲ کیلوبایت باشد.')
      return
    }
    const reader = new FileReader()
    reader.onload = () => setLogo(typeof reader.result === 'string' ? reader.result : null)
    reader.readAsDataURL(file)
  }

  return (
    <div className="card max-w-2xl p-6">
      <div className="space-y-4">
        <div>
          <label className="label">نام سازمان</label>
          <input className="input" value={name} onChange={(e) => setName(e.target.value)} />
          <p className="mt-1 text-[11px] text-slate-400">این نام در سربرگ، حکم مأموریت و گزارش‌ها استفاده می‌شود.</p>
        </div>
        <div>
          <label className="label">لوگو</label>
          <div className="flex items-center gap-3">
            <div className="flex h-16 w-16 items-center justify-center overflow-hidden rounded-xl border border-slate-200 bg-slate-50">
              {logo ? <img src={logo} alt="لوگو" className="h-full w-full object-contain" /> : <span className="text-[10px] text-slate-400">بدون لوگو</span>}
            </div>
            <input
              ref={logoInputRef}
              type="file"
              accept="image/png,image/jpeg,image/svg+xml"
              className="hidden"
              onChange={(e) => {
                pickLogo(e.target.files?.[0])
                // پاک‌کردن مقدار تا انتخاب دوباره همان فایل هم onChange را روشن کند.
                e.target.value = ''
              }}
            />
            <button type="button" className="btn-ghost btn-sm" onClick={() => logoInputRef.current?.click()}>
              انتخاب تصویر
            </button>
            {logo && (
              <button className="btn-ghost btn-sm text-rose-600" onClick={() => setLogo(null)}>
                حذف
              </button>
            )}
          </div>
        </div>
        {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
        <div className="flex justify-end">
          <button className="btn-primary" disabled={save.isPending || !name.trim()} onClick={() => save.mutate()}>
            {save.isPending ? 'در حال ذخیره…' : 'ذخیره تنظیمات سازمان'}
          </button>
        </div>
      </div>
    </div>
  )
}

function NumberField({
  label,
  value,
  onChange,
  hint,
  min = 0,
}: {
  label: string
  value: number
  onChange: (value: number) => void
  hint?: string
  min?: number
}) {
  return (
    <div>
      <label className="label">{label}</label>
      <input
        type="number"
        min={min}
        className="input num"
        value={Number.isFinite(value) ? value : 0}
        onChange={(e) => onChange(Number(e.target.value))}
      />
      {hint && <p className="mt-1 text-[11px] text-slate-400">{hint}</p>}
    </div>
  )
}

function RulesTab() {
  const queryClient = useQueryClient()
  const settings = useSettings()
  const [rules, setRules] = useState<SettingsBundle['missionRules'] | null>(null)
  const [policy, setPolicy] = useState<SettingsBundle['sessionPolicy'] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (settings.data) {
      setRules(settings.data.missionRules)
      setPolicy(settings.data.sessionPolicy)
    }
  }, [settings.data])

  const saveRules = useMutation({
    mutationFn: () => api.put('/api/settings/mission-rules', rules),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['settings'] }),
    onError: (e: unknown) => setError((e as Error).message),
  })

  const savePolicy = useMutation({
    mutationFn: () => api.put('/api/settings/session-policy', policy),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['settings'] }),
    onError: (e: unknown) => setError((e as Error).message),
  })

  if (settings.isLoading || !rules || !policy) return <Spinner />
  if (settings.isError) return <ErrorBox error={settings.error} />

  return (
    <div className="space-y-4">
      <div className="card p-6">
        <h3 className="mb-4 text-sm font-bold text-emerald-950">قواعد مأموریت</h3>
        <div className="grid gap-4 sm:grid-cols-2">
          <NumberField
            label="فاصله استراحت (دقیقه)"
            value={rules.minimumGapMinutes}
            onChange={(v) => setRules({ ...rules, minimumGapMinutes: v })}
            hint="حداقل فاصله بین دو مأموریت متوالی یک راننده یا خودرو."
          />
          <NumberField
            label="حداکثر روزهای آینده برای ثبت"
            value={rules.futureRequestDays}
            onChange={(v) => setRules({ ...rules, futureRequestDays: v })}
            hint="ثبت مأموریت بیش از این بازه مجاز نیست."
          />
          <NumberField
            label="بستن خودکار انصراف (ساعت)"
            value={rules.withdrawalAutoCloseHours}
            onChange={(v) => setRules({ ...rules, withdrawalAutoCloseHours: v })}
            hint="درخواست انصراف بدون بررسی پس از این مدت بسته می‌شود."
          />
          <NumberField
            label="یادآوری شروع (دقیقه)"
            value={rules.startReminderMinutes}
            onChange={(v) => setRules({ ...rules, startReminderMinutes: v })}
            hint="زمان ارسال یادآوری پیش از شروع مأموریت."
          />
        </div>
        <div className="mt-5 flex justify-end">
          <button className="btn-primary" disabled={saveRules.isPending} onClick={() => saveRules.mutate()}>
            {saveRules.isPending ? 'در حال ذخیره…' : 'ذخیره قواعد'}
          </button>
        </div>
      </div>

      <div className="card p-6">
        <h3 className="mb-4 text-sm font-bold text-emerald-950">سیاست نشست</h3>
        <div className="grid gap-4 sm:grid-cols-2">
          <NumberField
            label="عمر مطلق نشست (دقیقه)"
            value={policy.absoluteMinutes}
            onChange={(v) => setPolicy({ ...policy, absoluteMinutes: v })}
            hint="حداکثر مدت اعتبار یک ورود، مستقل از فعالیت."
          />
          <NumberField
            label="مهلت عدم فعالیت (دقیقه)"
            value={policy.idleMinutes}
            onChange={(v) => setPolicy({ ...policy, idleMinutes: v })}
            hint="نشست پس از این مدت بی‌فعالیتی پایان می‌یابد."
          />
        </div>
        {error && <div className="mt-4 rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
        <div className="mt-5 flex justify-end">
          <button className="btn-primary" disabled={savePolicy.isPending} onClick={() => savePolicy.mutate()}>
            {savePolicy.isPending ? 'در حال ذخیره…' : 'ذخیره سیاست نشست'}
          </button>
        </div>
      </div>
    </div>
  )
}

function TemplateTab() {
  const queryClient = useQueryClient()
  const settings = useSettings()
  const [template, setTemplate] = useState<SettingsBundle['missionOrderTemplate'] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (settings.data) setTemplate(settings.data.missionOrderTemplate)
  }, [settings.data])

  const save = useMutation({
    mutationFn: () => api.put('/api/settings/mission-order-template', template),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['settings'] }),
    onError: (e: unknown) => setError((e as Error).message),
  })

  if (settings.isLoading || !template) return <Spinner />
  if (settings.isError) return <ErrorBox error={settings.error} />

  return (
    <div className="card max-w-2xl p-6">
      <div className="space-y-4">
        <div>
          <label className="label">متن سربرگ حکم</label>
          <textarea
            className="input min-h-20 resize-y"
            value={template.headerText ?? ''}
            onChange={(e) => setTemplate({ ...template, headerText: e.target.value })}
            placeholder="مثلاً: بسمه تعالی — حکم مأموریت"
          />
        </div>
        <div>
          <label className="label">متن پانویس حکم</label>
          <textarea
            className="input min-h-20 resize-y"
            value={template.footerText ?? ''}
            onChange={(e) => setTemplate({ ...template, footerText: e.target.value })}
            placeholder="مثلاً: این حکم صرفاً برای انجام مأموریت اداری معتبر است."
          />
        </div>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            className="h-4 w-4 accent-emerald-700"
            checked={template.showLogo}
            onChange={(e) => setTemplate({ ...template, showLogo: e.target.checked })}
          />
          نمایش لوگوی سازمان در حکم
        </label>
        {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
        <div className="flex justify-end">
          <button className="btn-primary" disabled={save.isPending} onClick={() => save.mutate()}>
            {save.isPending ? 'در حال ذخیره…' : 'ذخیره قالب حکم'}
          </button>
        </div>
      </div>
    </div>
  )
}

interface SplitReason {
  id: string
  title: string
  sortOrder: number
  isActive: boolean
}

function SplitReasonsTab() {
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState('')
  const [error, setError] = useState<string | null>(null)

  const reasons = useQuery({
    queryKey: ['split-reasons'],
    queryFn: () => api.get<SplitReason[]>('/api/settings/split-reasons?includeInactive=true'),
  })

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['split-reasons'] })

  const create = useMutation({
    mutationFn: () => api.post('/api/settings/split-reasons', { name: draft.trim() }),
    onSuccess: () => {
      setDraft('')
      refresh()
    },
    onError: (e: unknown) => setError((e as Error).message),
  })

  const update = useMutation({
    mutationFn: (payload: { id: string; title: string; sortOrder: number; isActive: boolean }) =>
      api.put(`/api/settings/split-reasons/${payload.id}`, {
        title: payload.title,
        sortOrder: payload.sortOrder,
        isActive: payload.isActive,
      }),
    onSuccess: refresh,
    onError: (e: unknown) => setError((e as Error).message),
  })

  return (
    <div className="max-w-3xl space-y-4">
      <div className="card flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-52 flex-1">
          <label className="label">دلیل جدید</label>
          <input className="input" value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="مثلاً: تغییر برنامه کاری" />
        </div>
        <button className="btn-primary btn-sm" disabled={create.isPending || !draft.trim()} onClick={() => create.mutate()}>
          افزودن
        </button>
      </div>

      {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}

      <div className="card overflow-hidden">
        {reasons.isLoading && <Spinner />}
        {reasons.isError && <ErrorBox error={reasons.error} />}
        {reasons.data && reasons.data.length === 0 && <EmptyState title="دلیلی ثبت نشده است" hint="دلایل تقسیم مأموریت را از فرم بالا اضافه کنید." />}
        {reasons.data && reasons.data.length > 0 && (
          <table className="w-full text-sm">
            <thead>
              <tr className="table-head">
                <th className="text-right">عنوان</th>
                <th className="text-right">ترتیب</th>
                <th className="text-right">وضعیت</th>
                <th className="text-left">عملیات</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {reasons.data.map((reason) => (
                <tr key={reason.id} className="hover:bg-slate-50/70">
                  <td>
                    <input
                      className="input h-9 py-1 text-xs"
                      defaultValue={reason.title}
                      onBlur={(e) => {
                        const title = e.target.value.trim()
                        if (title && title !== reason.title) update.mutate({ ...reason, title })
                      }}
                    />
                  </td>
                  <td>
                    <input
                      type="number"
                      className="input num h-9 w-20 py-1 text-xs"
                      defaultValue={reason.sortOrder}
                      onBlur={(e) => {
                        const sortOrder = Number(e.target.value)
                        if (Number.isFinite(sortOrder) && sortOrder !== reason.sortOrder) update.mutate({ ...reason, sortOrder })
                      }}
                    />
                  </td>
                  <td>
                    <span className={`badge ${reason.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'}`}>
                      {reason.isActive ? 'فعال' : 'غیرفعال'}
                    </span>
                  </td>
                  <td className="text-left">
                    <button className="btn-ghost btn-sm" onClick={() => update.mutate({ ...reason, isActive: !reason.isActive })}>
                      {reason.isActive ? 'غیرفعال‌سازی' : 'فعال‌سازی'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}

function OrgUnitsTab() {
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState('')
  const [error, setError] = useState<string | null>(null)

  const units = useQuery({ queryKey: ['org-units'], queryFn: () => api.get<OrgUnitDto[]>('/api/organization-units') })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['org-units'] })

  const create = useMutation({
    mutationFn: () => api.post('/api/organization-units', { name: draft.trim() }),
    onSuccess: () => {
      setDraft('')
      refresh()
    },
    onError: (e: unknown) => setError((e as Error).message),
  })

  const toggle = useMutation({
    mutationFn: (unit: OrgUnitDto) => api.post(`/api/organization-units/${unit.id}/active`, { active: !unit.isActive }),
    onSuccess: refresh,
    onError: (e: unknown) => setError((e as Error).message),
  })

  const rename = useMutation({
    mutationFn: (payload: { id: string; name: string }) => api.put(`/api/organization-units/${payload.id}`, { name: payload.name }),
    onSuccess: refresh,
    onError: (e: unknown) => setError((e as Error).message),
  })

  return (
    <div className="max-w-3xl space-y-4">
      <div className="card flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-52 flex-1">
          <label className="label">واحد سازمانی جدید</label>
          <input className="input" value={draft} onChange={(e) => setDraft(e.target.value)} placeholder="مثلاً: معاونت فنی" />
        </div>
        <button className="btn-primary btn-sm" disabled={create.isPending || !draft.trim()} onClick={() => create.mutate()}>
          افزودن
        </button>
      </div>

      {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}

      <div className="card overflow-hidden">
        {units.isLoading && <Spinner />}
        {units.isError && <ErrorBox error={units.error} />}
        {units.data && units.data.length === 0 && <EmptyState title="واحدی ثبت نشده است" />}
        {units.data && units.data.length > 0 && (
          <table className="w-full text-sm">
            <thead>
              <tr className="table-head">
                <th className="text-right">نام</th>
                <th className="text-right">وضعیت</th>
                <th className="text-left">عملیات</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {units.data.map((unit) => (
                <tr key={unit.id} className="hover:bg-slate-50/70">
                  <td>
                    <input
                      className="input h-9 py-1 text-xs"
                      defaultValue={unit.name}
                      onBlur={(e) => {
                        const name = e.target.value.trim()
                        if (name && name !== unit.name) rename.mutate({ id: unit.id, name })
                      }}
                    />
                  </td>
                  <td>
                    <span className={`badge ${unit.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'}`}>
                      {unit.isActive ? 'فعال' : 'غیرفعال'}
                    </span>
                  </td>
                  <td className="text-left">
                    <button className="btn-ghost btn-sm" onClick={() => toggle.mutate(unit)}>
                      {unit.isActive ? 'غیرفعال‌سازی' : 'فعال‌سازی'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}

function LocationsTab() {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [rejecting, setRejecting] = useState<LocationDto | null>(null)
  const [error, setError] = useState<string | null>(null)

  const params = new URLSearchParams()
  if (status) params.set('status', status)
  if (search) params.set('q', search)

  const locations = useQuery({
    queryKey: ['locations', status, search],
    queryFn: () => api.get<LocationDto[]>(`/api/locations?${params.toString()}`),
  })

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['locations'] })

  const approve = useMutation({
    mutationFn: (id: string) => api.post(`/api/locations/${id}/approve`),
    onSuccess: refresh,
    onError: (e: unknown) => setError((e as Error).message),
  })

  const reject = useMutation({
    mutationFn: (payload: { id: string; reason: string }) => api.post(`/api/locations/${payload.id}/reject`, { reason: payload.reason }),
    onSuccess: () => {
      setRejecting(null)
      refresh()
    },
    onError: (e: unknown) => setError((e as Error).message),
  })

  const disable = useMutation({
    mutationFn: (id: string) => api.post(`/api/locations/${id}/disable`, { reason: 'غیرفعال‌سازی توسط مدیر' }),
    onSuccess: refresh,
    onError: (e: unknown) => setError((e as Error).message),
  })

  const reopen = useMutation({
    mutationFn: (location: LocationDto) => api.post(`/api/locations/${location.id}/reopen`, { name: location.name }),
    onSuccess: refresh,
    onError: (e: unknown) => setError((e as Error).message),
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.del(`/api/locations/${id}`),
    onSuccess: refresh,
    onError: (e: unknown) => setError((e as Error).message),
  })

  const busy = approve.isPending || reject.isPending || disable.isPending || reopen.isPending || remove.isPending

  return (
    <div>
      <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-52 flex-1">
          <label className="label">جستجو</label>
          <input className="input" value={search} onChange={(e) => setSearch(e.target.value)} placeholder="نام مکان" />
        </div>
        <div className="w-48">
          <label className="label">وضعیت</label>
          <select className="input" value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">همه</option>
            {Object.entries(LOCATION_STATUS).map(([value, title]) => (
              <option key={value} value={value}>
                {title}
              </option>
            ))}
          </select>
        </div>
      </div>

      {error && <div className="mb-4 rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}

      <div className="card overflow-hidden">
        {locations.isLoading && <Spinner />}
        {locations.isError && <ErrorBox error={locations.error} />}
        {locations.data && locations.data.length === 0 && <EmptyState title="مکانی یافت نشد" hint="با تغییر فیلترها دوباره تلاش کنید." />}
        {locations.data && locations.data.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead>
                <tr className="table-head">
                  <th className="text-right">نام</th>
                  <th className="text-right">وضعیت</th>
                  <th className="text-right">پیشنهاددهنده</th>
                  <th className="text-right">بازبینی</th>
                  <th className="text-right">تاریخ ثبت</th>
                  <th className="text-left">عملیات</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {locations.data.map((location) => (
                  <tr key={location.id} className="hover:bg-slate-50/70">
                    <td className="text-xs font-medium text-emerald-950">{location.name}</td>
                    <td>
                      <span className="badge bg-slate-100 text-slate-600">{LOCATION_STATUS[location.status] ?? '—'}</span>
                    </td>
                    <td className="num text-[11px] text-slate-500" dir="ltr">
                      {location.proposedBy}
                    </td>
                    <td className="text-[11px] text-slate-500">
                      {location.reviewedBy ?? '—'}
                      {location.reviewReason && <p className="text-[10px] text-slate-400">{location.reviewReason}</p>}
                    </td>
                    <td className="num text-[11px] text-slate-400">{formatFaDateTime(location.createdAtUtc)}</td>
                    <td className="space-x-1 space-x-reverse whitespace-nowrap text-left">
                      {location.status === 1 && (
                        <>
                          <button className="btn-ghost btn-sm" disabled={busy} onClick={() => approve.mutate(location.id)}>
                            تأیید
                          </button>
                          <button className="btn-ghost btn-sm text-rose-600" disabled={busy} onClick={() => setRejecting(location)}>
                            رد
                          </button>
                        </>
                      )}
                      {location.status === 2 && (
                        <button className="btn-ghost btn-sm" disabled={busy} onClick={() => disable.mutate(location.id)}>
                          غیرفعال‌سازی
                        </button>
                      )}
                      {(location.status === 3 || location.status === 4) && (
                        <>
                          <button className="btn-ghost btn-sm" disabled={busy} onClick={() => reopen.mutate(location)}>
                            بازگشایی
                          </button>
                          <button className="btn-ghost btn-sm text-rose-600" disabled={busy} onClick={() => remove.mutate(location.id)}>
                            حذف
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <Modal open={Boolean(rejecting)} title={rejecting ? `رد مکان: ${rejecting.name}` : ''} onClose={() => setRejecting(null)}>
        {rejecting && <RejectForm onSubmit={(reason) => reject.mutate({ id: rejecting.id, reason })} pending={reject.isPending} />}
      </Modal>
    </div>
  )
}

function RejectForm({ onSubmit, pending }: { onSubmit: (reason: string) => void; pending: boolean }) {
  const [reason, setReason] = useState('')
  return (
    <div className="space-y-4">
      <div>
        <label className="label">علت رد</label>
        <textarea className="input min-h-20 resize-y" value={reason} onChange={(e) => setReason(e.target.value)} />
      </div>
      <div className="flex justify-end">
        <button className="btn-primary bg-rose-600 hover:bg-rose-700" disabled={pending || !reason.trim()} onClick={() => onSubmit(reason.trim())}>
          {pending ? 'در حال ثبت…' : 'رد مکان'}
        </button>
      </div>
    </div>
  )
}