import { cleanup, render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAppRoutes } from './AppRoutes'

const loaded = vi.hoisted(() => ({ pages: [] as string[] }))

vi.mock('../auth/useAuth', () => ({
  useAuth: () => ({ user: { username: 'route.user' }, loading: false }),
}))

vi.mock('../layout/AppLayout', async () => {
  const { Outlet } = await import('react-router-dom')
  return {
    AppLayout: () => <div data-testid="app-layout"><h1>چیدمان سامانه</h1><Outlet /></div>,
  }
})

vi.mock('../pages/MissionDetailsPage', () => {
  loaded.pages.push('mission-details')
  return { MissionDetailsPage: () => <h1>جزئیات مأموریت</h1> }
})

vi.mock('../pages/DriversPage', () => {
  loaded.pages.push('drivers')
  return { DriversPage: () => <h1>رانندگان</h1> }
})

afterEach(() => cleanup())

describe('lazy application routes', () => {
  it('keeps page imports deferred and renders a direct protected route', async () => {
    expect(loaded.pages).toEqual([])
    const router = createMemoryRouter(createAppRoutes(), {
      initialEntries: ['/missions/mission-1'],
    })

    render(<RouterProvider router={router} />)

    expect(screen.getByRole('status')).toHaveTextContent('در حال بارگذاری صفحه…')
    expect(await screen.findByRole('heading', { name: 'جزئیات مأموریت' })).toBeInTheDocument()
    expect(loaded.pages).toEqual(['mission-details'])
  })
})
