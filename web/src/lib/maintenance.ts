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
export const assetKeys = ['oil-can', 'oil-filter', 'air-filter', 'old-tire', 'old-brake-pad', 'old-battery', 'timing-belt', 'engine', 'transmission', 'radiator', 'electrical', 'inspection', 'general-service']
export function pixelAsset(key: string, group = 'maintenance') {
  return `/assets/pixel-art/${group}/${key}.png`
}
export const stateLabels: Record<string, string> = { due: 'سررسید رسیده', approaching: 'نزدیک موعد', healthy: 'در محدوده', unknown: 'نیازمند اطلاعات', inactive: 'غیرفعال' }
