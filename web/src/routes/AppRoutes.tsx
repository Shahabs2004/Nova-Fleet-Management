import { Suspense, type ReactNode } from 'react'
import { Navigate, type RouteObject } from 'react-router-dom'
import { AppLayout } from '../layout/AppLayout'
import { lazyPages } from './lazyPages'
import { RequireAuth } from './RequireAuth'

const page = (children: ReactNode) => (
  <Suspense fallback={<div role="status" className="p-6 text-sm text-slate-500">در حال بارگذاری صفحه…</div>}>
    {children}
  </Suspense>
)

export function createAppRoutes(): RouteObject[] {
  return [
    { path: '/login', element: page(<lazyPages.login />) },
    { path: '/setup', element: page(<lazyPages.setup />) },
    {
      path: '/',
      element: <RequireAuth><AppLayout /></RequireAuth>,
      children: [
        { index: true, element: <Navigate to="/dashboard" replace /> },
        { path: 'dashboard', element: page(<lazyPages.dashboard />) },
        { path: 'missions', element: page(<lazyPages.missions />) },
        { path: 'missions/:id', element: page(<lazyPages.missionDetails />) },
        { path: 'timeline', element: page(<lazyPages.timeline />) },
        { path: 'drivers', element: page(<lazyPages.drivers />) },
        { path: 'vehicles', element: page(<lazyPages.vehicles />) },
        { path: 'maintenance', element: page(<lazyPages.maintenanceFleet />) },
        { path: 'maintenance/vehicles/:id', element: page(<lazyPages.maintenanceVehicle />) },
        { path: 'reports', element: page(<lazyPages.reports />) },
        { path: 'sms', element: page(<lazyPages.sms />) },
        { path: 'security', element: page(<lazyPages.security />) },
        { path: 'profile', element: page(<lazyPages.profile />) },
        { path: 'settings', element: page(<lazyPages.settings />) },
      ],
    },
  ]
}
