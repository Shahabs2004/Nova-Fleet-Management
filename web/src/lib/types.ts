export interface ProfileDetails {
  username: string
  firstName: string
  lastName: string
  mobilePhone: string | null
  email: string | null
  organizationalUnit: string | null
  avatarDataUrl: string | null
}

export interface UserDetails extends ProfileDetails {
  id: string
  isDisabled: boolean
  isDirectoryUser: boolean
  isLocalAdmin: boolean
  roleIds: string[]
}

export interface CurrentUser {
  avatarDataUrl: string | null
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
  isBackdated: boolean
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
  purpose: string | null
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

export interface DriverListItem {
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

export interface DriverDto extends DriverListItem {
  avatarDataUrl: string | null
}

export interface DriverOptionDto {
  id: string
  displayName: string
  mobilePhone: string
  defaultVehicleId: string | null
}

export interface DriverProfileRequest {
  firstName: string
  lastName: string
  mobilePhone: string
  avatarDataUrl: string | null
  rowVersion?: string
}

export interface DriverCompositeCreateRequest {
  profile: DriverProfileRequest
  defaultVehicleId: string | null
}

export interface DriverCompositeUpdateRequest extends DriverCompositeCreateRequest {
  profile: DriverProfileRequest & { rowVersion: string }
}

export interface VehicleProfileRequest {
  brand: string
  model: string
  plateFirstTwo: string
  plateLetter: string
  plateNextThree: string
  plateIranCode: string
  rowVersion?: string
}

export interface VehicleCompositeCreateRequest {
  profile: VehicleProfileRequest
  driverId: string | null
}

export interface VehicleCompositeUpdateRequest extends VehicleCompositeCreateRequest {
  profile: VehicleProfileRequest & { rowVersion: string }
}

export interface VehicleOptionDto {
  id: string
  displayName: string
  plateDisplay: string
  effectiveStatus: number
  manualStatus: number
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
  driverId: string | null
  driverDisplayName: string | null
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
  missionsByDay: { label: string; value: number; isToday: boolean }[]
  driverWorkload: { name: string; value: number }[]
  vehicleStatuses: { label: string; value: number }[]
  upcomingMissions: MissionListItem[]
}

export interface NotificationDto {
  id: string
  title: string
  message: string
  referenceType: string | null
  referenceId: string | null
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
  providerReference: string | null
  lastError: string | null
  createdAtUtc: string
  lastAttemptAtUtc: string | null
  acceptedAtUtc: string | null
  deliveredAtUtc: string | null
  nextAttemptAtUtc: string | null
  gatewayMode: number
  sendStartedAtUtc: string | null
  nextDeliveryPollAtUtc: string | null
  canRetry: boolean
  canCancel: boolean
  canReconcile: boolean
}

export interface SmsStatusSummary {
  queued: number
  accepted: number
  delivered: number
  failed: number
  permanentFailure: number
  todayCount: number
  indeterminate: number
}

export type SmsReconciliationDecision = 'retry' | 'failed'

export interface SmsReconciliationRequest {
  decision: SmsReconciliationDecision
  reason: string
  confirmed: boolean
}

export type SmsRecipient = 'requester' | 'driver'

export interface SmsTemplate {
  eventType: number
  eventKey: string
  title: string
  body: string
  isActive: boolean
  recipients: SmsRecipient[]
}

export interface SetupStatus {
  isCompleted: boolean
  hasLocalAdmin: boolean
  userCount: number
  organizationName: string | null
}

export interface SetupDatabaseState {
  isConfigured: boolean
  source: string
  serverName: string | null
  databaseName: string | null
  useWindowsAuthentication: boolean
  userName: string | null
  hasPassword: boolean
}

export interface SetupDatabaseResult {
  restartRequired: boolean
  createdDatabase: boolean
  sameAsActive: boolean
  hint: string
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

export interface DirectoryMappingDto {
  id: string
  groupName: string
  roleId: string
  roleTitle: string
  isActive: boolean
}

export interface DefaultRoleDto {
  roleName: string
}

export interface DirectoryGroupDto {
  name: string
  distinguishedName: string
}

export interface DirectoryGroupsResultDto {
  success: boolean
  error: string | null
  groups: DirectoryGroupDto[]
  hitSearchLimit: boolean
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
  missionRules: { minimumGapMinutes: number; futureRequestDays: number; withdrawalAutoCloseHours: number; startReminderMinutes: number; backdateRequestDays: number | null }
  sessionPolicy: { absoluteMinutes: number; idleMinutes: number }
  missionOrderTemplate: { headerText: string | null; footerText: string | null; showLogo: boolean }
}

export interface SettingsSaveResult {
  restartRequired: boolean
  hint: string | null
}

export interface ConnectionTestResult {
  success: boolean
  message: string
  detail: string | null
}

export interface DirectorySettings {
  enabled: boolean
  host: string
  port: number
  useSsl: boolean
  domain: string
  baseDn: string
  bindDn: string
  timeoutSeconds: number
  searchFilter: string
  hasBindPassword: boolean
}

export interface SmsConnectionSettings {
  enabled: boolean
  mode: string
  endpoint: string | null
  organization: string | null
  username: string | null
  senderNumber: string | null
  timeoutSeconds: number
  retryMinutes: number
  maxAttempts: number
  hasPassword: boolean
}

export interface DatabaseSettings {
  connectionString: string | null
  isConfigured: boolean
  source: string
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
  totalCount: number
  rowsTruncated: boolean
  maxRows: number
}