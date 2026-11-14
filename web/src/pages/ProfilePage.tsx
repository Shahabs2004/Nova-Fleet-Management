import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { AvatarEditor } from '../components/AvatarEditor'
import { PageHeader, Spinner } from '../components/ui'
import { api } from '../lib/api'
import type { ProfileDetails } from '../lib/types'

export function ProfilePage() {
  const { user } = useAuth()
  const profile = useQuery({
    queryKey: ['profile', user?.username],
    queryFn: () => api.get<ProfileDetails>('/api/profile'),
    staleTime: 0,
    refetchOnMount: 'always',
  })
  const [loaded, setLoaded] = useState(false)
  // Keep the editor mounted on later refetch errors without hydrating stale data on entry.
  if (!loaded && profile.isSuccess && profile.isFetchedAfterMount) setLoaded(true)

  return (
    <div>
      <PageHeader title="پروفایل من" subtitle="مشاهده اطلاعات شخصی و مدیریت تصویر پروفایل" />
      {profile.isError && (
        <div role="alert" className="card mb-4 p-4 text-sm text-rose-700">
          <p>دریافت پروفایل ناموفق بود: {profile.error.message}</p>
          <button type="button" className="btn-ghost btn-sm mt-2" disabled={profile.isFetching} onClick={() => void profile.refetch()}>تلاش دوباره</button>
        </div>
      )}
      {(!profile.data || !loaded) && !profile.isError && <Spinner />}
      {profile.data && loaded && (
        <ProfileForm key={user?.username} profile={profile.data} loading={profile.isFetching || profile.isError} />
      )}
    </div>
  )
}

function ProfileForm({ profile, loading }: { profile: ProfileDetails; loading: boolean }) {
  const { user, refresh } = useAuth()
  const queryClient = useQueryClient()
  const [avatarDataUrl, setAvatarDataUrl] = useState<string | null>(profile.avatarDataUrl)
  const [savedAvatar, setSavedAvatar] = useState<string | null>(profile.avatarDataUrl)
  const [editing, setEditing] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const saving = useRef(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const blocked = loading || submitting || editing || avatarDataUrl === savedAvatar
  const fields = [
    ['نام کاربری', profile.username],
    ['نام', profile.firstName],
    ['نام خانوادگی', profile.lastName],
    ['موبایل', profile.mobilePhone],
    ['ایمیل', profile.email],
    ['واحد سازمانی', profile.organizationalUnit],
  ]

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    if (blocked || saving.current) return
    saving.current = true
    setSubmitting(true)
    setError(null)
    setNotice(null)
    try {
      const saved = await api.put<ProfileDetails>('/api/profile', { avatarDataUrl })
      setAvatarDataUrl(saved.avatarDataUrl)
      setSavedAvatar(saved.avatarDataUrl)
      queryClient.setQueryData(['profile', user?.username], saved)
      await Promise.all([
        refresh(),
        queryClient.invalidateQueries({ queryKey: ['users'] }),
        queryClient.invalidateQueries({ queryKey: ['profile'] }),
      ])
      setNotice(saved.avatarDataUrl ? 'تصویر پروفایل با موفقیت ذخیره شد.' : 'تصویر پروفایل با موفقیت حذف شد.')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'ذخیره تصویر پروفایل ناموفق بود.')
    } finally {
      saving.current = false
      setSubmitting(false)
    }
  }

  return (
    <div className="card max-w-3xl space-y-6 p-5 sm:p-6">
      <section aria-labelledby="profile-information">
        <h2 id="profile-information" className="mb-4 text-sm font-bold text-emerald-950">اطلاعات شخصی</h2>
        <dl className="grid gap-4 sm:grid-cols-2">
          {fields.map(([label, value]) => (
            <div key={label}>
              <dt className="label">{label}</dt>
              <dd className="break-words text-sm text-slate-700" dir="auto">{value || '—'}</dd>
            </div>
          ))}
        </dl>
        <p className="mt-4 text-xs leading-6 text-slate-500">اطلاعات شخصی فقط قابل مشاهده است. برای تغییر آن با مدیر سامانه تماس بگیرید.</p>
      </section>
      <form className="space-y-4 border-t border-slate-100 pt-5" onSubmit={submit}>
        <h2 className="text-sm font-bold text-emerald-950">تصویر پروفایل</h2>
        <AvatarEditor name={`${profile.firstName} ${profile.lastName}`.trim() || profile.username}
          value={avatarDataUrl} onChange={(value) => { setAvatarDataUrl(value); setNotice(null); setError(null) }}
          onEditingChange={setEditing} disabled={submitting || loading} />
        <p className="text-xs leading-6 text-slate-500">پس از تأیید برش یا حذف تصویر، تغییرات را ذخیره کنید.</p>
        {error && <p role="alert" className="rounded-xl bg-rose-50 p-3 text-sm text-rose-700">{error}</p>}
        {notice && <p role="status" className="rounded-xl bg-emerald-50 p-3 text-sm text-emerald-800">{notice}</p>}
        <div className="flex justify-end">
          <button type="submit" className="btn-primary" disabled={blocked}>{submitting ? 'در حال ذخیره…' : 'ذخیره تصویر'}</button>
        </div>
      </form>
    </div>
  )
}
