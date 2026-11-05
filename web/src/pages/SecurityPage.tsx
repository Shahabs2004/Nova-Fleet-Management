import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { EmptyState, Modal, PageHeader, Pagination, Spinner } from '../components/ui'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import { formatFaDateTime, toFa } from '../lib/date'
import { SESSION_METHOD, SESSION_STATUS } from '../lib/labels'
import type { AuditEntryDto, PagedResult, RoleDto, UserListItem, UserSessionDto } from '../lib/types'

interface PermissionDefinition {
  code: string
  title: string
  description: string
}

interface Assignment {
  permission: string
  effect: number
}

const GRANT = 1
const DENY = 2

type Tab = 'users' | 'roles' | 'sessions' | 'audit'

const TABS: { key: Tab; title: string; permission: string }[] = [
  { key: 'users', title: 'کاربران', permission: 'users.manage' },
  { key: 'roles', title: 'نقش‌ها و مجوزها', permission: 'settings.authorization' },
  { key: 'sessions', title: 'نشست‌ها', permission: 'security.sessions.view' },
  { key: 'audit', title: 'ممیزی', permission: 'security.audit.view' },
]

export function SecurityPage() {
  const { can } = useAuth()
  const available = TABS.filter((item) => can(item.permission))
  const [tab, setTab] = useState<Tab>(available[0]?.key ?? 'sessions')

  return (
    <div>
      <PageHeader title="امنیت و دسترسی" subtitle="کاربران، نقش‌ها، نشست‌های فعال و سوابق ممیزی" />

      {available.length === 0 && <EmptyState title="دسترسی ندارید" hint="برای مشاهده این بخش به مجوز مدیریت امنیت نیاز است." />}

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

      {tab === 'users' && can('users.manage') && <UsersTab />}
      {tab === 'roles' && can('settings.authorization') && <RolesTab />}
      {tab === 'sessions' && can('security.sessions.view') && <SessionsTab canForceEnd={can('settings.sessions')} />}
      {tab === 'audit' && can('security.audit.view') && <AuditTab />}
    </div>
  )
}

function ErrorBox({ error }: { error: unknown }) {
  return <div className="card p-6 text-sm font-medium text-rose-700">{(error as Error)?.message ?? 'خطا در دریافت اطلاعات.'}</div>
}

function UsersTab() {
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [includeDisabled, setIncludeDisabled] = useState(true)
  const [createOpen, setCreateOpen] = useState(false)
  const [resetting, setResetting] = useState<UserListItem | null>(null)

  const users = useQuery({
    queryKey: ['users', search, includeDisabled],
    queryFn: () =>
      api.get<UserListItem[]>(
        `/api/users?includeDisabled=${includeDisabled}&search=${encodeURIComponent(search)}`,
      ),
  })

  const toggle = useMutation({
    mutationFn: (user: UserListItem) => api.post(`/api/users/${user.id}/disabled`, { active: !user.isDisabled }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['users'] }),
  })

  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.get<RoleDto[]>('/api/roles') })

  return (
    <div>
      <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-52 flex-1">
          <label className="label">جستجو</label>
          <input className="input" placeholder="نام یا نام کاربری" value={search} onChange={(e) => setSearch(e.target.value)} />
        </div>
        <label className="flex items-center gap-2 pb-2 text-sm text-slate-600">
          <input
            type="checkbox"
            className="h-4 w-4 accent-emerald-700"
            checked={includeDisabled}
            onChange={(e) => setIncludeDisabled(e.target.checked)}
          />
          نمایش غیرفعال‌ها
        </label>
        <button className="btn-primary btn-sm" onClick={() => setCreateOpen(true)}>
          کاربر جدید
        </button>
      </div>

      <div className="card overflow-hidden">
        {users.isLoading && <Spinner />}
        {users.isError && <ErrorBox error={users.error} />}
        {users.data && users.data.length === 0 && <EmptyState title="کاربری یافت نشد" hint="عبارت جستجو را تغییر دهید." />}
        {users.data && users.data.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead>
                <tr className="table-head">
                  <th className="text-right">کاربر</th>
                  <th className="text-right">واحد سازمانی</th>
                  <th className="text-right">نقش‌ها</th>
                  <th className="text-right">نوع</th>
                  <th className="text-right">وضعیت</th>
                  <th className="text-left">عملیات</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {users.data.map((user) => (
                  <tr key={user.id} className="hover:bg-slate-50/70">
                    <td>
                      <p className="text-xs font-bold text-emerald-950">{user.displayName}</p>
                      <p className="num mt-0.5 text-[11px] text-slate-400" dir="ltr">
                        {user.username}
                      </p>
                    </td>
                    <td className="text-xs text-slate-600">{user.organizationalUnit ?? '—'}</td>
                    <td className="text-xs text-slate-600">{user.roles.length > 0 ? user.roles.join('، ') : '—'}</td>
                    <td>
                      <span className="badge bg-slate-100 text-slate-600">
                        {user.isDirectoryUser ? 'دایرکتوری' : 'محلی'}
                        {user.isLocalAdmin ? ' · مدیر' : ''}
                      </span>
                    </td>
                    <td>
                      <span className={`badge ${user.isDisabled ? 'bg-rose-50 text-rose-700' : 'bg-emerald-50 text-emerald-700'}`}>
                        {user.isDisabled ? 'غیرفعال' : 'فعال'}
                      </span>
                    </td>
                    <td className="space-x-1 space-x-reverse text-left">
                      {!user.isDirectoryUser && (
                        <button className="btn-ghost btn-sm" onClick={() => setResetting(user)}>
                          بازنشانی رمز
                        </button>
                      )}
                      <button
                        className="btn-ghost btn-sm"
                        disabled={toggle.isPending}
                        onClick={() => toggle.mutate(user)}
                      >
                        {user.isDisabled ? 'فعال‌سازی' : 'غیرفعال‌سازی'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <Modal open={createOpen} title="ایجاد کاربر محلی" onClose={() => setCreateOpen(false)} wide>
        <CreateUserForm
          roles={roles.data ?? []}
          onDone={async () => {
            setCreateOpen(false)
            await queryClient.invalidateQueries({ queryKey: ['users'] })
          }}
        />
      </Modal>

      <Modal open={Boolean(resetting)} title={resetting ? `بازنشانی رمز: ${resetting.displayName}` : ''} onClose={() => setResetting(null)}>
        {resetting && (
          <ResetPasswordForm
            userId={resetting.id}
            onDone={async () => {
              setResetting(null)
              await queryClient.invalidateQueries({ queryKey: ['users'] })
            }}
          />
        )}
      </Modal>
    </div>
  )
}

function CreateUserForm({ roles, onDone }: { roles: RoleDto[]; onDone: () => Promise<void> }) {
  const [form, setForm] = useState({
    username: '',
    password: '',
    firstName: '',
    lastName: '',
    mobilePhone: '',
    email: '',
    organizationalUnit: '',
    isLocalAdmin: false,
  })
  const [roleIds, setRoleIds] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const set = (key: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((prev) => ({ ...prev, [key]: e.target.value }))

  const submit = async () => {
    setSubmitting(true)
    setError(null)
    try {
      await api.post('/api/users', {
        username: form.username.trim(),
        password: form.password,
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        mobilePhone: form.mobilePhone.trim() || null,
        email: form.email.trim() || null,
        organizationalUnit: form.organizationalUnit.trim() || null,
        isLocalAdmin: form.isLocalAdmin,
        roleIds,
      })
      await onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSubmitting(false)
    }
  }

  const valid = form.username.trim() && form.password.length >= 8 && form.firstName.trim() && form.lastName.trim()

  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <div>
          <label className="label">نام کاربری</label>
          <input className="input num" dir="ltr" value={form.username} onChange={set('username')} />
        </div>
        <div>
          <label className="label">رمز عبور (حداقل ۸ نویسه)</label>
          <input className="input" dir="ltr" type="password" value={form.password} onChange={set('password')} />
        </div>
        <div>
          <label className="label">نام</label>
          <input className="input" value={form.firstName} onChange={set('firstName')} />
        </div>
        <div>
          <label className="label">نام خانوادگی</label>
          <input className="input" value={form.lastName} onChange={set('lastName')} />
        </div>
        <div>
          <label className="label">موبایل</label>
          <input className="input num" dir="ltr" value={form.mobilePhone} onChange={set('mobilePhone')} />
        </div>
        <div>
          <label className="label">ایمیل</label>
          <input className="input" dir="ltr" value={form.email} onChange={set('email')} />
        </div>
        <div className="sm:col-span-2">
          <label className="label">واحد سازمانی</label>
          <input className="input" value={form.organizationalUnit} onChange={set('organizationalUnit')} />
        </div>
      </div>
      <div>
        <label className="label">نقش‌ها</label>
        <div className="flex flex-wrap gap-2">
          {roles.map((role) => {
            const checked = roleIds.includes(role.id)
            return (
              <label
                key={role.id}
                className={`cursor-pointer rounded-lg border px-3 py-1.5 text-xs transition-colors ${
                  checked ? 'border-emerald-300 bg-emerald-50 text-emerald-800' : 'border-slate-200 text-slate-600'
                }`}
              >
                <input
                  type="checkbox"
                  className="sr-only"
                  checked={checked}
                  onChange={() =>
                    setRoleIds((prev) => (checked ? prev.filter((id) => id !== role.id) : [...prev, role.id]))
                  }
                />
                {role.title}
              </label>
            )
          })}
        </div>
      </div>
      <label className="flex items-center gap-2 text-sm text-slate-600">
        <input
          type="checkbox"
          className="h-4 w-4 accent-emerald-700"
          checked={form.isLocalAdmin}
          onChange={(e) => setForm((prev) => ({ ...prev, isLocalAdmin: e.target.checked }))}
        />
        این کاربر مدیر محلی باشد (دسترسی کامل)
      </label>
      {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
      <div className="flex justify-end">
        <button className="btn-primary" disabled={submitting || !valid} onClick={submit}>
          {submitting ? 'در حال ایجاد…' : 'ایجاد کاربر'}
        </button>
      </div>
    </div>
  )
}

function ResetPasswordForm({ userId, onDone }: { userId: string; onDone: () => Promise<void> }) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const submit = async () => {
    setSubmitting(true)
    setError(null)
    try {
      await api.post(`/api/users/${userId}/reset-password`, { newPassword: password })
      await onDone()
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-4">
      <div>
        <label className="label">رمز عبور جدید</label>
        <input className="input" dir="ltr" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
      </div>
      {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
      <div className="flex justify-end">
        <button className="btn-primary" disabled={submitting || password.length < 8} onClick={submit}>
          {submitting ? 'در حال ذخیره…' : 'بازنشانی رمز'}
        </button>
      </div>
    </div>
  )
}

function RolesTab() {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<RoleDto | null>(null)
  const roles = useQuery({ queryKey: ['roles'], queryFn: () => api.get<RoleDto[]>('/api/roles') })
  const permissions = useQuery({ queryKey: ['permissions'], queryFn: () => api.get<PermissionDefinition[]>('/api/permissions') })

  const toggle = useMutation({
    mutationFn: (role: RoleDto) => api.post(`/api/roles/${role.id}/active`, { active: !role.isActive }),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['roles'] }),
  })

  if (roles.isLoading) return <Spinner />
  if (roles.isError) return <ErrorBox error={roles.error} />

  return (
    <div>
      <div className="grid gap-3 lg:grid-cols-2">
        {(roles.data ?? []).map((role) => (
          <div key={role.id} className="card p-4">
            <div className="flex items-start justify-between gap-3">
              <div>
                <p className="text-sm font-bold text-emerald-950">{role.title}</p>
                <p className="num mt-0.5 text-[11px] text-slate-400" dir="ltr">
                  {role.name}
                </p>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                <span className={`badge ${role.isActive ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'}`}>
                  {role.isActive ? 'فعال' : 'غیرفعال'}
                </span>
                <span className="badge bg-slate-100 text-slate-600">{toFa(role.userCount)} کاربر</span>
              </div>
            </div>
            {role.description && <p className="mt-2 text-xs leading-6 text-slate-500">{role.description}</p>}
            <div className="mt-3 flex items-center justify-between gap-2">
              <span className="text-[11px] text-slate-400">{toFa(role.permissions.length)} مجوز تعریف‌شده</span>
              <div className="flex gap-1">
                {!role.isSystem && (
                  <button className="btn-ghost btn-sm" disabled={toggle.isPending} onClick={() => toggle.mutate(role)}>
                    {role.isActive ? 'غیرفعال‌سازی' : 'فعال‌سازی'}
                  </button>
                )}
                <button className="btn-ghost btn-sm" onClick={() => setEditing(role)}>
                  مجوزها
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>

      <Modal open={Boolean(editing)} title={editing ? `مجوزهای نقش: ${editing.title}` : ''} onClose={() => setEditing(null)} wide>
        {editing && (
          <PermissionEditor
            assignments={editing.permissions}
            definitions={permissions.data ?? []}
            onSave={async (assignments) => {
              await api.put(`/api/roles/${editing.id}/permissions`, { permissions: assignments })
              setEditing(null)
              await queryClient.invalidateQueries({ queryKey: ['roles'] })
            }}
          />
        )}
      </Modal>
    </div>
  )
}

function PermissionEditor({
  assignments,
  definitions,
  onSave,
}: {
  assignments: Assignment[]
  definitions: PermissionDefinition[]
  onSave: (assignments: Assignment[]) => Promise<void>
}) {
  const initial = useMemo(() => {
    const map = new Map<string, number>()
    for (const item of assignments) map.set(item.permission, item.effect)
    return map
  }, [assignments])
  const [state, setState] = useState<Map<string, number>>(() => new Map(initial))
  const [filter, setFilter] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const cycle = (code: string) =>
    setState((prev) => {
      const next = new Map(prev)
      const current = next.get(code)
      if (current === undefined) next.set(code, GRANT)
      else if (current === GRANT) next.set(code, DENY)
      else next.delete(code)
      return next
    })

  const visible = definitions.filter(
    (item) =>
      !filter ||
      item.code.toLowerCase().includes(filter.toLowerCase()) ||
      item.title.includes(filter) ||
      item.description.includes(filter),
  )

  const save = async () => {
    setSubmitting(true)
    setError(null)
    try {
      await onSave([...state.entries()].map(([permission, effect]) => ({ permission, effect })))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-4">
      <input className="input" placeholder="جستجوی مجوز…" value={filter} onChange={(e) => setFilter(e.target.value)} />
      <p className="text-[11px] text-slate-400">روی هر مجوز بزنید تا بین «اعطا»، «سلب» و «پیش‌فرض» جابه‌جا شود. سلب بر اعطا اولویت دارد.</p>
      <div className="max-h-[50vh] space-y-1 overflow-y-auto pl-1">
        {visible.map((item) => {
          const effect = state.get(item.code)
          return (
            <button
              key={item.code}
              type="button"
              onClick={() => cycle(item.code)}
              className={`flex w-full items-center justify-between gap-3 rounded-lg border px-3 py-2 text-right transition-colors ${
                effect === GRANT
                  ? 'border-emerald-200 bg-emerald-50/70'
                  : effect === DENY
                    ? 'border-rose-200 bg-rose-50/70'
                    : 'border-slate-100 hover:bg-slate-50'
              }`}
            >
              <span className="min-w-0">
                <span className="block text-xs font-semibold text-emerald-950">{item.title}</span>
                <span className="num block text-[10px] text-slate-400" dir="ltr">
                  {item.code}
                </span>
              </span>
              <span
                className={`badge shrink-0 ${
                  effect === GRANT
                    ? 'bg-emerald-100 text-emerald-800'
                    : effect === DENY
                      ? 'bg-rose-100 text-rose-800'
                      : 'bg-slate-100 text-slate-500'
                }`}
              >
                {effect === GRANT ? 'اعطا' : effect === DENY ? 'سلب' : 'پیش‌فرض'}
              </span>
            </button>
          )
        })}
      </div>
      {error && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{error}</div>}
      <div className="flex justify-end">
        <button className="btn-primary" disabled={submitting} onClick={save}>
          {submitting ? 'در حال ذخیره…' : 'ذخیره مجوزها'}
        </button>
      </div>
    </div>
  )
}

function SessionsTab({ canForceEnd }: { canForceEnd: boolean }) {
  const queryClient = useQueryClient()
  const [username, setUsername] = useState('')
  const [status, setStatus] = useState('')
  const [page, setPage] = useState(1)
  const pageSize = 20

  const params = new URLSearchParams()
  if (username) params.set('username', username)
  if (status) params.set('status', status)
  params.set('page', String(page))
  params.set('pageSize', String(pageSize))

  const sessions = useQuery({
    queryKey: ['sessions', username, status, page],
    queryFn: () => api.get<PagedResult<UserSessionDto>>(`/api/sessions?${params.toString()}`),
  })

  const end = useMutation({
    mutationFn: (id: string) => api.post(`/api/sessions/${id}/force-end`),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['sessions'] }),
  })

  return (
    <div>
      <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-52 flex-1">
          <label className="label">نام کاربری</label>
          <input
            className="input num"
            dir="ltr"
            placeholder="username"
            value={username}
            onChange={(e) => {
              setUsername(e.target.value)
              setPage(1)
            }}
          />
        </div>
        <div className="w-44">
          <label className="label">وضعیت</label>
          <select
            className="input"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value)
              setPage(1)
            }}
          >
            <option value="">همه</option>
            {Object.entries(SESSION_STATUS).map(([value, title]) => (
              <option key={value} value={value}>
                {title}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div className="card overflow-hidden">
        {sessions.isLoading && <Spinner />}
        {sessions.isError && <ErrorBox error={sessions.error} />}
        {sessions.data && sessions.data.items.length === 0 && <EmptyState title="نشستی ثبت نشده است" />}
        {sessions.data && sessions.data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead>
                <tr className="table-head">
                  <th className="text-right">کاربر</th>
                  <th className="text-right">روش ورود</th>
                  <th className="text-right">وضعیت</th>
                  <th className="text-right">ورود</th>
                  <th className="text-right">آخرین فعالیت</th>
                  <th className="text-right">IP</th>
                  <th className="text-left">عملیات</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {sessions.data.items.map((session) => (
                  <tr key={session.id} className="hover:bg-slate-50/70">
                    <td>
                      <p className="text-xs font-bold text-emerald-950">{session.displayName}</p>
                      <p className="num mt-0.5 text-[11px] text-slate-400" dir="ltr">
                        {session.username}
                      </p>
                    </td>
                    <td className="text-xs text-slate-600">{SESSION_METHOD[session.loginMethod] ?? '—'}</td>
                    <td>
                      <span
                        className={`badge ${
                          session.status === 1 ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-600'
                        }`}
                      >
                        {SESSION_STATUS[session.status] ?? '—'}
                      </span>
                    </td>
                    <td className="num text-[11px] text-slate-500">{formatFaDateTime(session.loginAtUtc)}</td>
                    <td className="num text-[11px] text-slate-500">{formatFaDateTime(session.lastActivityAtUtc)}</td>
                    <td className="num text-[11px] text-slate-400" dir="ltr">
                      {session.clientIpAddress ?? '—'}
                    </td>
                    <td className="text-left">
                      {canForceEnd && session.status === 1 && (
                        <button className="btn-ghost btn-sm" disabled={end.isPending} onClick={() => end.mutate(session.id)}>
                          پایان نشست
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {sessions.data && (
          <div className="px-5 pb-4">
            <Pagination page={page} pageSize={pageSize} total={sessions.data.totalCount} onChange={setPage} />
          </div>
        )}
      </div>
    </div>
  )
}

function AuditTab() {
  const [actor, setActor] = useState('')
  const [action, setAction] = useState('')
  const [outcome, setOutcome] = useState('')
  const [page, setPage] = useState(1)
  const pageSize = 25

  const actions = useQuery({ queryKey: ['audit-actions'], queryFn: () => api.get<string[]>('/api/audit/actions') })

  const params = new URLSearchParams()
  if (actor) params.set('actor', actor)
  if (action) params.set('action', action)
  if (outcome) params.set('outcome', outcome)
  params.set('page', String(page))
  params.set('pageSize', String(pageSize))

  const audit = useQuery({
    queryKey: ['audit', actor, action, outcome, page],
    queryFn: () => api.get<PagedResult<AuditEntryDto>>(`/api/audit?${params.toString()}`),
  })

  return (
    <div>
      <div className="card mb-4 flex flex-wrap items-end gap-3 p-4">
        <div className="min-w-44 flex-1">
          <label className="label">کاربر</label>
          <input
            className="input num"
            dir="ltr"
            placeholder="username"
            value={actor}
            onChange={(e) => {
              setActor(e.target.value)
              setPage(1)
            }}
          />
        </div>
        <div className="w-52">
          <label className="label">عملیات</label>
          <select
            className="input"
            value={action}
            onChange={(e) => {
              setAction(e.target.value)
              setPage(1)
            }}
          >
            <option value="">همه عملیات</option>
            {(actions.data ?? []).map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </select>
        </div>
        <div className="w-40">
          <label className="label">نتیجه</label>
          <select
            className="input"
            value={outcome}
            onChange={(e) => {
              setOutcome(e.target.value)
              setPage(1)
            }}
          >
            <option value="">همه</option>
            <option value="Success">موفق</option>
            <option value="Denied">رد دسترسی</option>
            <option value="Failure">ناموفق</option>
          </select>
        </div>
      </div>

      <div className="card overflow-hidden">
        {audit.isLoading && <Spinner />}
        {audit.isError && <ErrorBox error={audit.error} />}
        {audit.data && audit.data.items.length === 0 && <EmptyState title="سابقه‌ای یافت نشد" />}
        {audit.data && audit.data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] text-sm">
              <thead>
                <tr className="table-head">
                  <th className="text-right">زمان</th>
                  <th className="text-right">کاربر</th>
                  <th className="text-right">عملیات</th>
                  <th className="text-right">موجودیت</th>
                  <th className="text-right">نتیجه</th>
                  <th className="text-right">IP</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {audit.data.items.map((entry) => (
                  <tr key={entry.id} className="hover:bg-slate-50/70">
                    <td className="num text-[11px] text-slate-500">{formatFaDateTime(entry.occurredAtUtc)}</td>
                    <td className="num text-xs text-slate-700" dir="ltr">
                      {entry.actorUsername}
                    </td>
                    <td>
                      <p className="num text-xs font-medium text-emerald-950" dir="ltr">
                        {entry.action}
                      </p>
                      {entry.reason && <p className="mt-0.5 text-[10px] text-slate-400">{entry.reason}</p>}
                    </td>
                    <td className="text-[11px] text-slate-500">{entry.entityType}</td>
                    <td>
                      <span
                        className={`badge ${
                          entry.outcome === 'Success'
                            ? 'bg-emerald-50 text-emerald-700'
                            : entry.outcome === 'Denied'
                              ? 'bg-rose-50 text-rose-700'
                              : 'bg-amber-50 text-amber-700'
                        }`}
                      >
                        {entry.outcome === 'Success' ? 'موفق' : entry.outcome === 'Denied' ? 'رد دسترسی' : 'ناموفق'}
                      </span>
                    </td>
                    <td className="num text-[11px] text-slate-400" dir="ltr">
                      {entry.clientIpAddress ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {audit.data && (
          <div className="px-5 pb-4">
            <Pagination page={page} pageSize={pageSize} total={audit.data.totalCount} onChange={setPage} />
          </div>
        )}
      </div>
    </div>
  )
}