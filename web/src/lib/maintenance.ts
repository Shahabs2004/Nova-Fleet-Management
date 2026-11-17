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
export const assetKeys = ['001-spare-parts', '002-spare-parts-1', '003-brake', '004-damper', '005-service', '006-disc-brake', '007-spare-part', '008-mechanic', '009-clutch-disc', '010-engine', '011-car-suspension', '012-grid', '013-machine', '014-door', '015-car-engine', '016-transportation', '017-battery', '018-battery-1', '019-spare-part-1', '020-service-1', '021-car-battery', '022-car-lights', '023-spare-part-2', '024-disc-brake-1', '025-steering-wheel', '026-engine-1', '027-piston', '028-rudder', '029-car-parts', '030-tools', '031-piston-1', '032-car-engine-1', '033-motor', '034-engine-2', '035-brake-1', '036-car-parts-1', '037-air-conditioner', '038-car-parts-2', '039-car-parts-3', '040-car-parts-4', '041-multimeter', '042-motor-1', '043-filter', '044-drive', '045-motor-2', '046-safety-seat', '047-engine-3', '048-wheel', '049-gearbox', '050-filter-1', '051-suspension', '052-gas-tank', '053-car-door', '054-side-mirror', '055-manual-transmission', '056-battery-2', '057-windshield', '058-headlights', '059-gas-station', '060-fender', '061-spring', '062-clutch', '063-exhaust-pipe', '064-car-seat', '065-fuel', '066-washer', '067-car', '068-car-key', '069-brake-pad', '070-car-parts-5', '071-car-door-1', '072-car-mirror', '073-car-radiator', '074-car-1', '075-bumper-car', '076-wheel-alignment', '077-bearing', '078-car-door-2', '079-key', '080-sound', '081-side-mirror-1', '082-headlight', '083-cogwheel', '084-oil-filter', '085-fuel-1', '086-fuel-2', '087-automotive', '088-vehicle', '089-automotive-1', '090-automotive-2', '091-automotive-3', '092-automotive-4', '093-service-2', '094-car-light-bulb', '095-exhaust-pipe-1', '096-chain', '097-automotive-5', '098-battery-3', '099-battery-4', '100-dashboard-interface', '101-cooling-fan', '102-throttle-plate', '103-spark-plug', '104-disc-brake-2', '105-mechanic-1', '106-engine-4', '107-wheel-alignment-1', '108-mechanic-2', '109-abs-light', '110-engine-5', '111-headlight-1', '112-gasoline', '113-safety-belt', '114-transport', '115-car-wash', '116-mechanic-3', '117-car-wash-1', '118-washing', '119-mechanic-4', '120-car-lift', '121-car-painting']
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
