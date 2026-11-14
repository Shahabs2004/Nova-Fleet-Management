import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { Avatar } from '../components/Avatar'
import { api } from '../lib/api'
import { formatFaTime } from '../lib/date'
import type { NotificationDto, SettingsBundle } from '../lib/types'
import { toFa } from '../lib/date'

interface NavItem {
  to: string
  title: string
  permission: string
  icon: React.ReactNode
}

const icon = (d: string) => (
  <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
    <path d={d} />
  </svg>
)

const DEFAULT_ORGANIZATION_NAME = 'ناوگان نوین'

const ROLE_TITLES: Record<string, string> = {
  Administrator: 'مدیر سامانه',
  SystemAdministrator: 'مدیر سامانه',
  FleetManager: 'مدیر ناوگان',
  Operator: 'اپراتور',
  Requester: 'درخواست‌کننده',
  Viewer: 'ناظر',
}

const NAV_GROUPS: { title: string; items: NavItem[] }[] = [
  {
    title: 'عملیات',
    items: [
      { to: '/dashboard', title: 'داشبورد', permission: 'dashboard.view', icon: icon('M3 12h7V3H3v9Zm11 9h7v-9h-7v9ZM3 21h7v-6H3v6Zm11-12h7V3h-7v6Z') },
      { to: '/missions', title: 'مأموریت‌ها', permission: 'missions.view.own', icon: icon('M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2M9 5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2M9 5h6m-8 8h10M12 11v4') },
      { to: '/timeline', title: 'تایم‌لاین', permission: 'missions.timeline.view', icon: icon('M3 6h18M6 12h12M10 18h4') },
      { to: '/drivers', title: 'رانندگان', permission: 'drivers.view', icon: icon('M16 21v-2a4 4 0 0 0-8 0v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z') },
      { to: '/vehicles', title: 'خودروها', permission: 'vehicles.view', icon: icon('M5 17h14M7 17a2 2 0 1 1-4 0 2 2 0 0 1 4 0Zm14 0a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM3 13l1.5-5A2 2 0 0 1 6.4 6.5h11.2a2 2 0 0 1 1.9 1.5L21 13v4H3v-4Z') },
    ],
  },
  {
    title: 'گزارش‌ها',
    items: [
      { to: '/reports', title: 'گزارش‌ها', permission: 'reports.view', icon: icon('M3 3v18h18M7 15l4-6 4 3 5-8') },
      { to: '/sms', title: 'پیامک‌ها', permission: 'notifications.sms.view', icon: icon('M21 15a2 2 0 0 1-2 2H8l-5 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2v10Z') },
    ],
  },
  {
    title: 'مدیریت',
    items: [
      { to: '/security', title: 'امنیت و کاربران', permission: 'security.audit.view', icon: icon('M12 2 4 5v6c0 5 3.4 9.7 8 11 4.6-1.3 8-6 8-11V5l-8-3Z') },
      { to: '/settings', title: 'تنظیمات', permission: 'settings.system.manage', icon: icon('M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm7.4-3a7.4 7.4 0 0 0-.1-1.2l2-1.6-2-3.4-2.4 1a7.4 7.4 0 0 0-2-1.2L14.5 3h-5l-.4 2.6a7.4 7.4 0 0 0-2 1.2l-2.4-1-2 3.4 2 1.6a7.4 7.4 0 0 0 0 2.4l-2 1.6 2 3.4 2.4-1a7.4 7.4 0 0 0 2 1.2l.4 2.6h5l.4-2.6a7.4 7.4 0 0 0 2-1.2l2.4 1 2-3.4-2-1.6c.06-.4.1-.8.1-1.2Z') },
    ],
  },
]

function NotificationBell() {
  const [open, setOpen] = useState(false)
  const queryClient = useQueryClient()
  const { data } = useQuery({
    queryKey: ['notifications'],
    queryFn: () => api.get<{ items: NotificationDto[]; unreadCount: number }>('/api/notifications?pageSize=8'),
    refetchInterval: 30_000,
  })

  const markAll = async () => {
    await api.post('/api/notifications/read-all')
    await queryClient.invalidateQueries({ queryKey: ['notifications'] })
  }

  const unread = data?.unreadCount ?? 0
  return (
    <div className="relative">
      <button
        onClick={() => setOpen(!open)}
        className="relative rounded-xl p-2 text-slate-400 transition-colors hover:bg-emerald-50 hover:text-emerald-800"
        aria-label="اعلان‌ها"
      >
        {icon('M18 8a6 6 0 1 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 0 1-3.4 0')}
        {unread > 0 && (
          <span className="num absolute -top-0.5 -left-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-gold-500 px-1 text-[10px] font-bold text-emerald-950">
            {toFa(unread)}
          </span>
        )}
      </button>
      {open && (
        <div className="card absolute left-0 top-12 z-40 w-80 p-0">
          <div className="flex items-center justify-between border-b border-slate-100 px-4 py-3">
            <span className="text-sm font-bold text-emerald-950">اعلان‌ها</span>
            {unread > 0 && (
              <button onClick={markAll} className="text-xs font-medium text-emerald-700 hover:underline">
                خواندن همه
              </button>
            )}
          </div>
          <div className="max-h-80 overflow-y-auto">
            {(data?.items ?? []).length === 0 && <p className="px-4 py-6 text-center text-sm text-slate-400">اعلانی وجود ندارد.</p>}
            {(data?.items ?? []).map((n) => (
              <div key={n.id} className={`border-b border-slate-50 px-4 py-3 ${!n.readAtUtc ? 'bg-emerald-50/50' : ''}`}>
                <p className="text-sm font-semibold text-slate-700">{n.title}</p>
                <p className="mt-0.5 text-xs leading-5 text-slate-500">{n.message}</p>
                <p className="mt-1 text-[10px] text-slate-400">{formatFaTime(n.createdAtUtc)}</p>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

export function AppLayout() {
  const { user, logout, can } = useAuth()
  const navigate = useNavigate()
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const { data: settings } = useQuery({
    queryKey: ['settings'],
    queryFn: () => api.get<SettingsBundle>('/api/settings'),
  })
  const organizationName = settings?.organization.name?.trim() || DEFAULT_ORGANIZATION_NAME
  const organizationLogo = settings?.organization.logoDataUrl ?? null

  return (
    <div className="flex min-h-screen">
      {sidebarOpen && <button className="fixed inset-0 z-20 bg-emerald-950/30 lg:hidden" onClick={() => setSidebarOpen(false)} aria-label="بستن منو" />}
      <aside
        className={`fixed inset-y-0 right-0 z-30 flex w-64 flex-col bg-emerald-950 text-emerald-100 transition-transform lg:static lg:translate-x-0 ${
          sidebarOpen ? 'translate-x-0' : 'translate-x-full lg:translate-x-0'
        }`}
      >
        <div className="flex items-center gap-3 px-5 py-5">
          <div className="flex h-10 w-10 items-center justify-center overflow-hidden rounded-xl bg-emerald-900/60 ring-1 ring-gold-500/40">
            {organizationLogo ? (
              <img src={organizationLogo} alt={organizationName} className="h-full w-full object-contain" />
            ) : (
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#C8A44A" strokeWidth="1.8" strokeLinecap="round">
                <path d="M3 15h4l2-5h6l2 5h4" />
                <circle cx="7" cy="17" r="2" stroke="#2DD4BF" />
                <circle cx="17" cy="17" r="2" stroke="#2DD4BF" />
                <path d="M10 7h5" stroke="#2DD4BF" />
              </svg>
            )}
          </div>
          <div>
            <p className="text-sm font-extrabold text-white">{organizationName}</p>
            <p className="text-[10px] text-emerald-400">سامانه مدیریت ناوگان</p>
          </div>
        </div>
        <nav className="flex-1 space-y-5 overflow-y-auto px-3 pb-4">
          {NAV_GROUPS.map((group) => {
            const items = group.items.filter((item) => can(item.permission))
            if (items.length === 0) return null
            return (
              <div key={group.title}>
                <p className="mb-1.5 px-3 text-[10px] font-semibold tracking-wide text-emerald-500/70">{group.title}</p>
                <div className="space-y-1">
                  {items.map((item) => (
                    <NavLink
                      key={item.to}
                      to={item.to}
                      onClick={() => setSidebarOpen(false)}
                      className={({ isActive }) =>
                        `flex items-center gap-3 rounded-xl px-3 py-2.5 text-[13px] font-medium transition-colors ${
                          isActive
                            ? 'bg-emerald-800/60 text-white shadow-inner'
                            : 'text-emerald-200/80 hover:bg-emerald-900/50 hover:text-white'
                        }`
                      }
                    >
                      {item.icon}
                      {item.title}
                    </NavLink>
                  ))}
                </div>
              </div>
            )
          })}
        </nav>
        <div className="border-t border-emerald-900/60 px-5 py-3 text-center text-[10px] text-emerald-500/60">
          طراحی و توسعه — شهاب صادقی
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-10 border-b border-slate-200/70 bg-surface/90 backdrop-blur">
          <div className="flex items-center justify-between gap-4 px-4 py-3 lg:px-8">
            <button className="rounded-xl p-2 text-slate-500 hover:bg-emerald-50 lg:hidden" onClick={() => setSidebarOpen(true)} aria-label="منو">
              {icon('M4 6h16M4 12h16M4 18h16')}
            </button>
            <p className="hidden text-sm text-slate-400 lg:block">به سامانه {organizationName} خوش آمدید</p>
            <div className="flex items-center gap-2">
              <NotificationBell />
              <NavLink to="/profile" aria-label="مشاهده پروفایل من و تغییر تصویر" title="پروفایل من"
                className="avatar-link p-1 transition-colors hover:bg-emerald-50">
                <div className="mr-1 text-left">
                  <p className="text-[13px] font-bold text-emerald-950">{user?.displayName}</p>
                  <p className="text-[10px] text-slate-400">{ROLE_TITLES[user?.roles?.[0] ?? ''] ?? user?.roles?.[0] ?? ''}</p>
                </div>
                <Avatar name={user?.displayName ?? ''} src={user?.avatarDataUrl} size="sm" />
              </NavLink>
              <button
                onClick={async () => {
                  await logout()
                  navigate('/login')
                }}
                className="btn-ghost btn-sm"
              >
                خروج
              </button>
            </div>
          </div>
        </header>
        <main className="flex-1 px-4 py-6 lg:px-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
