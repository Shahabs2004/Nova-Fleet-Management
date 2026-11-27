import { act, cleanup, render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TimelinePage } from './TimelinePage'

vi.mock('../auth/useAuth', () => ({
  useAuth: () => ({ can: () => true, user: { username: 'timeline.user', displayName: 'آزمون' } }),
}))

vi.mock('react-router-dom', async (importOriginal) => ({
  ...await importOriginal<typeof import('react-router-dom')>(),
  useNavigate: () => vi.fn(),
}))

vi.mock('@tanstack/react-query', () => {
  const empty: unknown[] = []
  const mission = {
    id: '11111111-1111-1111-1111-111111111111',
    driverId: '22222222-2222-2222-2222-222222222222',
    driverName: 'راننده آزمون',
    title: 'مأموریت آزمون',
    startAtUtc: '2026-11-27T06:00:00.000Z',
    endAtUtc: '2026-11-27T08:00:00.000Z',
    status: 1,
  }
  return {
    useQuery: ({ queryKey }: { queryKey: unknown[] }) => {
      if (queryKey[0] === 'timeline') return { data: [mission], isLoading: false, isError: false, error: null }
      if (queryKey[0] === 'driver-options' || queryKey[0] === 'vehicle-options') return { data: empty }
      return { data: null, isLoading: false, isError: false, error: null }
    },
    useQueryClient: () => ({ invalidateQueries: vi.fn() }),
  }
})

describe('timeline current-time marker', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-27T06:00:00.000Z'))
    vi.stubGlobal('ResizeObserver', class {
      private readonly callback: ResizeObserverCallback
      constructor(callback: ResizeObserverCallback) { this.callback = callback }
      observe() {
        this.callback([{ contentRect: { width: 1000 } } as ResizeObserverEntry], this as unknown as ResizeObserver)
      }
      disconnect() {}
      unobserve() {}
    })
  })

  afterEach(() => {
    cleanup()
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('advances the marker without waiting for an unrelated render', () => {
    const { container } = render(<MemoryRouter><TimelinePage /></MemoryRouter>)
    const marker = container.querySelector<HTMLElement>('.border-dashed')
    expect(marker).not.toBeNull()
    const initialRight = marker!.style.right

    act(() => vi.advanceTimersByTime(60_000))

    expect(container.querySelector<HTMLElement>('.border-dashed')!.style.right).not.toBe(initialRight)
  })
})
