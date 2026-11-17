export interface Template {
  id: string; name: string; category: string; assetKey: string; intervalKm: number | null
  intervalMonths: number | null; warningKm: number; warningDays: number; isActive: boolean; version: number
}
export interface Part { id?: string; name: string; specification: string | null; quantity: number; unitCostToman: number; warrantyUntil: string | null }
export interface ServiceItem { id?: string; templateId: string; scheduleId: string | null; name?: string; category?: string; assetKey?: string; laborCostToman: number; parts: Part[] }
export interface Attachment { id: string; fileName: string; contentType: string; size: number; uploadedBy: string; createdAtUtc: string }
export interface ServiceRecord {
  id: string; vehicleId: string; performedAtUtc: string; odometerKm: number | null; workshop: string | null
  notes: string | null; otherCostToman: number; totalCostToman: number; isVoided: boolean; createdBy: string
  version: number; items: ServiceItem[]; attachments: Attachment[]
}
export interface Schedule {
  id: string; templateId: string; name: string; assetKey: string; intervalKm: number | null; intervalMonths: number | null
  warningKm: number; warningDays: number; baselineKm: number | null; baselineAtUtc: string | null; isActive: boolean; version: number
  lastKm: number | null; lastAtUtc: string | null; dueKm: number | null; dueAtUtc: string | null; state: string; cycleKey: string
}
export interface MaintenanceVehicle {
  vehicleId: string; displayName: string; plate: string; isArchived: boolean; currentKm: number | null
  odometerAtUtc: string | null; odometerTimeEstimated: boolean; hasOdometerConflicts: boolean; totalCostToman: number
  lastServiceAtUtc: string | null; dueCount: number; approachingCount: number; schedules: Schedule[]
}
export interface Reading {
  id: string; sourceKind: string; sourceId: string; observedAtUtc: string; isTimeEstimated: boolean
  kilometers: number; isVoided: boolean; recordedBy: string; version: number; hasConflict: boolean
}
export interface OdometerSummary { currentKm: number | null; observedAtUtc: string | null; isTimeEstimated: boolean; hasConflicts: boolean; readings: Reading[] }
export interface Revision { id: string; entityKind: string; entityId: string; version: number; beforeJson: string; reason: string; actor: string; createdAtUtc: string }
export interface MaintenanceReport {
  fromUtc: string; toUtc: string; totalCostToman: number; distanceKm: number | null; costPerKmToman: number | null
  distanceFromUtc: string | null; distanceToUtc: string | null; distanceCostToman: number | null
  months: { period: string; costToman: number }[]; categories: { category: string; costToman: number }[]
}
export const maintenancePath = (id: string) => `/api/maintenance/vehicles/${id}`
let serviceIconKeysPromise: Promise<string[]> | null = null
// manifest منبع زمان‌اجرای فهرست آیکن‌هاست: افزودن آیکن = یک PNG + یک ردیف manifest، بدون تغییر کد یا build.
export function loadServiceIconKeys(): Promise<string[]> {
  serviceIconKeysPromise ??= fetch('/assets/pixel-art/manifest.json')
    .then(r => r.json() as Promise<{ assets: { name: string; path: string }[] }>)
    .then(m => m.assets.filter(a => a.path.startsWith('maintenance/')).map(a => a.name).sort())
    .catch(err => { serviceIconKeysPromise = null; throw err })
  return serviceIconKeysPromise
}
const legacyAssetKeys: Record<string, string> = {
  'oil-can': '105-mechanic-1', 'oil-filter': '084-oil-filter', 'air-filter': '050-filter-1', 'old-tire': '048-wheel',
  'old-brake-pad': '069-brake-pad', 'old-battery': '021-car-battery', 'timing-belt': '096-chain', 'engine': '010-engine',
  'transmission': '049-gearbox', 'radiator': '073-car-radiator', 'electrical': '094-car-light-bulb', 'inspection': '120-car-lift',
  'general-service': '030-tools',
}
export function normalizeAssetKey(key: string): string { return legacyAssetKeys[key] ?? key }
export function pixelAsset(key: string, group = 'maintenance') {
  return `/assets/pixel-art/${group}/${normalizeAssetKey(key)}.png`
}
export const stateLabels: Record<string, string> = { due: 'سررسید رسیده', approaching: 'نزدیک موعد', healthy: 'در محدوده', unknown: 'نیازمند اطلاعات', inactive: 'غیرفعال' }
