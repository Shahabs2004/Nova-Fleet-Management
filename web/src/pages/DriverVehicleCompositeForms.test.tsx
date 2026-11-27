import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DriversPage } from './DriversPage'
import { VehiclesPage } from './VehiclesPage'

vi.mock('../auth/useAuth', () => ({
  useAuth: () => ({ can: () => true }),
}))

function response(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function setup(page: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(<QueryClientProvider client={client}><MemoryRouter>{page}</MemoryRouter></QueryClientProvider>)
}

const driver = {
  id: 'driver-1', firstName: 'رضا', lastName: 'محمدی', displayName: 'رضا محمدی',
  mobilePhone: '09121112233', employmentStatus: 1, defaultVehicleId: null,
  defaultVehiclePlate: null, isArchived: false, createdAtUtc: '2026-11-20T00:00:00Z', rowVersion: 'driver-v1',
}
const driverDetail = { ...driver, avatarDataUrl: 'data:image/png;base64,YQ==' }
const vehicle = {
  id: 'vehicle-1', brand: 'ایویکو', model: 'دیلی', displayName: 'ایویکو دیلی',
  licensePlate: '12ب34567', plateDisplay: '12 ب 345 - 67', plateFirstTwo: '12', plateLetter: 'ب',
  plateNextThree: '345', plateIranCode: '67', manualStatus: 1, effectiveStatus: 1,
  isArchived: false, createdAtUtc: '2026-11-20T00:00:00Z', rowVersion: 'vehicle-v1',
  driverId: null, driverDisplayName: null,
}
const pageResult = (item: unknown) => ({ items: [item], totalCount: 1, page: 1, pageSize: 12 })

function writes() {
  return vi.mocked(fetch).mock.calls.filter(([, init]) => init?.method && init.method !== 'GET')
}

function field(label: string): HTMLElement {
  return screen.getByText(label, { selector: 'label' }).nextElementSibling as HTMLElement
}

describe('driver and vehicle composite forms', () => {
  beforeEach(() => { vi.stubGlobal('fetch', vi.fn()) })
  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('loads driver detail without an avatar and submits one composite update', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const path = String(input)
      if (path === '/api/drivers/driver-1') return response(driverDetail)
      if (path.startsWith('/api/drivers?')) return response(pageResult(driver))
      if (path === '/api/drivers/options') return response([])
      if (path === '/api/vehicles/options') return response([vehicle])
      if (path === '/api/drivers/driver-1/composite') return response({ ...driverDetail, firstName: 'علی', defaultVehicleId: vehicle.id })
      return response(pageResult({ ...driver, firstName: 'علی', defaultVehicleId: vehicle.id }))
    })
    setup(<DriversPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'ویرایش' }))
    await screen.findByDisplayValue('رضا')
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/drivers/driver-1', expect.anything()))
    fireEvent.change(field('نام'), { target: { value: 'علی' } })
    fireEvent.change(field('خودروی پیش‌فرض'), { target: { value: vehicle.id } })
    fireEvent.click(screen.getByRole('button', { name: 'ذخیره' }))

    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0][0]).toBe('/api/drivers/driver-1/composite')
    expect(JSON.parse(String(writes()[0][1]?.body))).toMatchObject({
      profile: { firstName: 'علی', rowVersion: 'driver-v1', avatarDataUrl: driverDetail.avatarDataUrl },
      defaultVehicleId: vehicle.id,
    })
  })

  it('creates a driver and default vehicle with one composite request', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const path = String(input)
      if (path === '/api/drivers/composite') return response({ ...driver, id: 'new-driver', defaultVehicleId: vehicle.id })
      if (path.startsWith('/api/drivers?')) return response(pageResult(driver))
      if (path === '/api/drivers/options') return response([])
      if (path === '/api/vehicles/options') return response([vehicle])
      return response(pageResult(driver))
    })
    setup(<DriversPage />)
    fireEvent.click(await screen.findByRole('button', { name: 'افزودن راننده' }))
    fireEvent.change(field('نام'), { target: { value: 'رضا' } })
    fireEvent.change(field('نام خانوادگی'), { target: { value: 'محمدی' } })
    fireEvent.change(field('شماره موبایل'), { target: { value: '09121112233' } })
    const defaultVehicle = field('خودروی پیش‌فرض')
    await waitFor(() => expect(defaultVehicle).not.toBeDisabled())
    fireEvent.change(defaultVehicle, { target: { value: vehicle.id } })
    fireEvent.click(screen.getByRole('button', { name: 'ذخیره' }))

    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0][0]).toBe('/api/drivers/composite')
    expect(JSON.parse(String(writes()[0][1]?.body))).toMatchObject({ defaultVehicleId: vehicle.id })
  })

  it('keeps an assigned driver omitted from active options during a profile-only update', async () => {
    const assignedVehicle = { ...vehicle, driverId: driver.id, driverDisplayName: driver.displayName }
    vi.mocked(fetch).mockImplementation(async (input) => {
      const path = String(input)
      if (path.startsWith('/api/vehicles?')) return response(pageResult(assignedVehicle))
      if (path === '/api/vehicles/options') return response([vehicle])
      if (path === '/api/drivers/options') return response([])
      if (path === '/api/vehicles/vehicle-1/composite') return response(assignedVehicle)
      return response(pageResult(assignedVehicle))
    })
    setup(<VehiclesPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'ویرایش' }))
    const relatedDriver = field('راننده این خودرو')
    await waitFor(() => expect(relatedDriver).not.toBeDisabled())
    expect(screen.getByRole('option', { name: driver.displayName })).toBeInTheDocument()
    fireEvent.change(field('برند'), { target: { value: 'برند جدید' } })
    fireEvent.click(screen.getByRole('button', { name: 'ذخیره' }))

    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0][0]).toBe('/api/vehicles/vehicle-1/composite')
    expect(JSON.parse(String(writes()[0][1]?.body))).toMatchObject({
      profile: { brand: 'برند جدید', rowVersion: 'vehicle-v1' },
      driverId: driver.id,
    })
  })

  it('creates a vehicle and related driver with one composite request', async () => {
    vi.mocked(fetch).mockImplementation(async (input) => {
      const path = String(input)
      if (path === '/api/vehicles/composite') return response({ ...vehicle, id: 'new-vehicle' })
      if (path.startsWith('/api/vehicles?')) return response(pageResult(vehicle))
      if (path === '/api/vehicles/options') return response([vehicle])
      if (path === '/api/drivers/options') return response([driver])
      return response(pageResult(vehicle))
    })
    setup(<VehiclesPage />)
    fireEvent.click(await screen.findByRole('button', { name: 'افزودن خودرو' }))
    fireEvent.change(field('برند'), { target: { value: 'ایویکو' } })
    fireEvent.change(field('مدل'), { target: { value: 'دیلی' } })
    fireEvent.change(screen.getByPlaceholderText('12'), { target: { value: '12' } })
    const letter = field('پلاک خودرو').querySelector('select')!
    fireEvent.change(letter, { target: { value: 'ب' } })
    fireEvent.change(screen.getByPlaceholderText('345'), { target: { value: '345' } })
    fireEvent.change(screen.getByPlaceholderText('67'), { target: { value: '67' } })
    const relatedDriver = field('راننده این خودرو')
    await waitFor(() => expect(relatedDriver).not.toBeDisabled())
    fireEvent.change(relatedDriver, { target: { value: driver.id } })
    fireEvent.click(screen.getByRole('button', { name: 'ذخیره' }))

    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0][0]).toBe('/api/vehicles/composite')
    expect(JSON.parse(String(writes()[0][1]?.body))).toMatchObject({ driverId: driver.id })
  })
})
