import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider, createBrowserRouter, Navigate } from 'react-router-dom'
import './index.css'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { AppLayout } from './layout/AppLayout'
import { LoginPage } from './pages/LoginPage'
import { SetupPage } from './pages/SetupPage'
import { DashboardPage } from './pages/DashboardPage'
import { MissionsPage } from './pages/MissionsPage'
import { MissionDetailsPage } from './pages/MissionDetailsPage'
import { DriversPage } from './pages/DriversPage'
import { VehiclesPage } from './pages/VehiclesPage'
import { ReportsPage } from './pages/ReportsPage'
import { SmsPage } from './pages/SmsPage'
import { SecurityPage } from './pages/SecurityPage'
import { ProfilePage } from './pages/ProfilePage'
import { SettingsPage } from './pages/SettingsPage'
import { TimelinePage } from './pages/TimelinePage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { retry: 1, refetchOnWindowFocus: false, staleTime: 30_000 },
  },
})

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth()
  if (loading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-surface">
        <div className="flex flex-col items-center gap-4">
          <svg className="h-9 w-9 animate-spin text-emerald-800" viewBox="0 0 24 24" fill="none">
            <circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" className="opacity-20" />
            <path d="M12 2a10 10 0 0 1 10 10" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
          </svg>
          <p className="text-sm text-slate-400">در حال بارگذاری سامانه…</p>
        </div>
      </div>
    )
  }
  return user ? <>{children}</> : <Navigate to="/login" replace />
}

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '/setup', element: <SetupPage /> },
  {
    path: '/',
    element: (
      <RequireAuth>
        <AppLayout />
      </RequireAuth>
    ),
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: 'dashboard', element: <DashboardPage /> },
      { path: 'missions', element: <MissionsPage /> },
      { path: 'missions/:id', element: <MissionDetailsPage /> },
      { path: 'timeline', element: <TimelinePage /> },
      { path: 'drivers', element: <DriversPage /> },
      { path: 'vehicles', element: <VehiclesPage /> },
      { path: 'reports', element: <ReportsPage /> },
      { path: 'sms', element: <SmsPage /> },
      { path: 'security', element: <SecurityPage /> },
      { path: 'profile', element: <ProfilePage /> },
      { path: 'settings', element: <SettingsPage /> },
    ],
  },
])

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
