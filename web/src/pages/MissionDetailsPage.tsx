import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import { faNumber, formatFaDateTime, jalaliOf, tehranHour, toFa } from '../lib/date'
import { JalaliDateField, jalaliValueToUtc, type JalaliDateValue } from '../components/JalaliDateField'
import { MISSION_TYPE } from '../lib/labels'
import type { DriverOptionDto, MissionDetails, VehicleOptionDto } from '../lib/types'
import { Modal, PageHeader, Spinner, StatusBadge } from '../components/ui'
import { MissionForm, type MissionFormOutput } from '../components/MissionForm'

function InfoRow({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-4 border-b border-slate-50 py-2.5 last:border-0">
      <span className="shrink-0 text-xs text-slate-400">{label}</span>
      <span className="text-left text-[13px] font-medium text-slate-700">{value ?? '—'}</span>
    </div>
  )
}

export function MissionDetailsPage() {
  const { id } = useParams<{ id: string }>()
  const { can, user } = useAuth()
  const queryClient = useQueryClient()
  const [action, setAction] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [driverId, setDriverId] = useState('')
  const [vehicleId, setVehicleId] = useState('')
  const [reason, setReason] = useState('')
  const [newEnd, setNewEnd] = useState<JalaliDateValue | null>(null)
  const [newEndHour, setNewEndHour] = useState('')
  const [extensionYear] = useState(() => jalaliOf(new Date())[0])
  const [odometerStart, setOdometerStart] = useState('')
  const [odometerEnd, setOdometerEnd] = useState('')

  const { data: mission, isLoading } = useQuery({
    queryKey: ['mission', user?.username, id],
    queryFn: () => api.get<MissionDetails>(`/api/missions/${id}`),
  })
  const { data: drivers } = useQuery({ queryKey: ['driver-options', user?.username], queryFn: () => api.get<DriverOptionDto[]>('/api/drivers/options'), enabled: action === 'approve' })
  const { data: vehicles } = useQuery({ queryKey: ['vehicle-options', user?.username], queryFn: () => api.get<VehicleOptionDto[]>('/api/vehicles/options'), enabled: action === 'approve' })

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['mission', user?.username, id] })

  const act = useMutation({
    mutationFn: async ({ path, body }: { path: string; body?: unknown }) => api.post(path, body),
    onSuccess: async () => {
      setAction(null)
      setActionError(null)
      setReason('')
      await invalidate()
    },
    onError: (err) => setActionError(err instanceof Error ? err.message : 'عملیات ناموفق بود.'),
  })

  const editMission = useMutation({
    mutationFn: (body: MissionFormOutput) => api.put(`/api/missions/${id}`, body),
    onSuccess: async () => {
      setAction(null)
      setActionError(null)
      await invalidate()
      await queryClient.invalidateQueries({ queryKey: ['missions', user?.username] })
    },
    onError: (err) => setActionError(err instanceof Error ? err.message : 'ویرایش مأموریت ناموفق بود.'),
  })

  if (isLoading) return <Spinner />
  if (!mission) return <p className="py-10 text-center text-slate-400">مأموریت پیدا نشد.</p>

  const m = mission
  const isRequester = m.requesterUsername === user?.username

  const runAction = (path: string, body?: unknown) => act.mutate({ path, body })

  const closeModal = () => {
    setAction(null)
    setActionError(null)
  }

  const actions: React.ReactNode[] = []
  if (m.status === 1 && can('missions.edit') && (isRequester || can('missions.approve'))) {
    actions.push(
      <button key="edit" className="btn-ghost" onClick={() => { setAction('edit'); setActionError(null) }}>
        ویرایش درخواست
      </button>,
    )
  }
  if (m.status === 1 && can('missions.review')) {
    actions.push(
      <button key="review" className="btn-ghost" onClick={() => runAction(`/api/missions/${id}/review`)}>
        شروع بررسی
      </button>,
    )
  }
  if ((m.status === 1 || m.status === 2) && can('missions.approve')) {
    actions.push(
      <button key="approve" className="btn-primary" onClick={() => { setAction('approve'); setDriverId(m.driverId ?? ''); setVehicleId(m.vehicleId ?? '') }}>
        تأیید و تخصیص
      </button>,
    )
  }
  if ((m.status === 1 || m.status === 2) && can('missions.reject')) {
    actions.push(
      <button key="reject" className="btn-danger" onClick={() => setAction('reject')}>
        رد درخواست
      </button>,
    )
  }
  if ((m.status === 3 || m.status === 4) && can('missions.extend')) {
    actions.push(
      <button key="extend" className="btn-ghost" onClick={() => {
        const [jy, jm, jd] = jalaliOf(new Date(m.plannedEndAtUtc))
        setNewEnd({ jy, jm, jd })
        setNewEndHour(String(tehranHour(m.plannedEndAtUtc)))
        setAction('extend')
      }}>
        تمدید
      </button>,
    )
  }
  if (m.status === 4 && can('missions.approve')) {
    actions.push(
      <button key="end" className="btn-ghost" onClick={() => setAction('end')}>
        خاتمه زودهنگام
      </button>,
    )
  }
  if ((m.status === 3 || m.status === 4) && (isRequester || can('missions.cancel'))) {
    actions.push(
      <button key="withdraw" className="btn-ghost" onClick={() => setAction('withdraw')}>
        درخواست انصراف
      </button>,
    )
  }
  if (m.status === 6 && can('missions.withdrawal.review')) {
    actions.push(
      <button key="w-approve" className="btn-primary" onClick={() => setAction('withdraw-approve')}>
        تأیید انصراف
      </button>,
      <button key="w-reject" className="btn-danger" onClick={() => setAction('withdraw-reject')}>
        رد انصراف
      </button>,
    )
  }
  if (![5, 7, 8].includes(m.status) && m.status !== 6 && can('missions.cancel')) {
    actions.push(
      <button key="cancel" className="btn-danger" onClick={() => setAction('cancel')}>
        لغو مأموریت
      </button>,
    )
  }
  if ((m.status === 3 || m.status === 4) && can('missions.odometer.manage')) {
    actions.push(
      <button key="odometer" className="btn-ghost" onClick={() => { setAction('odometer'); setOdometerStart(m.odometerStartKm?.toString() ?? ''); setOdometerEnd(m.odometerEndKm?.toString() ?? '') }}>
        ثبت کیلومترشمار
      </button>,
    )
  }
  if (can('missions.print') && m.status !== 6) {
    actions.push(
      m.approvedAtUtc && [3, 4, 5].includes(m.status) ? (
        <a key="print" className="btn-ghost" href={`/api/missions/${id}/order.pdf`} target="_blank" rel="noreferrer">
          چاپ حکم
        </a>
      ) : (
        <button key="print" type="button" className="btn-ghost" disabled title="چاپ حکم فقط برای مأموریت تأییدشده، در حال اجرا یا تکمیل‌شده فعال است.">
          چاپ حکم
        </button>
      ),
    )
  }

  return (
    <div>
      <PageHeader
        title={`مأموریت ${toFa(m.missionNumber)}`}
        subtitle={m.title}
        actions={
          <>
            <Link to="/missions" className="btn-ghost btn-sm">
              بازگشت به فهرست
            </Link>
            {actions}
          </>
        }
      />

      {m.decisionReason && (
        <div className="card mb-5 border-r-4 border-r-gold-500 bg-gold-100/40 p-4 text-sm text-slate-700">
          <span className="font-bold">یادداشت تصمیم: </span>
          {m.decisionReason}
        </div>
      )}

      <div className="grid gap-5 lg:grid-cols-3">
        <div className="card lg:col-span-2">
          <div className="flex items-center justify-between border-b border-slate-100 px-5 py-4">
            <h2 className="text-sm font-bold text-emerald-950">مشخصات مأموریت</h2>
            <StatusBadge status={m.status} isBackdated={m.isBackdated} />
          </div>
          <div className="grid gap-x-8 px-5 py-2 sm:grid-cols-2">
            <InfoRow label="نوع" value={MISSION_TYPE[m.type]} />
            <InfoRow label="درخواست‌کننده" value={`${m.requesterName} (${m.requesterUsername})`} />
            <InfoRow label="واحد سازمانی" value={m.requesterOrganizationalUnit || '—'} />
            <InfoRow label="مبدأ" value={m.origin} />
            <InfoRow label="مقصد" value={m.destination} />
            <InfoRow label="مقصدهای میانی" value={m.intermediateStops.length > 0 ? m.intermediateStops.join(' ← ') : '—'} />
            <InfoRow label="هدف" value={m.purpose} />
            <InfoRow label="شروع برنامه‌ریزی‌شده" value={formatFaDateTime(m.plannedStartAtUtc)} />
            <InfoRow label="پایان برنامه‌ریزی‌شده" value={formatFaDateTime(m.plannedEndAtUtc)} />
            <InfoRow label="شروع واقعی" value={m.actualStartAtUtc ? formatFaDateTime(m.actualStartAtUtc) : '—'} />
            <InfoRow label="پایان واقعی" value={m.actualEndAtUtc ? formatFaDateTime(m.actualEndAtUtc) : '—'} />
            {m.description && <InfoRow label="توضیحات" value={m.description} />}
            {m.passengerCount !== null && <InfoRow label="تعداد همراهان" value={faNumber(m.passengerCount)} />}
            {m.passengerNames && <InfoRow label="نام همراهان" value={m.passengerNames} />}
            {m.cargoDescription && <InfoRow label="شرح بار" value={m.cargoDescription} />}
            {m.consigneeName && <InfoRow label="تحویل‌گیرنده" value={m.consigneeName} />}
          </div>
        </div>

        <div className="space-y-5">
          <div className="card p-5">
            <h2 className="mb-3 text-sm font-bold text-emerald-950">منابع تخصیص‌یافته</h2>
            <InfoRow
              label="راننده"
              value={
                m.driverName ? (
                  <span>
                    {m.driverName}
                    <span className="num mr-1.5 text-xs text-slate-400" dir="ltr">{m.driverMobile}</span>
                  </span>
                ) : 'تخصیص نیافته'
              }
            />
            <InfoRow label="خودرو" value={m.vehicleDisplayName ?? 'تخصیص نیافته'} />
            <InfoRow label="تأییدکننده" value={m.approvedBy ? `${m.approvedBy}` : '—'} />
            {m.approvedAtUtc && <InfoRow label="زمان تأیید" value={formatFaDateTime(m.approvedAtUtc)} />}
          </div>

          <div className="card p-5">
            <h2 className="mb-3 text-sm font-bold text-emerald-950">کیلومترشمار</h2>
            <InfoRow label="شروع" value={m.odometerStartKm !== null ? `${faNumber(m.odometerStartKm)} کیلومتر` : '—'} />
            <InfoRow label="پایان" value={m.odometerEndKm !== null ? `${faNumber(m.odometerEndKm)} کیلومتر` : '—'} />
            <InfoRow label="مسافت" value={m.distanceKm !== null ? `${faNumber(m.distanceKm)} کیلومتر` : '—'} />
          </div>

          {(m.relatedMissionId || m.splitReason) && (
            <div className="card border-r-4 border-r-emerald-700 p-5">
              <h2 className="mb-3 text-sm font-bold text-emerald-950">تقسیم مأموریت</h2>
              <InfoRow label="علت تقسیم" value={m.splitReason} />
              {m.splitAtUtc && <InfoRow label="زمان تقسیم" value={formatFaDateTime(m.splitAtUtc)} />}
              {m.relatedMissionId && (
                <InfoRow
                  label="مأموریت مرتبط"
                  value={
                    <Link to={`/missions/${m.relatedMissionId}`} className="font-bold text-emerald-700 hover:underline">
                      {m.relatedMissionNumber}
                    </Link>
                  }
                />
              )}
            </div>
          )}
        </div>
      </div>

      <Modal open={action === 'approve'} title="تأیید و تخصیص منابع" onClose={closeModal}>
        {m.isBackdated && <p className="mb-4 rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900">
          این درخواست با تاریخ گذشته ثبت شده است. اگر زمان پایان گذشته باشد، پس از تأیید در چرخه خودکار تکمیل می‌شود.
        </p>}
        <div className="space-y-4">
          <div>
            <label className="label">راننده</label>
            <select className="input" value={driverId} onChange={(e) => setDriverId(e.target.value)}>
              <option value="">— انتخاب راننده —</option>
              {(drivers ?? []).map((d) => (
                <option key={d.id} value={d.id}>{d.displayName}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="label">خودرو</label>
            <select className="input" value={vehicleId} onChange={(e) => setVehicleId(e.target.value)}>
              <option value="">— انتخاب خودرو —</option>
              {(vehicles ?? []).filter((v) => v.manualStatus === 1).map((v) => (
                <option key={v.id} value={v.id}>{v.displayName}</option>
              ))}
            </select>
          </div>
          <div>
            <label className="label">یادداشت (اختیاری)</label>
            <input className="input" value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          {actionError && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{actionError}</div>}
          <button
            className="btn-primary w-full"
            disabled={!driverId || !vehicleId || act.isPending}
            onClick={() => runAction(`/api/missions/${id}/approve`, { driverId, vehicleId, note: reason || null })}
          >
            تأیید مأموریت
          </button>
        </div>
      </Modal>

      <Modal open={action === 'reject' || action === 'cancel' || action === 'withdraw'} title={action === 'reject' ? 'رد درخواست' : action === 'cancel' ? 'لغو مأموریت' : 'درخواست انصراف'} onClose={closeModal}>
        <div className="space-y-4">
          <div>
            <label className="label">علت (اجباری)</label>
            <textarea className="input min-h-24" value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          {actionError && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{actionError}</div>}
          <button
            className="btn-danger w-full"
            disabled={!reason.trim() || act.isPending}
            onClick={() =>
              runAction(
                action === 'reject' ? `/api/missions/${id}/reject` : action === 'cancel' ? `/api/missions/${id}/cancel` : `/api/missions/${id}/withdrawal`,
                { reason },
              )
            }
          >
            ثبت
          </button>
        </div>
      </Modal>

      <Modal open={action === 'withdraw-approve' || action === 'withdraw-reject'} title={action === 'withdraw-approve' ? 'تأیید انصراف' : 'رد انصراف'} onClose={closeModal}>
        <div className="space-y-4">
          <div>
            <label className="label">{action === 'withdraw-reject' ? 'علت رد (اجباری)' : 'یادداشت (اختیاری)'}</label>
            <textarea className="input min-h-20" value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          {actionError && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{actionError}</div>}
          <button
            className={action === 'withdraw-approve' ? 'btn-primary w-full' : 'btn-danger w-full'}
            disabled={(action === 'withdraw-reject' && !reason.trim()) || act.isPending}
            onClick={() => runAction(`/api/missions/${id}/withdrawal/decision`, { approve: action === 'withdraw-approve', reason: reason || null })}
          >
            ثبت تصمیم
          </button>
        </div>
      </Modal>

      <Modal open={action === 'extend'} title="تمدید مأموریت" onClose={closeModal}>
        <div className="space-y-4">
          <div>
            <label className="label">زمان پایان جدید (تهران)</label>
            <JalaliDateField value={newEnd} onChange={setNewEnd} startYear={newEnd?.jy ?? extensionYear} yearsBack={5} yearsForward={2} />
            <label className="label mt-3" htmlFor="extension-end-hour">ساعت پایان</label>
            <select id="extension-end-hour" className="input" value={newEndHour} onChange={(e) => setNewEndHour(e.target.value)}>
              <option value="">ساعت</option>
              {Array.from({ length: 24 }, (_, hour) => <option key={hour} value={hour}>{toFa(String(hour).padStart(2, '0'))}</option>)}
            </select>
            <p className="mt-1 text-[11px] text-slate-400">تاریخ و ساعت کامل به وقت تهران.</p>
          </div>
          <div>
            <label className="label" htmlFor="extension-reason">علت تمدید</label>
            <input id="extension-reason" className="input" value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          {actionError && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{actionError}</div>}
          <button
            className="btn-primary w-full"
            disabled={!newEnd || newEndHour === '' || !reason.trim() || act.isPending}
            onClick={() => {
              const utc = jalaliValueToUtc(newEnd, Number(newEndHour))
              if (!utc) return
              runAction(`/api/missions/${id}/extend`, { newPlannedEndAtUtc: utc, reason })
            }}
          >
            ثبت تمدید
          </button>
        </div>
      </Modal>

      <Modal open={action === 'end'} title="خاتمه زودهنگام مأموریت" onClose={closeModal}>
        <div className="space-y-4">
          <div>
            <label className="label">کیلومترشمار پایان (اختیاری)</label>
            <input className="input num" dir="ltr" type="number" min={0} value={odometerEnd} onChange={(e) => setOdometerEnd(e.target.value)} />
          </div>
          <div>
            <label className="label">علت خاتمه</label>
            <input className="input" value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          {actionError && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{actionError}</div>}
          <button
            className="btn-primary w-full"
            disabled={!reason.trim() || act.isPending}
            onClick={() => runAction(`/api/missions/${id}/end`, { endedAtUtc: new Date().toISOString(), odometerEndKm: odometerEnd ? Number(odometerEnd) : null, reason })}
          >
            ثبت خاتمه
          </button>
        </div>
      </Modal>

      <Modal open={action === 'odometer'} title="ثبت کیلومترشمار" onClose={closeModal}>
        <div className="space-y-4">
          <div>
            <label className="label">کیلومترشمار شروع</label>
            <input className="input num" dir="ltr" type="number" min={0} value={odometerStart} onChange={(e) => setOdometerStart(e.target.value)} />
          </div>
          <div>
            <label className="label">کیلومترشمار پایان</label>
            <input className="input num" dir="ltr" type="number" min={0} value={odometerEnd} onChange={(e) => setOdometerEnd(e.target.value)} />
          </div>
          {actionError && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{actionError}</div>}
          <button
            className="btn-primary w-full"
            disabled={act.isPending}
            onClick={() => runAction(`/api/missions/${id}/odometer`, { startKm: odometerStart ? Number(odometerStart) : null, endKm: odometerEnd ? Number(odometerEnd) : null, rowVersion: m.rowVersion })}
          >
            ثبت
          </button>
        </div>
      </Modal>

      <Modal open={action === 'edit'} title="ویرایش درخواست مأموریت" onClose={closeModal} wide>
        <MissionForm
          initial={m}
          submitting={editMission.isPending}
          error={actionError}
          onSubmit={(output) => editMission.mutate(output)}
        />
      </Modal>
    </div>
  )
}
