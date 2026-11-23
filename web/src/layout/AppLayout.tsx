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
  permission: string | string[]
  icon: React.ReactNode
}

const icon = (d: string) => (
  <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
    <path d={d} />
  </svg>
)

const menuIcon = (name: string) => (
  <img src={`/icons/menu/${name}.png`} alt="" aria-hidden="true" className="h-8 w-8 shrink-0 object-contain" />
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
      { to: '/dashboard', title: 'داشبورد', permission: 'dashboard.view', icon: menuIcon('dashboard') },
      { to: '/timeline', title: 'تایم‌لاین', permission: 'missions.timeline.view', icon: menuIcon('timeline') },
      { to: '/missions', title: 'مأموریت‌ها', permission: 'missions.view.own', icon: menuIcon('missions') },
      { to: '/drivers', title: 'رانندگان', permission: 'drivers.view', icon: menuIcon('drivers') },
      { to: '/vehicles', title: 'خودروها', permission: 'vehicles.view', icon: menuIcon('vehicles') },
      { to: '/maintenance', title: 'سرویس و نگهداری', permission: 'maintenance.view', icon: menuIcon('maintenance') },
    ],
  },
  {
    title: 'گزارش‌ها',
    items: [
      { to: '/reports', title: 'گزارش‌ها', permission: 'reports.view', icon: menuIcon('reports') },
      { to: '/sms', title: 'پیامک‌ها', permission: 'notifications.sms.view', icon: menuIcon('sms') },
    ],
  },
  {
    title: 'مدیریت',
    items: [
      { to: '/security', title: 'امنیت و کاربران', permission: ['security.audit.view', 'security.sessions.view', 'users.manage', 'settings.authorization'], icon: menuIcon('security') },
      { to: '/settings', title: 'تنظیمات', permission: 'settings.system.manage', icon: menuIcon('settings') },
    ],
  },
]

function NotificationBell() {
  const { can } = useAuth()
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
                {n.referenceType === 'maintenance' && n.referenceId && can('maintenance.view') && <NavLink className="text-xs text-emerald-700 underline" to={`/maintenance/vehicles/${encodeURIComponent(n.referenceId)}`} onClick={() => setOpen(false)}>دفترچهٔ خودرو</NavLink>}
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
  const [labelsOpen, setLabelsOpen] = useState(false)
  const { data: settings } = useQuery({
    queryKey: ['settings'],
    queryFn: () => api.get<SettingsBundle>('/api/settings'),
  })
  const organizationName = settings?.organization.name?.trim() || DEFAULT_ORGANIZATION_NAME
  const organizationLogo = settings?.organization.logoDataUrl ?? null

  return (
    <div className="flex min-h-screen">
      {labelsOpen && <button className="fixed inset-0 z-20 bg-emerald-950/30 lg:hidden" onClick={() => setLabelsOpen(false)} aria-label="بستن منو" />}
      <aside
        className={`fixed inset-y-0 right-0 z-30 flex flex-col bg-emerald-950 text-emerald-100 transition-[width] duration-200 lg:static ${
          labelsOpen ? 'w-64 shadow-2xl lg:shadow-none' : 'w-20'
        }`}
        aria-label="منوی اصلی"
      >
        <div className={`flex items-center gap-3 py-5 ${labelsOpen ? 'px-5' : 'justify-center px-2'}`}>
          <div className="flex h-10 w-10 shrink-0 items-center justify-center overflow-hidden rounded-xl bg-emerald-900/60 ring-1 ring-gold-500/40">
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
          {labelsOpen && (
            <div className="min-w-0">
              <p className="truncate text-sm font-extrabold text-white">{organizationName}</p>
              <p className="text-[10px] text-emerald-400">سامانه مدیریت ناوگان</p>
            </div>
          )}
        </div>
        <nav className={`sidebar-nav flex-1 space-y-4 overflow-y-auto pb-4 ${labelsOpen ? 'px-3' : 'px-2'}`}>
          {NAV_GROUPS.map((group) => {
            const items = group.items.filter((item) =>
              (Array.isArray(item.permission) ? item.permission : [item.permission]).some((permission) => can(permission)),
            )
            if (items.length === 0) return null
            return (
              <div key={group.title}>
                {labelsOpen
                  ? <p className="mb-1.5 px-3 text-[11px] font-semibold tracking-wide text-emerald-500/70">{group.title}</p>
                  : <div className="mx-3 mb-2 border-t border-emerald-900/60" aria-hidden="true" />}
                <div className="space-y-1">
                  {items.map((item) => (
                    <NavLink
                      key={item.to}
                      to={item.to}
                      title={labelsOpen ? undefined : item.title}
                      aria-label={item.title}
                      onClick={() => { if (!window.matchMedia('(min-width: 1024px)').matches) setLabelsOpen(false) }}
                      className={({ isActive }) =>
                        `flex items-center rounded-xl py-2 text-[15px] font-medium transition-colors ${
                          labelsOpen ? 'gap-3 px-3' : 'justify-center px-0'
                        } ${
                          isActive
                            ? 'bg-emerald-800/60 text-white shadow-inner'
                            : 'text-emerald-200/80 hover:bg-emerald-900/50 hover:text-white'
                        }`
                      }
                    >
                      {item.icon}
                      {labelsOpen && <span className="truncate">{item.title}</span>}
                    </NavLink>
                  ))}
                </div>
              </div>
            )
          })}
        </nav>
        <div className="border-t border-emerald-900/60 px-2 py-2">
          <button
            onClick={() => setLabelsOpen(v => !v)}
            aria-expanded={labelsOpen}
            aria-label={labelsOpen ? 'نمایش فقط آیکن‌ها' : 'نمایش متن منوها'}
            title={labelsOpen ? 'نمایش فقط آیکن‌ها' : 'نمایش متن منوها'}
            className={`flex w-full items-center rounded-xl py-2 text-emerald-300/80 transition-colors hover:bg-emerald-900/50 hover:text-white ${labelsOpen ? 'justify-between px-3' : 'justify-center px-0'}`}
          >
            {labelsOpen && <span className="text-[13px] font-medium">جمع‌کردن منو</span>}
            {icon(labelsOpen ? 'M9 18l6-6-6-6' : 'M4 6h16M4 12h16M4 18h16')}
          </button>
          {labelsOpen && (
            <p className="mt-1 px-3 pb-1 text-center text-[10px] text-emerald-500/60">طراحی و توسعه — شهاب صادقی</p>
          )}
        </div>
      </aside>

      <div className="mr-20 flex min-w-0 flex-1 flex-col lg:mr-0">
        <header className="sticky top-0 z-10 border-b border-slate-200/70 bg-surface/90 backdrop-blur">
          <div className="flex items-center justify-between gap-4 px-4 py-3 lg:px-8">
            <button className="rounded-xl p-2 text-slate-500 hover:bg-emerald-50 lg:hidden" onClick={() => setLabelsOpen(v => !v)} aria-expanded={labelsOpen} aria-label={labelsOpen ? 'بستن منو' : 'نمایش متن منوها'}>
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
