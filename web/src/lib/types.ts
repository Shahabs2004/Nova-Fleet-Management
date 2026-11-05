export interface CurrentUser {
  username: string
  displayName: string
  organizationalUnit: string | null
  isLocalAdmin: boolean
  roles: string[]
  permissions: string[]
}

export interface MissionListItem {
  id: string
  missionNumber: string
  title: string
  type: number
  status: number
  requesterName: string
  requesterOrganizationalUnit: string
  origin: string
  destination: string
  plannedStartAtUtc: string
  plannedEndAtUtc: string
  actualEndAtUtc: string | null
  driverId: string | null
  driverName: string | null
  vehicleId: string | null
  vehiclePlate: string | null
  splitReason: string | null
  relatedMissionId: string | null
  isOwn: boolean
}

export interface ChangeRequestDto {
  id: string
  requesterUsername: string
  reason: string
  status: number
  reviewerUsername: string | null
  createdAtUtc: string
  reviewStartedAtUtc: string | null
  reviewedAtUtc: string | null
  decisionReason: string | null
  proposed: Record<string, unknown>
}

export interface MissionDetails extends MissionListItem {
  requesterUsername: string
  requesterPhoneSnapshot: string
  requestingGroup: string
  purpose: string
  intermediateStops: string[]
  actualStartAtUtc: string | null
  driverMobile: string | null
  vehicleDisplayName: string | null
  description: string | null
  passengerCount: number | null
  missionOrderNumber: string | null
  missionOrderDate: string | null
  cargoDescription: string | null
  consigneeName: string | null
  passengerNames: string | null
  odometerStartKm: number | null
  odometerEndKm: number | null
  distanceKm: number | null
  decisionReason: string | null
  statusBeforeWithdrawal: number | null
  relatedMissionNumber: string | null
  splitAtUtc: string | null
  splitBy: string | null
  approvedBy: string | null
  approvedAtUtc: string | null
  reviewStartedAtUtc: string | null
  createdAtUtc: string
  updatedAtUtc: string
  rowVersion: string
  changeRequests: ChangeRequestDto[]
}

export interface TimelineMission {
  id: string
  missionNumber: string
  title: string
  status: number
  type: number
  startAtUtc: string
  endAtUtc: string
  driverId: string | null
  driverName: string | null
  vehicleId: string | null
  vehiclePlate: string | null
  requesterName: string
  origin: string
  destination: string
}

export interface DriverDto {
  id: string
  firstName: string
  lastName: string
  displayName: string
  mobilePhone: string
  employmentStatus: number
  defaultVehicleId: string | null
  defaultVehiclePlate: string | null
  isArchived: boolean
  createdAtUtc: string
  rowVersion: string
}

export interface VehicleDto {
  id: string
  brand: string
  model: string
  displayName: string
  licensePlate: string
  plateDisplay: string
  plateFirstTwo: string
  plateLetter: string
  plateNextThree: string
  plateIranCode: string
  manualStatus: number
  effectiveStatus: number
  isArchived: boolean
  createdAtUtc: string
  rowVersion: string
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export interface DashboardStats {
  pendingRequests: number
  activeMissions: number
  readyVehicles: number
  activeDrivers: number
  pendingWithdrawals: number
  pendingChangeRequests: number
  failedSms: number
  missionsThisMonth: number
  distanceThisMonthKm: number
  missionsByDay: { label: string; value: number }[]
  driverWorkload: { name: string; value: number }[]
  vehicleStatuses: { label: string; value: number }[]
  upcomingMissions: MissionListItem[]
}

export interface NotificationDto {
  id: string
  title: string
  message: string
  eventKey: string
  createdAtUtc: string
  readAtUtc: string | null
}

export interface SmsItem {
  id: string
  eventKey: string
  eventType: number
  missionId: string | null
  missionNumber: string
  recipientName: string
  recipientMobile: string
  messageBody: string
  status: number
  attemptCount: number
  maxAttempts: number
  lastError: string | null
  createdAtUtc: string
  deliveredAtUtc: string | null
  nextAttemptAtUtc: string | null
  canRetry: boolean
}

export interface SmsTemplate {
  eventType: number
  eventKey: string
  title: string
  body: string
  isActive: boolean
  recipients: string[]
}

export interface SetupStatus {
  isCompleted: boolean
  hasLocalAdmin: boolean
  userCount: number
  organizationName: string | null
}

export interface UserListItem {
  id: string
  username: string
  displayName: string
  organizationalUnit: string | null
  isDisabled: boolean
  isDirectoryUser: boolean
  isLocalAdmin: boolean
  roles: string[]
}

export interface RoleDto {
  id: string
  name: string
  title: string
  description: string | null
  isSystem: boolean
  isActive: boolean
  userCount: number
  permissions: { permission: string; effect: number }[]
}

export interface AuditEntryDto {
  id: string
  actorUsername: string
  action: string
  entityType: string
  entityId: string | null
  outcome: string
  reason: string | null
  clientIpAddress: string | null
  occurredAtUtc: string
}

export interface UserSessionDto {
  id: string
  username: string
  displayName: string
  loginMethod: number
  status: number
  loginAtUtc: string
  lastActivityAtUtc: string
  endedAtUtc: string | null
  clientIpAddress: string | null
  reason: string | null
}

export interface LocationDto {
  id: string
  name: string
  status: number
  proposedBy: string
  reviewedBy: string | null
  reviewReason: string | null
  reviewedAtUtc: string | null
  createdAtUtc: string
}

export interface OrgUnitDto {
  id: string
  name: string
  isActive: boolean
}

export interface SettingsBundle {
  organization: { name: string; logoDataUrl: string | null }
  missionRules: { minimumGapMinutes: number; futureRequestDays: number; withdrawalAutoCloseHours: number; startReminderMinutes: number }
  sessionPolicy: { absoluteMinutes: number; idleMinutes: number }
  missionOrderTemplate: { headerText: string | null; footerText: string | null; showLogo: boolean }
}

export interface ReportDefinition {
  key: string
  title: string
  description: string
}

export interface ReportTable {
  key: string
  title: string
  columns: { key: string; title: string; isNumeric?: boolean }[]
  rows: (string | number | null)[][]
  totals: Record<string, string | number>
  generatedAtUtc: string
  subtitle?: string
}