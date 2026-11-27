import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useEffect, type ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from './AuthContext'
import { useAuth } from './useAuth'
import { ConnectionsSettingsTab } from '../components/ConnectionsSettings'
import { MissionForm } from '../components/MissionForm'
import { UserProfileForm } from '../components/UserProfileForm'
import { MaintenanceReports } from '../components/maintenance/MaintenanceReports'
import { OdometerPanel } from '../components/maintenance/OdometerPanel'
import { RecordDetails } from '../components/maintenance/RecordDetails'
import { SecurityPage } from '../pages/SecurityPage'
import type { CurrentUser, UserDetails } from '../lib/types'

const firstUser: CurrentUser = {
  avatarDataUrl: null,
  username: 'first.user',
  displayName: 'کاربر اول',
  organizationalUnit: null,
  isLocalAdmin: false,
  roles: [],
  permissions: [],
}

const secondUser: CurrentUser = { ...firstUser, username: 'second.user', displayName: 'کاربر دوم' }
const administrator: CurrentUser = {
  ...firstUser,
  isLocalAdmin: true,
  permissions: ['users.manage', 'settings.authorization', 'security.sessions.view', 'security.sessions.manage', 'security.audit.view'],
}

let auth: ReturnType<typeof useAuth>

function Probe() {
  const context = useAuth()
  const { user, loading } = context
  useEffect(() => {
    auth = context
  }, [context])
  return (
    <div>
      <span data-testid="user">{user?.username ?? 'anonymous'}</span>
      <span data-testid="loading">{String(loading)}</span>
    </div>
  )
}

function Authenticated({ children }: { children: ReactNode }) {
  const { user } = useAuth()
  return user ? <>{children}</> : null
}

function response(status: number, body?: unknown): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function setup(children?: ReactNode, currentUser = firstUser) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const order: string[] = []
  const cancelQueries = queryClient.cancelQueries.bind(queryClient)
  const clear = queryClient.clear.bind(queryClient)
  vi.spyOn(queryClient, 'cancelQueries').mockImplementation(async (...args) => {
    order.push('cancel')
    await cancelQueries(...args)
  })
  vi.spyOn(queryClient, 'clear').mockImplementation(() => {
    order.push('clear')
    clear()
  })
  queryClient.setQueryData(['notifications', currentUser.username], ['private'])
  queryClient.setQueryData(['dashboard', currentUser.username], { count: 3 })
  render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <MemoryRouter>
          <Probe />
          <Authenticated>{children}</Authenticated>
        </MemoryRouter>
      </AuthProvider>
    </QueryClientProvider>,
  )
  return { queryClient, order }
}

function queryKeys(queryClient: QueryClient) {
  return queryClient.getQueryCache().getAll().map((query) => query.queryKey)
}

async function waitForUser(username: string) {
  await waitFor(() => expect(screen.getByTestId('user')).toHaveTextContent(username))
}

function expectOnlyRequests(...paths: string[]) {
  expect(fetch).toHaveBeenCalledTimes(paths.length)
  expect(vi.mocked(fetch).mock.calls.map(([input]) => String(input))).toEqual(paths)
}

describe('AuthContext cache isolation', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it.each([
    ['a network error', () => Promise.reject(new TypeError('network unavailable'))],
    ['HTTP 403', () => Promise.resolve(response(403, { message: 'forbidden' }))],
    ['HTTP 500', () => Promise.resolve(response(500, { message: 'temporary failure' }))],
  ])('preserves the authenticated user when /me has %s', async (_label, failure) => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, firstUser))
    const { queryClient, order } = setup()
    await waitForUser(firstUser.username)

    vi.mocked(fetch).mockImplementationOnce(failure)
    await act(async () => { await auth.refresh() })

    expect(screen.getByTestId('user')).toHaveTextContent(firstUser.username)
    expect(screen.getByTestId('loading')).toHaveTextContent('false')
    expect(queryClient.getQueryData(['notifications', firstUser.username])).toEqual(['private'])
    expect(queryClient.getQueryData(['dashboard', firstUser.username])).toEqual({ count: 3 })
    expect(order).toEqual([])
  })

  it('clears the user and cancels then clears private cache on HTTP 401', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, firstUser))
    const { queryClient, order } = setup()
    await waitForUser(firstUser.username)

    vi.mocked(fetch).mockResolvedValueOnce(response(401, { message: 'expired' }))
    await act(async () => { await auth.refresh() })

    await waitForUser('anonymous')
    expect(order).toEqual(['cancel', 'clear'])
    expect(queryClient.getQueryCache().getAll()).toEqual([])
  })

  it('cancels then clears legacy and scoped private cache when refresh adopts another username', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, firstUser))
    const { queryClient, order } = setup()
    await waitForUser(firstUser.username)
    queryClient.setQueryData(['drivers', 'page=1'], ['legacy-private'])
    vi.mocked(fetch).mockClear()
    vi.mocked(fetch).mockResolvedValueOnce(response(200, secondUser))

    await act(async () => { await auth.refresh() })

    expectOnlyRequests('/api/auth/me')
    expect(screen.getByTestId('user')).toHaveTextContent(secondUser.username)
    expect(order).toEqual(['cancel', 'clear'])
    expect(queryClient.getQueryCache().getAll()).toEqual([])
  })

  it('preserves private cache when refresh returns the same username', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, firstUser))
    const { queryClient, order } = setup()
    await waitForUser(firstUser.username)
    vi.mocked(fetch).mockClear()
    vi.mocked(fetch).mockResolvedValueOnce(response(200, { ...firstUser, displayName: 'کاربر به‌روزشده' }))

    await act(async () => { await auth.refresh() })

    expectOnlyRequests('/api/auth/me')
    expect(screen.getByTestId('user')).toHaveTextContent(firstUser.username)
    expect(order).toEqual([])
    expect(queryClient.getQueryData(['notifications', firstUser.username])).toEqual(['private'])
    expect(queryClient.getQueryData(['dashboard', firstUser.username])).toEqual({ count: 3 })
    expect(queryClient.getQueryCache().getAll()).toHaveLength(2)
  })

  it('cancels then clears the cache when logging out', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, firstUser))
    const { queryClient, order } = setup()
    await waitForUser(firstUser.username)
    vi.mocked(fetch).mockClear()
    vi.mocked(fetch).mockResolvedValueOnce(response(204))

    await act(async () => { await auth.logout() })

    expectOnlyRequests('/api/auth/logout')
    expect(screen.getByTestId('user')).toHaveTextContent('anonymous')
    expect(order).toEqual(['cancel', 'clear'])
    expect(queryClient.getQueryCache().getAll()).toEqual([])
  })

  it('cancels then clears the cache when switching to another username', async () => {
    vi.mocked(fetch).mockResolvedValueOnce(response(200, firstUser))
    const { queryClient, order } = setup()
    await waitForUser(firstUser.username)
    vi.mocked(fetch).mockClear()
    vi.mocked(fetch).mockResolvedValueOnce(response(200, secondUser))

    await act(async () => { await auth.loginLocal(secondUser.username, 'test-password') })

    expectOnlyRequests('/api/auth/login-local')
    expect(screen.getByTestId('user')).toHaveTextContent(secondUser.username)
    expect(order).toEqual(['cancel', 'clear'])
    expect(queryClient.getQueryCache().getAll()).toEqual([])
  })
})

function mockPrivateRoutes(routes: Record<string, unknown>, currentUser = firstUser) {
  vi.mocked(fetch).mockImplementation(async (input) => {
    const path = String(input)
    const pathname = path.split('?')[0]
    if (pathname === '/api/auth/me') return response(200, currentUser)
    if (!(pathname in routes)) throw new Error(`Unexpected request: ${path}`)
    return response(200, routes[pathname])
  })
}

const profile = {
  username: firstUser.username,
  firstName: 'کاربر',
  lastName: 'اول',
  mobilePhone: '09120000000',
  email: null,
  organizationalUnit: null,
  avatarDataUrl: null,
}

const targetUser: UserDetails = {
  ...profile,
  id: 'target-user',
  username: 'target.user',
  isDisabled: false,
  isDirectoryUser: false,
  isLocalAdmin: false,
  roleIds: [],
}

const emptyPage = (pageSize: number) => ({ items: [], totalCount: 0, page: 1, pageSize })

describe('private query key scoping', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('scopes mission-form profile and approved-location reads by username', async () => {
    mockPrivateRoutes({
      '/api/profile': profile,
      '/api/locations': [],
    })
    const { queryClient } = setup(<MissionForm onSubmit={vi.fn()} submitting={false} error={null} />)
    await waitForUser(firstUser.username)

    await waitFor(() => expect(queryKeys(queryClient)).toEqual(expect.arrayContaining([
      ['approved-locations', firstUser.username],
      ['profile', firstUser.username],
    ])))
  })

  it('scopes user-form details, roles, and organization units by username', async () => {
    mockPrivateRoutes({
      '/api/users/target-user': targetUser,
      '/api/roles': [],
      '/api/organization-units': [],
    })
    const { queryClient } = setup(
      <UserProfileForm userId="target-user" onDone={vi.fn().mockResolvedValue(undefined)} onSavingChange={vi.fn()} />,
    )
    await waitForUser(firstUser.username)

    await waitFor(() => expect(queryKeys(queryClient)).toEqual(expect.arrayContaining([
      ['users', firstUser.username, 'details', 'target-user'],
      ['roles', firstUser.username],
      ['org-units', firstUser.username],
    ])))
  })

  it('scopes every connection settings read by username', async () => {
    mockPrivateRoutes({
      '/api/settings/connections/database': { connectionString: null, isConfigured: false, source: 'test' },
      '/api/settings/connections/directory': {
        enabled: false, host: '', port: 636, useSsl: true, domain: '', baseDn: '', bindDn: '',
        timeoutSeconds: 15, searchFilter: '(&(objectClass=user)(sAMAccountName={0}))', hasBindPassword: false,
      },
      '/api/settings/connections/sms': {
        enabled: false, mode: 'Simulation', endpoint: null, organization: null, username: null,
        senderNumber: null, timeoutSeconds: 20, retryMinutes: 5, maxAttempts: 5, hasPassword: false,
      },
    })
    const { queryClient } = setup(<ConnectionsSettingsTab />)
    await waitForUser(firstUser.username)

    await waitFor(() => expect(queryKeys(queryClient)).toEqual(expect.arrayContaining([
      ['connections-database', firstUser.username],
      ['connections-directory', firstUser.username],
      ['connections-sms', firstUser.username],
    ])))
  })

  it('scopes maintenance report, odometer, record, and revision reads by username', async () => {
    mockPrivateRoutes({
      '/api/maintenance/vehicles/vehicle-1/report': {
        fromUtc: '2025-01-01T00:00:00Z', toUtc: '2026-01-01T00:00:00Z', totalCostToman: 0,
        distanceKm: null, costPerKmToman: null, distanceFromUtc: null, distanceToUtc: null,
        distanceCostToman: null, months: [], categories: [],
      },
      '/api/maintenance/vehicles/vehicle-1/odometer': {
        currentKm: null, observedAtUtc: null, isTimeEstimated: false, hasConflicts: false, readings: [],
      },
      '/api/maintenance/vehicles/vehicle-1/records/record-1': {
        id: 'record-1', vehicleId: 'vehicle-1', performedAtUtc: '2026-01-01T00:00:00Z', odometerKm: null,
        workshop: null, notes: null, otherCostToman: 0, totalCostToman: 0, isVoided: false,
        createdBy: firstUser.username, version: 1, items: [], attachments: [],
      },
      '/api/maintenance/vehicles/vehicle-1/revisions/record-1': [],
    })
    const onSaved = vi.fn().mockResolvedValue(undefined)
    const { queryClient } = setup(
      <>
        <MaintenanceReports vehicleId="vehicle-1" />
        <OdometerPanel vehicleId="vehicle-1" archived={false} onSaved={onSaved} onRecord={vi.fn()} />
        <RecordDetails vehicleId="vehicle-1" id="record-1" onClose={vi.fn()} onEdit={vi.fn()} onSaved={onSaved} />
      </>,
    )
    await waitForUser(firstUser.username)

    await waitFor(() => expect(queryKeys(queryClient)).toEqual(expect.arrayContaining([
      ['maintenance', firstUser.username, 'vehicle-1', 'report', { from: expect.any(String), to: expect.any(String) }],
      ['maintenance', firstUser.username, 'vehicle-1', 'odometer'],
      ['maintenance', firstUser.username, 'vehicle-1', 'record', 'record-1'],
      ['maintenance', firstUser.username, 'vehicle-1', 'revisions', 'record-1'],
    ])))
  })

  it('scopes users, roles, sessions, audit, and authorization lookups by username', async () => {
    mockPrivateRoutes({
      '/api/users': [],
      '/api/roles': [],
      '/api/permissions': [],
      '/api/roles/default-role': { roleName: '' },
      '/api/roles/directory-mappings': [],
      '/api/roles/directory-groups': { success: true, error: null, groups: [], hitSearchLimit: false },
      '/api/sessions': emptyPage(20),
      '/api/audit/actions': [],
      '/api/audit': emptyPage(25),
    }, administrator)
    const { queryClient } = setup(<SecurityPage />, administrator)
    await waitForUser(administrator.username)
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/users?'), expect.anything()))

    fireEvent.click(screen.getByRole('button', { name: 'نقش‌ها و مجوزها' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/roles/directory-groups', expect.anything()))
    fireEvent.click(screen.getByRole('button', { name: 'نشست‌ها' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/sessions?'), expect.anything()))
    fireEvent.click(screen.getByRole('button', { name: 'ممیزی' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringContaining('/api/audit?'), expect.anything()))

    expect(queryKeys(queryClient)).toEqual(expect.arrayContaining([
      ['users', administrator.username, '', true],
      ['roles', administrator.username],
      ['permissions', administrator.username],
      ['default-role', administrator.username],
      ['directory-mappings', administrator.username],
      ['directory-groups', administrator.username],
      ['sessions', administrator.username, '', '', 1],
      ['audit-actions', administrator.username],
      ['audit', administrator.username, '', '', '', 1],
    ]))
  })
})
