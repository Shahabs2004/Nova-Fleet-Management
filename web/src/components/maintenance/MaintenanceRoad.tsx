import { useEffect, useRef } from 'react'
import { faNumber, formatFaDate } from '../../lib/date'
import { pixelAsset, type MaintenanceVehicle, type Schedule, type ServiceRecord } from '../../lib/maintenance'
import { Notice, StateBadge } from './MaintenanceControls'

type RoadEvent = { id: string; km: number; title: string; date: string | null; asset: string; schedule?: Schedule; record?: ServiceRecord }
export function MaintenanceRoad({ vehicle, records, onRecord, onSchedule, hasMore, loadingMore, onMore }: { vehicle: MaintenanceVehicle; records: ServiceRecord[]; onRecord: (id: string) => void; onSchedule: (s: Schedule) => void; hasMore: boolean; loadingMore: boolean; onMore: () => void }) {
  const scroll = useRef<HTMLDivElement>(null)
  const car = useRef<HTMLDivElement>(null)
  useEffect(() => { if (scroll.current && car.current) scroll.current.scrollTop = Math.max(0, car.current.offsetTop - scroll.current.clientHeight / 3) }, [])
  const events: RoadEvent[] = [
    ...records.filter(r => !r.isVoided && r.odometerKm !== null).map(r => ({ id: r.id, km: r.odometerKm!, title: r.items.map(i => i.name).join('، '), date: r.performedAtUtc, asset: r.items[0]?.assetKey ?? 'general-service', record: r })),
    ...vehicle.schedules.filter(s => s.isActive && s.dueKm !== null).map(s => ({ id: s.id, km: s.dueKm!, title: s.name, date: s.dueAtUtc, asset: s.assetKey, schedule: s })),
  ]
  const kilometers = [...new Set([...events.map(e => e.km), ...(vehicle.currentKm !== null ? [vehicle.currentKm] : [])])].sort((a, b) => b - a)
  let y = 70
  const breaks: { y: number; high: number; low: number }[] = []
  const positions = new Map<number, number>()
  kilometers.forEach((km, i) => {
    if (i > 0) {
      const gap = kilometers[i - 1] - km
      if (gap > 5000) { breaks.push({ y: y + 12, high: kilometers[i - 1], low: km }); y += 76 }
      else y += Math.min(80, gap / 50)
    }
    positions.set(km, y)
    y += Math.max(190, Math.ceil(events.filter(e => e.km === km).length / 2) * 150)
  })
  const positioned = kilometers.flatMap((km, groupIndex) => events.filter(e => e.km === km).map((event, i) => ({ event, side: (groupIndex + i) % 2 === 0 ? 'left' : 'right', y: positions.get(km)! + Math.floor(i / 2) * 150 })))
  const dateOnly = vehicle.schedules.filter(s => s.isActive && s.dueKm === null)
  const unknown = records.filter(r => !r.isVoided && r.odometerKm === null)
  return <section className="m-road-panel" aria-label="جادهٔ نگهداری خودرو">
    <div className="m-row"><div><p className="m-kicker">دفتر سفر خودرو</p><h2>مسیر سرویس و نگهداری</h2></div>
      <button className="m-button" disabled={vehicle.currentKm === null} onClick={() => { if (scroll.current && car.current) scroll.current.scrollTop = car.current.offsetTop - scroll.current.clientHeight / 3 }}>کیلومتر فعلی</button></div>
    <p className="m-muted">بالای جاده: کیلومتر بیشتر · خطوط شکسته: فاصلهٔ فشرده‌شده</p>
    {vehicle.hasOdometerConflicts && <Notice error>محور، مقادیر ثبت‌شده را نشان می‌دهد؛ به‌علت ناسازگاری، موقعیت فعلی خودرو نامعلوم است.</Notice>}
    <div className="m-road-scroll" ref={scroll} tabIndex={0} aria-label="جاده با پیمایش مستقل">
      <div className="m-road" style={{ height: Math.max(650, y + 100) }}>
        <img className="m-pixel m-road-tree" src={pixelAsset('tree', 'scenery')} width={72} height={72} alt="" />
        <img className="m-pixel m-road-sign" src={pixelAsset('road-sign', 'scenery')} width={48} height={72} alt="" />
        {breaks.map(b => <div className="m-axis-break" key={b.high} style={{ top: b.y }}>شکست مقیاس · {faNumber(b.low)} تا {faNumber(b.high)} کیلومتر</div>)}
        {positioned.map(({ event: e, side, y: top }) => <button key={e.id} className="m-road-event" data-side={side} data-future={!!e.schedule} style={{ top }} onClick={() => e.record ? onRecord(e.record.id) : onSchedule(e.schedule!)}>
          <img className="m-icon" src={pixelAsset(e.asset)} alt="" /><strong>{e.title}</strong><span>{faNumber(e.km)} کیلومتر</span><small>{e.date ? formatFaDate(e.date) : 'بدون موعد زمانی'}</small>{e.schedule && <StateBadge state={e.schedule.state} />}
        </button>)}
        {vehicle.currentKm !== null && <div className="m-road-current" ref={car} style={{ top: positions.get(vehicle.currentKm) }}><img className="m-pixel" src={pixelAsset('vehicle-top', 'vehicles')} alt="موقعیت فعلی خودرو" /><span>{faNumber(vehicle.currentKm)} کیلومتر</span></div>}
        {kilometers.length === 0 && <div className="m-road-empty"><img className="m-pixel" src={pixelAsset('vehicle-top', 'vehicles')} width={64} height={128} alt="" /><p>برای شروع مسیر، کیلومتر یا سرویس ثبت کنید.</p></div>}
      </div>
    </div>
    {hasMore && <button className="m-button" disabled={loadingMore} onClick={onMore}>{loadingMore ? 'در حال دریافت…' : 'افزودن سوابق قدیمی‌تر به جاده'}</button>}
    {(dateOnly.length > 0 || unknown.length > 0) && <section className="m-panel m-list"><h3>خارج از محور کیلومتر</h3><small>برای موعدهای زمانی و سوابق بدون کیلومتر، موقعیت ساختگی روی جاده تعیین نمی‌شود.</small>
      {dateOnly.map(s => <button className="m-button" key={s.id} onClick={() => onSchedule(s)}>{s.name} · {formatFaDate(s.dueAtUtc)} <StateBadge state={s.state} /></button>)}
      {unknown.map(r => <button className="m-button" key={r.id} onClick={() => onRecord(r.id)}>{r.items.map(i => i.name).join('، ')} · {formatFaDate(r.performedAtUtc)}</button>)}
    </section>}
  </section>
}
