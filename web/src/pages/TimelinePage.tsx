import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { JalaliDateField, jalaliValueToUtc, type JalaliDateValue } from '../components/JalaliDateField'
import { EmptyState, Modal, PageHeader, Spinner } from '../components/ui'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import {
  faWeekdayShort,
  formatFaDateLong,
  formatFaDateTime,
  formatFaTime,
  jalaliMonths,
  jalaliOf,
  tehranHour,
  tehranWeekdayIndex,
  todayJalali,
  toFa,
} from '../lib/date'
import { MISSION_STATUS, MISSION_TYPE } from '../lib/labels'
import type { DriverDto, MissionDetails, TimelineMission, VehicleDto } from '../lib/types'

const DAY_MS = 86_400_000
const HOUR_MS = 3_600_000
const NAME_WIDTH = 190
const ROW_HEIGHT = 44
const BAR_HEIGHT = 26

type Zoom = 'hour' | 'day' | 'week' | 'month' | 'quarter'

/* زوم از «یک ساعت» (۲۴ پیکسل بر ساعت) تا «فصل» (۰٫۲ پیکسل بر ساعت، دو ماه در یک نگاه). */
const ZOOM_PX_PER_HOUR: Record<Zoom, number> = { hour: 24, day: 2.7, week: 1.08, month: 0.45, quarter: 0.18 }
const ZOOM_CELL_HOURS: Record<Zoom, number> = { hour: 1, day: 24, week: 24, month: 168, quarter: 168 }
const ZOOM_LABEL: Record<Zoom, string> = { hour: 'ساعت', day: 'روز', week: 'هفته', month: 'ماه', quarter: 'فصل' }
const ZOOM_ORDER: Zoom[] = ['hour', 'day', 'week', 'month', 'quarter']

const RANGE_PRESETS: { label: string; days: number; zoom: Zoom }[] = [
  { label: 'یک روز', days: 1, zoom: 'hour' },
  { label: 'سه روز', days: 3, zoom: 'day' },
  { label: 'یک هفته', days: 7, zoom: 'day' },
  { label: 'دو هفته', days: 14, zoom: 'week' },
  { label: 'یک ماه', days: 30, zoom: 'week' },
  { label: 'دو ماه', days: 60, zoom: 'month' },
]

interface TimeCell {
  key: string
  iso: string
  width: number
  label: string
  sub: string
  isWeekend: boolean
  isToday: boolean
}

interface PlacedMission {
  mission: TimelineMission
  lane: number
  start: number
  end: number
}

interface DriverRow {
  key: string
  name: string
  placed: PlacedMission[]
  lanes: number
}

interface MonthBand {
  label: string
  width: number
}

function rowHeight(row: DriverRow) {
  return Math.max(12 + row.lanes * BAR_HEIGHT, ROW_HEIGHT)
}

function toJalaliValue(iso: string): JalaliDateValue {
  const [jy, jm, jd] = jalaliOf(new Date(iso))
  return { jy, jm, jd }
}

function shiftDays(days: number): JalaliDateValue {
  const [jy, jm, jd] = jalaliOf(new Date(Date.now() + days * DAY_MS))
  return { jy, jm, jd }
}

function addJalaliDays(value: JalaliDateValue, days: number): JalaliDateValue {
  const iso = jalaliValueToUtc(value, 0)
  if (!iso) return value
  const [jy, jm, jd] = jalaliOf(new Date(new Date(iso).getTime() + days * DAY_MS))
  return { jy, jm, jd }
}

export function TimelinePage() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [year] = todayJalali()
  const [from, setFrom] = useState<JalaliDateValue | null>(() => shiftDays(-7))
  const [to, setTo] = useState<JalaliDateValue | null>(() => shiftDays(14))
  const [driverId, setDriverId] = useState('')
  const [vehicleId, setVehicleId] = useState('')
  const [zoom, setZoom] = useState<Zoom>('week')
  const [onlyBusyDrivers, setOnlyBusyDrivers] = useState(true)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const fromUtc = jalaliValueToUtc(from, 0)
  const toUtc = jalaliValueToUtc(to, 23)

  const drivers = useQuery({ queryKey: ['driver-options'], queryFn: () => api.get<DriverDto[]>('/api/drivers/options') })
  const vehicles = useQuery({ queryKey: ['vehicle-options'], queryFn: () => api.get<VehicleDto[]>('/api/vehicles/options') })

  const params = new URLSearchParams()
  if (fromUtc) params.set('fromUtc', fromUtc)
  if (toUtc) params.set('toUtc', toUtc)
  if (driverId) params.set('driverId', driverId)
  if (vehicleId) params.set('vehicleId', vehicleId)
  const enabled = Boolean(fromUtc && toUtc)

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['timeline', fromUtc, toUtc, driverId, vehicleId],
    queryFn: () => api.get<TimelineMission[]>(`/api/timeline?${params.toString()}`),
    enabled,
  })

  const hasGrid = enabled && !isLoading && !isError && (data?.length ?? 0) > 0

  const pxPerHour = ZOOM_PX_PER_HOUR[zoom]
  const cellHours = ZOOM_CELL_HOURS[zoom]
  const cellWidth = cellHours * pxPerHour
  const rangeStart = fromUtc ? new Date(fromUtc).getTime() : 0
  const rangeEnd = toUtc ? new Date(toUtc).getTime() : 0

  const scrollRef = useRef<HTMLDivElement>(null)
  const [viewportWidth, setViewportWidth] = useState(0)

  useEffect(() => {
    const node = scrollRef.current
    if (!node) return
    const observer = new ResizeObserver((entries) => setViewportWidth(entries[0].contentRect.width))
    observer.observe(node)
    return () => observer.disconnect()
  }, [hasGrid])

  const { cells, bands } = useMemo(() => {
    const empty = { cells: [] as TimeCell[], bands: [] as MonthBand[] }
    if (!enabled || rangeEnd <= rangeStart) return empty

    const today = jalaliOf(new Date())
    const list: TimeCell[] = []
    for (let cursor = rangeStart; cursor < rangeEnd; cursor += cellHours * HOUR_MS) {
      const iso = new Date(cursor).toISOString()
      const [jy, jm, jd] = jalaliOf(new Date(iso))
      const weekdayIndex = tehranWeekdayIndex(iso)
      const hour = tehranHour(iso)
      const isHourCell = cellHours === 1
      list.push({
        key: iso,
        iso,
        width: cellWidth,
        label: isHourCell ? toFa(String(hour).padStart(2, '0')) : toFa(jd),
        sub: isHourCell ? (hour === 0 ? `${toFa(jd)} ${jalaliMonths[jm - 1]}` : '') : faWeekdayShort[weekdayIndex],
        isWeekend: !isHourCell && weekdayIndex === 6,
        isToday: jy === today[0] && jm === today[1] && jd === today[2],
      })
    }

    const groups: MonthBand[] = []
    for (const cell of list) {
      const [jy, jm] = jalaliOf(new Date(cell.iso))
      const label = `${jalaliMonths[jm - 1]} ${toFa(jy)}`
      const last = groups[groups.length - 1]
      if (last && last.label === label) last.width += cell.width
      else groups.push({ label, width: cell.width })
    }

    return { cells: list, bands: groups }
  }, [enabled, rangeStart, rangeEnd, cellHours, cellWidth])

  /* ریل همیشه عرض ظرف را پر می‌کند: اگر جمع ستون‌ها کمتر از فضای موجود است مقیاس بالا می‌رود و اگر بیشتر است همان ۱ می‌ماند و اسکرول افقی فعال می‌شود. */
  const naturalWidth = cells.length * cellWidth
  const availableWidth = Math.max(viewportWidth - NAME_WIDTH - 2, 0)
  const scale = naturalWidth > 0 && availableWidth > naturalWidth ? availableWidth / naturalWidth : 1
  const trackWidth = naturalWidth * scale
  const scaledCells = cells.map((cell) => ({ ...cell, width: cell.width * scale }))
  const scaledBands = bands.map((band) => ({ ...band, width: band.width * scale }))

  const rows = useMemo<DriverRow[]>(() => {
    const map = new Map<string, DriverRow>()
    for (const mission of data ?? []) {
      const key = mission.driverId ?? 'unassigned'
      const row = map.get(key) ?? { key, name: mission.driverName ?? 'بدون راننده', placed: [], lanes: 1 }
      row.placed.push({
        mission,
        lane: 0,
        start: new Date(mission.startAtUtc).getTime(),
        end: new Date(mission.endAtUtc).getTime(),
      })
      map.set(key, row)
    }

    const known = new Map((drivers.data ?? []).map((item) => [item.id, item.displayName]))
    for (const row of map.values()) {
      if (row.key !== 'unassigned') row.name = known.get(row.key) ?? row.name
      row.placed.sort((a, b) => a.start - b.start)

      /* راننده و خودرو نباید مأموریت هم‌پوشان داشته باشند؛ lane فقط برای ردیف «بدون راننده» که چند درخواست باز روی یک زمان است. */
      const laneEnds: number[] = []
      for (const item of row.placed) {
        let lane = laneEnds.findIndex((endAt) => endAt <= item.start)
        if (lane < 0) {
          laneEnds.push(item.end)
          lane = laneEnds.length - 1
        } else {
          laneEnds[lane] = item.end
        }
        item.lane = lane
      }
      row.lanes = Math.max(laneEnds.length, 1)
    }

    const listed = [...map.values()].filter((row) => !onlyBusyDrivers || row.placed.length > 0)
    if (!onlyBusyDrivers) {
      for (const driver of drivers.data ?? []) {
        if (!listed.some((row) => row.key === driver.id)) {
          listed.push({ key: driver.id, name: driver.displayName, placed: [], lanes: 1 })
        }
      }
    }

    return listed.sort((a, b) => {
      if (a.key === 'unassigned') return 1
      if (b.key === 'unassigned') return -1
      return a.name.localeCompare(b.name, 'fa')
    })
  }, [data, drivers.data, onlyBusyDrivers])

  const totalMissions = data?.length ?? 0
  const todayOffset = ((Date.now() - rangeStart) / HOUR_MS) * pxPerHour * scale
  const showToday = hasGrid && todayOffset >= 0 && todayOffset <= trackWidth

  const selected = useQuery({
    queryKey: ['mission', selectedId],
    queryFn: () => api.get<MissionDetails>(`/api/missions/${selectedId}`),
    enabled: Boolean(selectedId),
  })

  const canEdit = Boolean(selectedId) && selected.data?.status === 1 && can('missions.edit')

  const rangeDays = fromUtc && toUtc ? Math.max(Math.round((rangeEnd - rangeStart + HOUR_MS) / DAY_MS), 1) : 0

  /* پیش‌بینی‌های بازه حول «امروز» می‌نشینند تا هم گذشته و هم آینده دیده شود. */
  const applyPreset = (preset: { days: number; zoom: Zoom }) => {
    const back = Math.floor(preset.days / 2)
    setFrom(shiftDays(-back))
    setTo(shiftDays(preset.days - back - 1))
    setZoom(preset.zoom)
  }

  const moveRange = (direction: 1 | -1) => {
    if (!from || !to) return
    const step = (rangeDays || 1) * direction
    setFrom(addJalaliDays(from, step))
    setTo(addJalaliDays(to, step))
  }

  const recenterToday = () => {
    const back = Math.floor((rangeDays - 1) / 2)
    setFrom(shiftDays(-back))
    setTo(shiftDays(rangeDays - back - 1))
  }

  /* تعادل بازه و مقیاس: با تغییر زوم، بازه حول مرکز فعلی آنقدر کوچک/بزرگ می‌شود که عرض ریل در محدوده خوانا بماند. */
  const applyZoom = (next: Zoom) => {
    setZoom(next)
    if (!from) return
    const pxPerDay = ZOOM_PX_PER_HOUR[next] * 24
    const fitted = Math.min(Math.max(rangeDays * pxPerDay, 620), 6200) / pxPerDay
    const days = Math.min(Math.max(Math.round(fitted), 1), 240)
    if (days === rangeDays) return
    const center = addJalaliDays(from, Math.floor(rangeDays / 2))
    const back = Math.floor((days - 1) / 2)
    setFrom(addJalaliDays(center, -back))
    setTo(addJalaliDays(center, days - back - 1))
  }

  const zoomHint = `${toFa(Math.round(ZOOM_PX_PER_HOUR[zoom] * 24))} پیکسل در روز · ${toFa(rangeDays)} روز`

  return (
    <div>
      <PageHeader
        title="تایم‌لاین مأموریت‌ها"
        subtitle="نوار زمانی هر راننده نشان می‌دهد از چه ساعتی تا چه ساعتی مأموریت دارد؛ با کلیک روی هر نوار می‌توانید ببینید ویرایش کنید"
      />

      <div className="card mb-5 p-4">
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
          <div>
            <label className="label">از تاریخ</label>
            <JalaliDateField value={from} onChange={setFrom} startYear={year} yearsBack={5} />
          </div>
          <div>
            <label className="label">تا تاریخ</label>
            <JalaliDateField value={to} onChange={setTo} startYear={year} yearsBack={5} />
          </div>
          <div>
            <label className="label">راننده</label>
            <select className="input" value={driverId} onChange={(e) => setDriverId(e.target.value)}>
              <option value="">همه رانندگان</option>
              {(drivers.data ?? []).map((item) => (
                <option key={item.id} value={item.id}>
                  {item.displayName}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="label">خودرو</label>
            <select className="input" value={vehicleId} onChange={(e) => setVehicleId(e.target.value)}>
              <option value="">همه خودروها</option>
              {(vehicles.data ?? []).map((item) => (
                <option key={item.id} value={item.id}>
                  {item.displayName} — {item.plateDisplay}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="label">بزرگ‌نمایی</label>
            <div className="flex rounded-xl bg-slate-100 p-1">
              {ZOOM_ORDER.map((item) => (
                <button
                  key={item}
                  onClick={() => applyZoom(item)}
                  title={`${ZOOM_LABEL[item]} · ${toFa(Math.round(ZOOM_PX_PER_HOUR[item] * 24))} پیکسل در روز`}
                  className={`flex-1 rounded-lg py-1.5 text-xs font-bold transition-colors ${
                    zoom === item ? 'bg-emerald-900 text-white shadow-sm' : 'text-slate-600 hover:text-emerald-800'
                  }`}
                >
                  {ZOOM_LABEL[item]}
                </button>
              ))}
            </div>
          </div>
        </div>

        <div className="mt-4 flex flex-wrap items-center gap-2">
          <span className="text-[11px] font-bold text-slate-400">بازه سریع</span>
          {RANGE_PRESETS.map((preset) => {
            const active = rangeDays === preset.days
            return (
              <button
                key={preset.label}
                onClick={() => applyPreset(preset)}
                className={`rounded-full px-3 py-1 text-[11px] font-bold transition-colors ${
                  active
                    ? 'bg-emerald-900 text-gold-400'
                    : 'bg-slate-100 text-slate-600 hover:bg-emerald-50 hover:text-emerald-800'
                }`}
              >
                {preset.label}
              </button>
            )
          })}
        </div>

        <div className="mt-4 flex flex-wrap items-center justify-between gap-3">
          <label className="flex cursor-pointer items-center gap-2 text-xs font-medium text-slate-600">
            <input
              type="checkbox"
              className="h-4 w-4 rounded border-slate-300 text-emerald-700 focus:ring-emerald-500"
              checked={onlyBusyDrivers}
              onChange={(e) => setOnlyBusyDrivers(e.target.checked)}
            />
            فقط رانندگانی که در این بازه مأموریت دارند
          </label>
          <div className="flex flex-wrap items-center gap-2">
            <div className="flex items-center gap-1 rounded-xl bg-slate-100 p-1">
              <button className="btn-ghost btn-sm" onClick={() => moveRange(-1)} title="بازه قبلی">
                ›
              </button>
              <button
                className="rounded-lg px-2.5 py-1 text-[11px] font-bold text-slate-600 transition-colors hover:text-emerald-800"
                onClick={recenterToday}
              >
                امروز
              </button>
              <button className="btn-ghost btn-sm" onClick={() => moveRange(1)} title="بازه بعدی">
                ‹
              </button>
            </div>
            <span className="badge bg-emerald-50 text-emerald-800 ring-1 ring-emerald-100">{toFa(rows.length)} راننده</span>
            <span className="badge bg-white text-slate-600 ring-1 ring-slate-200">{toFa(totalMissions)} مأموریت</span>
            <span className="badge bg-white text-slate-500 ring-1 ring-slate-200">{zoomHint}</span>
          </div>
        </div>
      </div>

      {!enabled && <div className="card p-6 text-sm text-slate-500">بازه تاریخ را کامل انتخاب کنید.</div>}
      {enabled && isLoading && <Spinner />}
      {enabled && isError && (
        <div className="card p-6 text-sm font-medium text-rose-700">{(error as Error)?.message ?? 'خطا در دریافت تایم‌لاین.'}</div>
      )}
      {enabled && !isLoading && !isError && totalMissions === 0 && (
        <div className="card">
          <EmptyState title="مأموریتی در این بازه نیست" hint="بازه تاریخ یا فیلترها را تغییر دهید." />
        </div>
      )}

      {hasGrid && (
        <div className="card overflow-hidden">
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 bg-emerald-50/50 px-5 py-3">
            <p className="text-sm font-bold text-emerald-950">
              {formatFaDateLong(fromUtc ?? '')} — {formatFaDateLong(toUtc ?? '')}
            </p>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1">
              {Object.values(MISSION_STATUS).map((item) => (
                <span key={item.title} className="flex items-center gap-1.5 text-[10px] text-slate-500">
                  <span className={`h-2.5 w-2.5 rounded-full ${item.dot}`} />
                  {item.title}
                </span>
              ))}
            </div>
          </div>

          <div ref={scrollRef} className="max-h-[70vh] overflow-auto">
            <div style={{ width: NAME_WIDTH + trackWidth }}>
              <div className="sticky top-0 z-30 flex bg-slate-50/95 backdrop-blur">
                <div
                  className="sticky right-0 z-40 flex items-center border-l border-slate-200 bg-slate-50/95 px-4 text-[11px] font-bold text-slate-600"
                  style={{ width: NAME_WIDTH }}
                >
                  راننده
                </div>
                <div>
                  <div className="flex border-b border-slate-100">
                    {scaledBands.map((band, index) => (
                      <div
                        key={`${band.label}-${index}`}
                        className="truncate border-l border-slate-100 py-1 text-center text-[10px] font-bold text-emerald-800"
                        style={{ width: band.width }}
                      >
                        {band.label}
                      </div>
                    ))}
                  </div>
                  <div className="flex">
                    {scaledCells.map((cell) => (
                      <div
                        key={cell.key}
                        title={cell.sub ? `${cell.label} ${cell.sub}` : cell.label}
                        className={`flex flex-col items-center justify-center border-l border-slate-100 py-1 ${
                          cell.isToday ? 'bg-gold-100' : cell.isWeekend ? 'bg-slate-200/60' : ''
                        }`}
                        style={{ width: cell.width }}
                      >
                        {cell.width >= 20 && (
                          <span
                            className={`num text-[10px] font-bold ${
                              cell.isToday ? 'text-emerald-900' : cell.isWeekend ? 'text-rose-500' : 'text-slate-600'
                            }`}
                          >
                            {cell.label}
                          </span>
                        )}
                        {cell.sub && cell.width >= 34 && (
                          <span className="text-[9px] text-slate-400">{cell.sub}</span>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              <div className="relative">
                {showToday && (
                  <div
                    className="pointer-events-none absolute top-0 z-20 h-full border-l-2 border-dashed border-gold-500"
                    style={{ right: NAME_WIDTH + todayOffset }}
                  />
                )}

                {rows.map((row, rowIndex) => (
                  <div
                    key={row.key}
                    className={`flex border-b border-slate-50 ${rowIndex % 2 ? 'bg-slate-50/40' : 'bg-white'}`}
                    style={{ minHeight: ROW_HEIGHT }}
                  >
                    <div
                      className={`sticky right-0 z-10 flex items-center gap-2 border-l border-slate-100 px-4 ${
                        rowIndex % 2 ? 'bg-slate-50/95' : 'bg-emerald-50/40'
                      }`}
                      style={{ width: NAME_WIDTH }}
                    >
                      <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-emerald-900 text-[11px] font-bold text-gold-400">
                        {row.name.trim()[0] ?? '؟'}
                      </span>
                      <div className="min-w-0">
                        <p className="truncate text-[13px] font-bold text-emerald-950">{row.name}</p>
                        <p className="text-[10px] text-slate-400">{toFa(row.placed.length)} مأموریت</p>
                      </div>
                    </div>

                    <div className="relative flex-1" style={{ height: rowHeight(row) }}>
                      <div className="absolute inset-0 flex">
                        {scaledCells.map((cell) => (
                          <div
                            key={cell.key}
                            className={`h-full border-l border-slate-100 ${cell.isWeekend ? 'bg-slate-200/50' : ''} ${
                              cell.isToday ? 'bg-gold-100/40' : ''
                            }`}
                            style={{ width: cell.width }}
                          />
                        ))}
                      </div>

                      {row.placed.length === 0 && (
                        <p className="absolute top-1/2 right-3 -translate-y-1/2 text-[11px] text-slate-300">بدون مأموریت</p>
                      )}

                      {row.placed.map((item) => {
                        const mission = item.mission
                        /* نوار مأموریت‌هایی که نیمی از بازه بیرون هستند داخل پنجره بریده می‌شود تا از ردیف بیرون نزند. */
                        const visibleStart = Math.max(item.start, rangeStart)
                        const visibleEnd = Math.min(item.end, rangeEnd)
                        const offset = ((visibleStart - rangeStart) / HOUR_MS) * pxPerHour * scale
                        const width = Math.max(((visibleEnd - visibleStart) / HOUR_MS) * pxPerHour * scale, 10)
                        const tone = MISSION_STATUS[mission.status]?.dot ?? 'bg-slate-400'
                        return (
                          <button
                            key={mission.id}
                            onClick={() => setSelectedId(mission.id)}
                            title={`${mission.title} · ${MISSION_TYPE[mission.type]} · ${formatFaDateTime(mission.startAtUtc)} تا ${formatFaDateTime(mission.endAtUtc)} · ${mission.vehiclePlate ?? 'بدون خودرو'}`}
                            className={`absolute flex items-center overflow-hidden rounded-lg px-2 text-right shadow-sm ring-1 ring-black/5 transition hover:z-20 hover:brightness-110 ${tone}`}
                            style={{
                              right: offset,
                              width: Math.min(width, trackWidth - offset),
                              top: 6 + item.lane * BAR_HEIGHT,
                              height: BAR_HEIGHT - 4,
                            }}
                          >
                            <span className="truncate text-[11px] font-bold text-white drop-shadow-[0_1px_1px_rgba(0,0,0,0.45)]">
                              {mission.title}
                            </span>
                            {width > 130 && (
                              <span className="num shrink-0 pr-2 text-[10px] font-medium text-white/90">
                                {formatFaTime(mission.startAtUtc)}
                              </span>
                            )}
                          </button>
                        )
                      })}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}

      <Modal
        open={Boolean(selectedId)}
        title={selected.data ? `مأموریت ${toFa(selected.data.missionNumber)}` : 'مأموریت'}
        onClose={() => setSelectedId(null)}
        wide
      >
        {selected.isLoading && <Spinner />}
        {selected.data && (
          <MissionQuickEdit
            key={selected.data.id}
            mission={selected.data}
            editable={canEdit}
            onSaved={() => {
              queryClient.invalidateQueries({ queryKey: ['timeline'] })
              queryClient.invalidateQueries({ queryKey: ['mission', selected.data.id] })
            }}
            onOpenFull={() => navigate(`/missions/${selected.data.id}`)}
          />
        )}
      </Modal>
    </div>
  )
}

function MissionQuickEdit({
  mission,
  editable,
  onSaved,
  onOpenFull,
}: {
  mission: MissionDetails
  editable: boolean
  onSaved: () => void
  onOpenFull: () => void
}) {
  const [title, setTitle] = useState(mission.title)
  const [origin, setOrigin] = useState(mission.origin)
  const [destination, setDestination] = useState(mission.destination)
  const [start, setStart] = useState<JalaliDateValue>(() => toJalaliValue(mission.plannedStartAtUtc))
  const [end, setEnd] = useState<JalaliDateValue>(() => toJalaliValue(mission.plannedEndAtUtc))
  const [startHour, setStartHour] = useState(tehranHour(mission.plannedStartAtUtc))
  const [endHour, setEndHour] = useState(tehranHour(mission.plannedEndAtUtc))
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const save = async () => {
    const plannedStartAtUtc = jalaliValueToUtc(start, startHour)
    const plannedEndAtUtc = jalaliValueToUtc(end, endHour)
    if (!plannedStartAtUtc || !plannedEndAtUtc) {
      setError('زمان شروع و پایان را کامل انتخاب کنید.')
      return
    }

    setSubmitting(true)
    setError(null)
    try {
      await api.put(`/api/missions/${mission.id}`, {
        title,
        type: mission.type,
        purpose: mission.purpose,
        origin,
        intermediateStops: mission.intermediateStops,
        destination,
        plannedStartAtUtc,
        plannedEndAtUtc,
        requestingGroup: mission.requestingGroup,
        requesterPhoneSnapshot: mission.requesterPhoneSnapshot,
        requesterOrganizationalUnit: mission.requesterOrganizationalUnit,
        description: mission.description,
        passengerCount: mission.passengerCount,
        missionOrderNumber: mission.missionOrderNumber,
        missionOrderDate: mission.missionOrderDate,
        cargoDescription: mission.cargoDescription,
        consigneeName: mission.consigneeName,
        passengerNames: mission.passengerNames,
      })
      onSaved()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="space-y-4">
      <div className="grid gap-3 rounded-xl bg-slate-50 p-4 text-xs sm:grid-cols-2 lg:grid-cols-3">
        <Info label="وضعیت" value={MISSION_STATUS[mission.status]?.title ?? '—'} />
        <Info label="نوع" value={MISSION_TYPE[mission.type] ?? '—'} />
        <Info label="درخواست‌کننده" value={mission.requesterName} />
        <Info label="راننده" value={mission.driverName ?? 'تخصیص نیافته'} />
        <Info label="خودرو" value={mission.vehicleDisplayName ?? mission.vehiclePlate ?? 'تخصیص نیافته'} />
        <Info label="واحد سازمانی" value={mission.requesterOrganizationalUnit || '—'} />
        <Info label="شروع برنامه‌ریزی‌شده" value={formatFaDateTime(mission.plannedStartAtUtc)} />
        <Info label="پایان برنامه‌ریزی‌شده" value={formatFaDateTime(mission.plannedEndAtUtc)} />
        <Info label="مسافت" value={mission.distanceKm != null ? `${toFa(mission.distanceKm)} کیلومتر` : '—'} />
      </div>

      {editable ? (
        <div className="grid gap-3 md:grid-cols-2">
          <div className="md:col-span-2">
            <label className="label">عنوان مأموریت</label>
            <input className="input" value={title} onChange={(e) => setTitle(e.target.value)} />
          </div>
          <div>
            <label className="label">مبدأ</label>
            <input className="input" value={origin} onChange={(e) => setOrigin(e.target.value)} />
          </div>
          <div>
            <label className="label">مقصد</label>
            <input className="input" value={destination} onChange={(e) => setDestination(e.target.value)} />
          </div>
          <div>
            <label className="label">تاریخ شروع</label>
            <JalaliDateField value={start} onChange={(value) => value && setStart(value)} startYear={start.jy} yearsBack={5} />
            <select className="input mt-2" value={startHour} onChange={(e) => setStartHour(Number(e.target.value))}>
              {Array.from({ length: 24 }, (_, hour) => (
                <option key={hour} value={hour}>
                  ساعت {toFa(String(hour).padStart(2, '0'))}
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="label">تاریخ پایان</label>
            <JalaliDateField value={end} onChange={(value) => value && setEnd(value)} startYear={end.jy} yearsBack={5} />
            <select className="input mt-2" value={endHour} onChange={(e) => setEndHour(Number(e.target.value))}>
              {Array.from({ length: 24 }, (_, hour) => (
                <option key={hour} value={hour}>
                  ساعت {toFa(String(hour).padStart(2, '0'))}
                </option>
              ))}
            </select>
          </div>
          {error && <p className="text-xs font-medium text-rose-700 md:col-span-2">{error}</p>}
        </div>
      ) : (
        <p className="rounded-xl bg-amber-50 p-4 text-xs leading-6 text-amber-800 ring-1 ring-amber-100">
          این مأموریت در وضعیت «{MISSION_STATUS[mission.status]?.title}» است و از این نما ویرایش نمی‌شود. برای تغییر زمان، مسیر یا
          منابع، از «درخواست تغییر» در صفحه مأموریت استفاده کنید.
        </p>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-4">
        <Link to={`/missions/${mission.id}`} className="text-xs font-medium text-emerald-700 hover:underline">
          مشاهده جزئیات کامل
        </Link>
        <div className="flex gap-2">
          {editable && (
            <button className="btn-primary btn-sm" onClick={save} disabled={submitting}>
              {submitting ? 'در حال ذخیره…' : 'ذخیره تغییرات'}
            </button>
          )}
          <button className="btn-ghost btn-sm" onClick={onOpenFull}>
            صفحه مأموریت
          </button>
        </div>
      </div>
    </div>
  )
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-[10px] text-slate-400">{label}</p>
      <p className="mt-0.5 font-bold text-slate-700">{value}</p>
    </div>
  )
}
