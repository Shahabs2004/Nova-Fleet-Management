import { useQuery } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { AvatarEditor } from './AvatarEditor'
import { Spinner } from './ui'
import { api } from '../lib/api'
import type { RoleDto, UserDetails } from '../lib/types'

interface Props {
  userId: string | null
  onDone: (username: string) => Promise<void>
  onSavingChange: (saving: boolean) => void
}

export function UserProfileForm({ userId, onDone, onSavingChange }: Props) {
  const details = useQuery({
    queryKey: ['users', 'details', userId],
    queryFn: () => api.get<UserDetails>(`/api/users/${userId}`),
    enabled: userId !== null,
    staleTime: 0,
    refetchOnMount: 'always',
  })
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.get<RoleDto[]>('/api/roles') })
  const [detailsLoaded, setDetailsLoaded] = useState(false)
  // Only hydrate from a successful GET on this open, never cached data after a failed refetch.
  if (!detailsLoaded && details.isSuccess && details.isFetchedAfterMount) setDetailsLoaded(true)
  const error = roles.error ?? details.error
  const ready = roles.data && (!userId || (details.data && detailsLoaded))

  return (
    <div className="space-y-4">
      {error && (
        <div role="alert" className="rounded-xl bg-rose-50 p-4 text-sm text-rose-700">
          <p>دریافت اطلاعات کاربر یا نقش‌ها ناموفق بود: {error.message}</p>
          <button type="button" className="btn-ghost btn-sm mt-2" disabled={roles.isFetching || details.isFetching} onClick={() => {
            if (roles.isError) void roles.refetch()
            if (details.isError) void details.refetch()
          }}>تلاش دوباره</button>
        </div>
      )}
      {!ready && !error && <Spinner />}
      {ready && (
        <UserFields
          key={userId ?? 'new'}
          initial={userId ? details.data : undefined}
          roles={roles.data ?? []}
          loading={roles.isFetching || details.isFetching || Boolean(error)}
          onDone={onDone}
          onSavingChange={onSavingChange}
        />
      )}
    </div>
  )
}

function UserFields({ initial, roles, loading, onDone, onSavingChange }: {
  initial?: UserDetails
  roles: RoleDto[]
  loading: boolean
  onDone: Props['onDone']
  onSavingChange: Props['onSavingChange']
}) {
  const [form, setForm] = useState(() => ({
    username: initial?.username ?? '',
    password: '',
    firstName: initial?.firstName ?? '',
    lastName: initial?.lastName ?? '',
    mobilePhone: initial?.mobilePhone ?? '',
    email: initial?.email ?? '',
    organizationalUnit: initial?.organizationalUnit ?? '',
    isLocalAdmin: initial?.isLocalAdmin ?? false,
  }))
  const [roleIds, setRoleIds] = useState<string[]>(() => [...(initial?.roleIds ?? [])])
  const [avatarDataUrl, setAvatarDataUrl] = useState<string | null>(initial?.avatarDataUrl ?? null)
  const [editing, setEditing] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const saving = useRef(false)
  const [error, setError] = useState<string | null>(null)
  const valid = Boolean(form.firstName.trim() && form.lastName.trim() &&
    (initial || (form.username.trim() && form.password.length >= 8)))
  const blocked = submitting || loading || editing || !valid
  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((prev) => ({ ...prev, [key]: e.target.value }))

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (blocked || saving.current) return
    saving.current = true
    setSubmitting(true)
    onSavingChange(true)
    setError(null)
    try {
      const body = {
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        mobilePhone: form.mobilePhone.trim() || null,
        email: form.email.trim() || null,
        organizationalUnit: form.organizationalUnit.trim() || null,
        roleIds,
        avatarDataUrl,
      }
      if (initial) await api.put(`/api/users/${initial.id}`, body)
      else await api.post('/api/users', {
        ...body, username: form.username.trim(), password: form.password, isLocalAdmin: form.isLocalAdmin,
      })
      await onDone(form.username.trim())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'ذخیره کاربر ناموفق بود.')
    } finally {
      saving.current = false
      setSubmitting(false)
      onSavingChange(false)
    }
  }

  return (
    <form className="space-y-4" onSubmit={submit}>
      <AvatarEditor name={`${form.firstName} ${form.lastName}`.trim() || form.username} value={avatarDataUrl}
        onChange={setAvatarDataUrl} onEditingChange={setEditing} disabled={submitting || loading} />
      <fieldset disabled={submitting || loading} className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <label>
            <span className="label">نام کاربری</span>
            <input className="input num" dir="ltr" value={form.username} onChange={set('username')} readOnly={Boolean(initial)} required />
          </label>
          {!initial && <label>
            <span className="label">رمز عبور (حداقل ۸ نویسه)</span>
            <input className="input" dir="ltr" type="password" autoComplete="new-password" value={form.password} onChange={set('password')} minLength={8} required />
          </label>}
          <label>
            <span className="label">نام</span>
            <input className="input" value={form.firstName} onChange={set('firstName')} required />
          </label>
          <label>
            <span className="label">نام خانوادگی</span>
            <input className="input" value={form.lastName} onChange={set('lastName')} required />
          </label>
          <label>
            <span className="label">موبایل</span>
            <input className="input num" dir="ltr" value={form.mobilePhone} onChange={set('mobilePhone')} />
          </label>
          <label>
            <span className="label">ایمیل</span>
            <input className="input" dir="ltr" value={form.email} onChange={set('email')} />
          </label>
          <label className="sm:col-span-2">
            <span className="label">واحد سازمانی</span>
            <input className="input" value={form.organizationalUnit} onChange={set('organizationalUnit')} />
          </label>
        </div>
        <fieldset>
          <legend className="label">نقش‌ها</legend>
          <div className="flex flex-wrap gap-2">
            {roles.map((role) => {
              const checked = roleIds.includes(role.id)
              return <label key={role.id} className={`flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-1.5 text-xs ${checked ? 'border-emerald-300 bg-emerald-50 text-emerald-800' : 'border-slate-200 text-slate-600'}`}>
                <input type="checkbox" className="accent-emerald-700" checked={checked}
                  onChange={() => setRoleIds((prev) => checked ? prev.filter((id) => id !== role.id) : [...prev, role.id])} />
                {role.title}
              </label>
            })}
            {roles.length === 0 && <p className="text-xs text-slate-500">نقشی تعریف نشده است.</p>}
          </div>
        </fieldset>
        {!initial ? <label className="flex items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" className="h-4 w-4 accent-emerald-700" checked={form.isLocalAdmin}
            onChange={(e) => setForm((prev) => ({ ...prev, isLocalAdmin: e.target.checked }))} />
          این کاربر مدیر محلی باشد (دسترسی کامل)
        </label> : <p className="text-xs text-slate-500">
          نوع حساب: {initial.isDirectoryUser ? 'دایرکتوری' : 'محلی'}{initial.isLocalAdmin ? ' · مدیر محلی' : ''} · {initial.isDisabled ? 'غیرفعال' : 'فعال'}
        </p>}
      </fieldset>
      {error && <div role="alert" className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">{error}</div>}
      <div className="flex justify-end">
        <button type="submit" className="btn-primary" disabled={blocked}>
          {submitting ? 'در حال ذخیره…' : initial ? 'ذخیره تغییرات' : 'ایجاد کاربر'}
        </button>
      </div>
    </form>
  )
}
