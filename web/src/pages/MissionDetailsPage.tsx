import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { api } from '../lib/api'
import { faNumber, formatFaDateTime, toFa } from '../lib/date'
import { MISSION_TYPE } from '../lib/labels'
import type { DriverDto, MissionDetails, VehicleDto } from '../lib/types'
import { Modal, PageHeader, Spinner, StatusBadge } from '../components/ui'

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
  const [newEnd, setNewEnd] = useState('')
  const [odometerStart, setOdometerStart] = useState('')
  const [odometerEnd, setOdometerEnd] = useState('')

  const { data: mission, isLoading } = useQuery({
    queryKey: ['mission', id],
    queryFn: () => api.get<MissionDetails>(`/api/missions/${id}`),
  })
  const { data: drivers } = useQuery({ queryKey: ['driver-options'], queryFn: () => api.get<DriverDto[]>('/api/drivers/options'), enabled: action === 'approve' })
  const { data: vehicles } = useQuery({ queryKey: ['vehicle-options'], queryFn: () => api.get<VehicleDto[]>('/api/vehicles/options'), enabled: action === 'approve' })

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['mission', id] })

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
      <button key="extend" className="btn-ghost" onClick={() => setAction('extend')}>
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
            <StatusBadge status={m.status} />
          </div>
          <div className="grid gap-x-8 px-5 py-2 sm:grid-cols-2">
            <InfoRow label="نوع" value={MISSION_TYPE[m.type]} />
            <InfoRow label="درخواست‌کننده" value={`${m.requesterName} (${m.requesterUsername})`} />
            <InfoRow label="واحد سازمانی" value={m.requesterOrganizationalUnit || '—'} />
            <InfoRow label="گروه درخواست‌کننده" value={m.requestingGroup} />
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
            <label className="label">زمان پایان جدید (UTC)</label>
            <input className="input" dir="ltr" type="datetime-local" value={newEnd} onChange={(e) => setNewEnd(e.target.value)} />
            <p className="mt-1 text-[11px] text-slate-400">زمان به‌صورت ساعت کامل به وقت تهران ذخیره می‌شود؛ اگر دقیقه وارد کردید گرد می‌شود.</p>
          </div>
          <div>
            <label className="label">علت تمدید</label>
            <input className="input" value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          {actionError && <div className="rounded-xl bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-100">{actionError}</div>}
          <button
            className="btn-primary w-full"
            disabled={!newEnd || !reason.trim() || act.isPending}
            onClick={() => {
              const utc = new Date(newEnd).toISOString().replace(/:\d\d\.\d+/, ':00')
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
    </div>
  )
}
