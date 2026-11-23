import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { useAuth } from '../../auth/AuthContext'
import { api } from '../../lib/api'
import { faNumber, formatFaDate, toFa } from '../../lib/date'
import { maintenancePath, type MaintenanceReport } from '../../lib/maintenance'
import { DateField, Notice } from './MaintenanceControls'

export function MaintenanceReports({ vehicleId }: { vehicleId: string }) {
  const { user } = useAuth()
  const [from, setFrom] = useState<string | null>(() => new Date(Date.now() - 365 * 86400000).toISOString())
  const [to, setTo] = useState<string | null>(() => new Date().toISOString())
  const [range, setRange] = useState({ from, to })
  const query = useQuery({ queryKey: ['maintenance', user?.username, vehicleId, 'report', range], queryFn: () => api.get<MaintenanceReport>(`${maintenancePath(vehicleId)}/report?${new URLSearchParams({ from: range.from!, to: range.to! })}`), enabled: !!range.from && !!range.to })
  const report = query.data
  return <section className="m-list"><h2>هزینه و کارکرد</h2><form className="m-form" onSubmit={e => { e.preventDefault(); if (from && to) setRange({ from, to }) }}><div className="m-form-grid"><DateField label="از تاریخ" value={from} onChange={setFrom} /><DateField label="تا تاریخ" value={to} onChange={setTo} /></div><button className="m-button" disabled={!from || !to}>نمایش گزارش</button></form>
    {query.isLoading && <Notice>در حال محاسبه…</Notice>}{query.error && <Notice error>{query.error.message}</Notice>}
    {report && <><div className="m-form-grid"><div className="m-stat"><span>هزینهٔ بازه</span><strong>{faNumber(report.totalCostToman)}</strong><small>تومان</small></div><div className="m-stat"><span>هزینه به‌ازای کیلومتر</span><strong>{faNumber(report.costPerKmToman)}</strong><small>تومان / کیلومتر</small></div></div>
      {report.distanceKm === null ? <Notice>برای هزینهٔ هر کیلومتر، حداقل دو قرائت واقعی و سازگار با کارکرد مثبت در بازه لازم است؛ قرائت‌های دارای زمان برآوردی استفاده نمی‌شوند.</Notice> : <Notice>مبنای کارکرد: {faNumber(report.distanceKm)} کیلومتر، از {formatFaDate(report.distanceFromUtc)} تا {formatFaDate(report.distanceToUtc)}. هزینهٔ همین فاصله: {faNumber(report.distanceCostToman)} تومان.</Notice>}
      <h3>هزینهٔ ماهانه (تومان)</h3>{report.months.length > 0 ? <><div className="m-chart"><ResponsiveContainer><BarChart data={report.months}><CartesianGrid stroke="var(--m-line)" vertical={false} /><XAxis dataKey="period" stroke="var(--m-muted)" tickFormatter={toFa} /><YAxis stroke="var(--m-muted)" width={80} tickFormatter={v => faNumber(v)} /><Tooltip contentStyle={{ background: 'var(--m-bg)', borderColor: 'var(--m-line)', color: 'var(--m-text)' }} formatter={v => [`${faNumber(Number(v))} تومان`, 'هزینه']} /><Bar dataKey="costToman" fill="var(--m-cyan)" radius={[4, 4, 0, 0]} /></BarChart></ResponsiveContainer></div>
      <details><summary>جدول قابل‌خواندن هزینه‌های ماهانه</summary><table><thead><tr><th>ماه</th><th>هزینه</th></tr></thead><tbody>{report.months.map(m => <tr key={m.period}><td>{toFa(m.period)}</td><td>{faNumber(m.costToman)} تومان</td></tr>)}</tbody></table></details></> : <Notice>در این بازه هزینه‌ای ثبت نشده است.</Notice>}
      <h3>هزینه بر اساس دسته</h3><table><thead><tr><th>دسته</th><th>هزینه (تومان)</th></tr></thead><tbody>{report.categories.map(c => <tr key={c.category}><td>{c.category}</td><td>{faNumber(c.costToman)}</td></tr>)}</tbody></table>
    </>}
  </section>
}
