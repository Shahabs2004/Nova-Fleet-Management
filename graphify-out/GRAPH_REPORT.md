# Graph Report - Nova-Fleet-Management  (2026-11-08)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 1833 nodes · 5491 edges · 94 communities (81 shown, 13 thin omitted)
- Extraction: 86% EXTRACTED · 14% INFERRED · 0% AMBIGUOUS · INFERRED: 746 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `ddca8aab`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- CancellationToken
- AuditRecord
- Mission
- ReportQuery
- Driver
- EndpointMapper.MapMissionEndpoints
- PagedResult
- DateTimeOffset
- TimelinePage.tsx
- ISettingsService
- Guid
- main.tsx
- LdapDirectoryAuthenticator
- SecurityPage.tsx
- package.json
- NovaFleet.Infrastructure
- AuthService.LoginLdapAsync
- Task
- ApplicationUser
- MissionChangeRequest
- MissionsPage.tsx
- DashboardService.GetAsync
- NovaFleetDbContext
- AuditEntry
- ReportsPage.tsx
- microsoft_entityframeworkcore
- Vehicle
- EndpointMapper
- Program.cs
- NovaFleet.Application.Abstractions
- Location
- compilerOptions
- MissionLifecycleWorker
- NovaFleet.Domain.Common
- EndpointMapper.cs
- EntityTypeBuilder
- OrganizationUnitDto
- NovaFleet.Application.Common
- PermissionCatalog
- NotificationService
- types.ts
- compilerOptions
- UserAccessSnapshot
- http
- Entity
- UserSession
- 20261103092928_InitialCreate.Designer.cs
- SmsEventType
- NotificationModels.cs
- UserPermission
- Role
- UserNotification
- NovaFleet.Application.Authorization
- toFa
- SetupService.InitializeAsync
- VehicleDto
- MissionSplitReason
- AdpSmsGateway
- MissionNumberSequence
- SmsTemplate
- SmsQueueStatus
- IRealtimeNotifier.NotifyUsersAsync
- NotificationMessage
- DirectoryGroupRoleMapping
- OrganizationUnit
- NovaFleet.Domain.Missions
- AuditTrail
- AdpSmsGateway.SendAsync
- VehicleEffectiveStatus
- Mission.ApplyApprovedChanges
- SmsOptions
- SimulatedSmsGateway
- EffectivePermissionSet
- SmsDispatcher
- SmsService
- IranianLicensePlate
- ResolvedCurrentUser
- UserRole
- ManualVehicleStatus
- ICurrentUser
- AnonymousCurrentUser
- IPasswordHasher
- VehicleService
- LocationStatus
- .oxlintrc.json
- HttpContextRequestContext
- IClock
- VehicleService.cs
- Concurrency
- SmsDeliveryState
- ISmsService.GetStatusSummaryAsync
- SmsService.QueueTestAsync
- tsconfig.json

## God Nodes (most connected - your core abstractions)
1. `Mission` - 94 edges
2. `NovaFleetDbContext` - 75 edges
3. `SmsQueueItem` - 50 edges
4. `AuditRecord` - 49 edges
5. `MissionDetailsDto` - 43 edges
6. `ApplicationUser` - 43 edges
7. `MissionService` - 42 edges
8. `NovaFleet.Application.Abstractions` - 41 edges
9. `Vehicle` - 34 edges
10. `Driver` - 30 edges

## Surprising Connections (you probably didn't know these)
- `missionRouteHelpers()` --indirect_call--> `faNumber()`  [INFERRED]
  web/src/pages/MissionsPage.tsx → web/src/lib/date.ts
- `Missions` --references--> `Mission`  [EXTRACTED]
  src/NovaFleet.Infrastructure/Persistence/NovaFleetDbContext.cs → src/NovaFleet.Domain/Missions/Mission.cs
- `NovaFleet.IntegrationTests` --references--> `net10.0`  [EXTRACTED]
  tests/NovaFleet.IntegrationTests/NovaFleet.IntegrationTests.csproj → src/NovaFleet.Api/NovaFleet.Api.csproj
- `NovaFleet.IntegrationTests` --references--> `Microsoft.NET.Sdk`  [EXTRACTED]
  tests/NovaFleet.IntegrationTests/NovaFleet.IntegrationTests.csproj → src/NovaFleet.Domain/NovaFleet.Domain.csproj
- `NovaFleet.UnitTests` --references--> `net10.0`  [EXTRACTED]
  tests/NovaFleet.UnitTests/NovaFleet.UnitTests.csproj → src/NovaFleet.Api/NovaFleet.Api.csproj

## Import Cycles
- None detected.

## Communities (94 total, 13 thin omitted)

### Community 0 - "CancellationToken"
Cohesion: 0.09
Nodes (15): CreateLocalUserRequest, DirectoryMappingDto, IRoleService, IUserService, PermissionAssignmentDto, ResetPasswordRequest, RoleDto, SaveDirectoryMappingRequest (+7 more)

### Community 1 - "AuditRecord"
Cohesion: 0.15
Nodes (8): AuditRecord, AppException, ConflictAppException, ForbiddenAppException, ValidationAppException, MissionDetailsDto, VehicleCreateRequest, MissionService

### Community 2 - "Mission"
Cohesion: 0.04
Nodes (43): Mission, ActualEndAtUtc, ActualStartAtUtc, ApprovedAtUtc, ApprovedBy, CargoDescription, ConsigneeName, DecisionReason (+35 more)

### Community 3 - "ReportQuery"
Cohesion: 0.08
Nodes (19): IReportService, ReportColumnDto, ReportDefinitionDto, ReportQuery, ReportTableDto, MissionStatus, Cancelled, Completed (+11 more)

### Community 4 - "Driver"
Cohesion: 0.07
Nodes (22): EmploymentStatusBody, DriverCreateRequest, DriverDto, DriverOptionDto, DriverUpdateRequest, IDriverService, Driver, DefaultVehicleId (+14 more)

### Community 5 - "EndpointMapper.MapMissionEndpoints"
Cohesion: 0.07
Nodes (20): IMissionService, MissionApprovalRequest, MissionCancelRequest, MissionChangeDecision, MissionChangeInput, MissionChangeRequestDto, MissionChangeRequestInput, MissionEndRequest (+12 more)

### Community 6 - "PagedResult"
Cohesion: 0.06
Nodes (17): SessionCookieEvents, AuditEntryDto, AuditQuery, IAuditQueryService, PagedResult, TotalPages, Paging, ISessionService (+9 more)

### Community 7 - "DateTimeOffset"
Cohesion: 0.08
Nodes (23): ProposedLocationDto, SmsQueueItem, AcceptedAtUtc, AttemptCount, CanRetryManually, CreatedBy, DeliveredAtUtc, DeliveryPollCount (+15 more)

### Community 8 - "TimelinePage.tsx"
Cohesion: 0.10
Nodes (39): jalaliValueToUtc(), todayParts(), asTehran(), div(), FA_DIGITS, faWeekdayShort, formatFaDate(), formatFaDateLong() (+31 more)

### Community 9 - "ISettingsService"
Cohesion: 0.12
Nodes (9): ISettingsService, MissionOrderTemplateDto, MissionRulesDto, OrganizationProfileDto, SessionPolicyDto, SettingsBundleDto, SplitReasonDto, UpdateSplitReasonRequest (+1 more)

### Community 10 - "Guid"
Cohesion: 0.10
Nodes (5): NotFoundAppException, MissionAssignmentDto, MissionAvailabilityDto, TimelineMissionDto, MissionChanges

### Community 11 - "main.tsx"
Cohesion: 0.13
Nodes (27): react, react-dom, react-router-dom, @tanstack/react-query, dependencies, react@^19.3.1, react-dom@^19.3.1, react-router-dom@^7.18.5 (+19 more)

### Community 12 - "LdapDirectoryAuthenticator"
Cohesion: 0.10
Nodes (15): DirectoryAuthResult, DirectoryUserInfo, IDirectoryAuthenticator, IsConfigured, DirectoryOptions, BaseDn, BindDn, BindPassword (+7 more)

### Community 13 - "SecurityPage.tsx"
Cohesion: 0.14
Nodes (32): EmptyState(), Spinner(), AuditEntryDto, RoleDto, UserListItem, UserSessionDto, Assignment, AuditTab() (+24 more)

### Community 14 - "package.json"
Cohesion: 0.09
Nodes (28): oxlint, tailwindcss, @tailwindcss/vite, @types/node, @types/react, @types/react-dom, typescript, vite (+20 more)

### Community 15 - "NovaFleet.Infrastructure"
Cohesion: 0.12
Nodes (28): ClosedXML (0.105.1), Microsoft.AspNetCore.OpenApi (10.0.14), Microsoft.EntityFrameworkCore.SqlServer (10.0.14), Microsoft.Extensions.Http (10.0.14), Microsoft.Extensions.Options (10.0.14), QuestPDF (2026.9.1), Scalar.AspNetCore (2.18.7), System.DirectoryServices.Protocols (10.0.14) (+20 more)

### Community 16 - "AuthService.LoginLdapAsync"
Cohesion: 0.18
Nodes (6): UnauthorizedAppException, CurrentUserDto, IAuthService, LoginRequest, SessionContext, AuthService

### Community 17 - "Task"
Cohesion: 0.21
Nodes (4): ILocationService, LocationDto, ISmsDispatcher, LocationService

### Community 18 - "ApplicationUser"
Cohesion: 0.09
Nodes (17): ApplicationUser, DirectoryDistinguishedName, DirectoryGroupsJson, DisplayName, Email, FirstName, IsDirectoryUser, IsDisabled (+9 more)

### Community 19 - "MissionChangeRequest"
Cohesion: 0.10
Nodes (17): MissionChangeRequest, DecisionReason, MissionId, ProposedValuesJson, Reason, RequesterUsername, ReviewedAtUtc, ReviewerUsername (+9 more)

### Community 20 - "MissionsPage.tsx"
Cohesion: 0.15
Nodes (22): MissionForm(), Modal(), Pagination(), StatusBadge(), DRIVER_STATUS, LOCATION_STATUS, MISSION_STATUS, MISSION_TYPE (+14 more)

### Community 21 - "DashboardService.GetAsync"
Cohesion: 0.14
Nodes (6): DashboardChartPointDto, DashboardStatsDto, DashboardWorkloadDto, IDashboardService, JalaliDate, DashboardService

### Community 22 - "NovaFleetDbContext"
Cohesion: 0.12
Nodes (10): RolePermission, Effect, Permission, RoleId, RolePermissionConfiguration, DesignTimeDbContextFactory, NovaFleetDbContext, Missions (+2 more)

### Community 23 - "AuditEntry"
Cohesion: 0.10
Nodes (17): AuditEntry, Action, ActorUsername, AfterJson, BeforeJson, ClientIpAddress, CorrelationId, EntityId (+9 more)

### Community 24 - "ReportsPage.tsx"
Cohesion: 0.11
Nodes (19): JALALI_MONTHS, JalaliDateField(), JalaliDateValue, jalaliDaysInMonth(), DateTimeField(), DateValue, HOURS, MissionFormOutput (+11 more)

### Community 25 - "microsoft_entityframeworkcore"
Cohesion: 0.16
Nodes (8): NovaFleet.Application.Setup, NovaFleet.Domain.Configuration, NovaFleet.Infrastructure.Auditing, NovaFleet.Domain.Auditing, NovaFleet.Infrastructure.Persistence.Configurations, NovaFleet.Application.Auditing, NovaFleet.Infrastructure.Persistence, NovaFleet.Api.Workers

### Community 26 - "Vehicle"
Cohesion: 0.10
Nodes (14): Vehicle, Brand, DisplayName, IsArchived, IsReadyForAssignment, ManualStatus, Model, PlateFirstTwo (+6 more)

### Community 27 - "EndpointMapper"
Cohesion: 0.10
Nodes (10): ActiveBody, ArchiveBody, ChangeDecisionBody, ChangePasswordBody, DefaultVehicleBody, EndpointMapper, NameBody, ReasonBody (+2 more)

### Community 28 - "Program.cs"
Cohesion: 0.14
Nodes (9): NovaFleet.Infrastructure.Missions, NovaFleet.Infrastructure.Drivers, NovaFleet.Infrastructure.Reports, NovaFleet.Infrastructure.Settings, NovaFleet.Api.Middleware, NovaFleet.Application.Reports, NovaFleet.Infrastructure.Organization, NovaFleet.Application.Drivers (+1 more)

### Community 29 - "NovaFleet.Application.Abstractions"
Cohesion: 0.13
Nodes (4): NovaFleet.Infrastructure.Directory, NovaFleet.Infrastructure.Security, NovaFleet.Api.Identity, NovaFleet.Application.Abstractions

### Community 30 - "Location"
Cohesion: 0.15
Nodes (9): Location, Name, ProposedBy, ReviewedAtUtc, ReviewedBy, ReviewReason, Status, LocationConfiguration (+1 more)

### Community 31 - "compilerOptions"
Cohesion: 0.10
Nodes (19): compilerOptions, allowArbitraryExtensions, allowImportingTsExtensions, erasableSyntaxOnly, jsx, lib, module, moduleDetection (+11 more)

### Community 32 - "MissionLifecycleWorker"
Cohesion: 0.13
Nodes (5): CurrentUserMiddleware, ApiExceptionHandlerMiddleware, CsrfHeaderMiddleware, MissionLifecycleWorker, SmsQueueWorker

### Community 33 - "NovaFleet.Domain.Common"
Cohesion: 0.16
Nodes (7): NovaFleet.Application.Dashboard, NovaFleet.Domain.Vehicles, NovaFleet.Domain.Drivers, NovaFleet.Application.Missions, NovaFleet.Domain.Organization, NovaFleet.Domain.Common, NovaFleet.Infrastructure.Dashboard

### Community 34 - "EndpointMapper.cs"
Cohesion: 0.16
Nodes (7): NovaFleet.Infrastructure.Locations, NovaFleet.Api.Endpoints, NovaFleet.Domain.Sessions, NovaFleet.Domain.Locations, NovaFleet.Application.Locations, NovaFleet.Infrastructure.Sessions, NovaFleet.Application.Sessions

### Community 35 - "EntityTypeBuilder"
Cohesion: 0.16
Nodes (8): DriverConfiguration, ApplicationUserConfiguration, MissionConfiguration, AuditEntryConfiguration, SmsQueueItemConfiguration, SmsTemplateConfiguration, SystemSettingConfiguration, UserSessionConfiguration

### Community 36 - "OrganizationUnitDto"
Cohesion: 0.20
Nodes (4): IAuditTrail, IOrganizationUnitService, OrganizationUnitDto, OrganizationUnitService

### Community 37 - "NovaFleet.Application.Common"
Cohesion: 0.21
Nodes (7): NovaFleet.Application.Notifications, NovaFleet.Infrastructure.Sms, NovaFleet.Application.Settings, NovaFleet.Domain.Notifications, NovaFleet.Infrastructure.Notifications, NovaFleet.Application.Common, SettingKeys

### Community 38 - "PermissionCatalog"
Cohesion: 0.13
Nodes (11): Administration, Dashboard, Drivers, Missions, Notifications, PermissionCatalog, Definitions, PermissionDefinition (+3 more)

### Community 39 - "NotificationService"
Cohesion: 0.24
Nodes (4): INotificationService, NotificationDto, NotificationPageDto, NotificationService

### Community 40 - "types.ts"
Cohesion: 0.15
Nodes (15): ChangeRequestDto, DashboardStats, MissionDetails, MissionListItem, NotificationDto, PagedResult, SettingsBundle, SmsItem (+7 more)

### Community 41 - "compilerOptions"
Cohesion: 0.12
Nodes (16): compilerOptions, allowImportingTsExtensions, erasableSyntaxOnly, lib, module, moduleDetection, noEmit, noFallthroughCasesInSwitch (+8 more)

### Community 42 - "UserAccessSnapshot"
Cohesion: 0.23
Nodes (3): IPermissionResolver, UserAccessSnapshot, PermissionResolver

### Community 43 - "http"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 44 - "Entity"
Cohesion: 0.13
Nodes (10): Entity, CreatedAtUtc, Id, SystemSetting, Description, Key, RowVersion, UpdatedAtUtc (+2 more)

### Community 45 - "UserSession"
Cohesion: 0.12
Nodes (11): UserSession, ClientIpAddress, EndedAtUtc, LastActivityAtUtc, LoginAtUtc, LoginMethod, Reason, SessionKey (+3 more)

### Community 46 - "20261103092928_InitialCreate.Designer.cs"
Cohesion: 0.20
Nodes (3): NovaFleet.Infrastructure.Persistence.Migrations, InitialCreate, NovaFleetDbContextModelSnapshot

### Community 47 - "SmsEventType"
Cohesion: 0.15
Nodes (12): SmsEventNames, SmsEventType, DriverOrVehicleChanged, ManualTest, MissionApproved, MissionCancelled, MissionChanged, MissionCompleted (+4 more)

### Community 48 - "NotificationModels.cs"
Cohesion: 0.24
Nodes (5): ISmsService, SmsQuery, SmsQueueItemDto, SmsTemplateDto, UpdateSmsTemplateRequest

### Community 49 - "UserPermission"
Cohesion: 0.14
Nodes (9): PermissionEffect, Deny, Grant, UserPermission, Effect, Permission, UserId, UserPermissionConfiguration (+1 more)

### Community 50 - "Role"
Cohesion: 0.16
Nodes (8): Role, Description, IsActive, IsSystem, Name, Title, RoleConfiguration, Roles

### Community 51 - "UserNotification"
Cohesion: 0.14
Nodes (10): UserNotification, EventKey, Message, ReadAtUtc, RecipientUsername, ReferenceId, ReferenceType, Title (+2 more)

### Community 52 - "NovaFleet.Application.Authorization"
Cohesion: 0.23
Nodes (5): NovaFleet.Application.Identity, NovaFleet.Domain.Identity, NovaFleet.Infrastructure.Identity, NovaFleet.Application.Authorization, NovaFleet.Infrastructure.Setup

### Community 53 - "toFa"
Cohesion: 0.32
Nodes (13): recharts, recharts@^3.10.1, PageHeader(), StatCard(), faNumber(), formatFaDateTime(), toFa(), DashboardPage() (+5 more)

### Community 54 - "SetupService.InitializeAsync"
Cohesion: 0.25
Nodes (4): ISetupService, SetupRequest, SetupStatusDto, SetupService

### Community 55 - "VehicleDto"
Cohesion: 0.27
Nodes (3): IVehicleService, VehicleDto, VehicleUpdateRequest

### Community 56 - "MissionSplitReason"
Cohesion: 0.16
Nodes (6): MissionSplitReason, IsActive, SortOrder, Title, MissionSplitReasonConfiguration, MissionSplitReasons

### Community 58 - "MissionNumberSequence"
Cohesion: 0.19
Nodes (6): MissionNumberSequence, LastNumber, RowVersion, Year, MissionNumberSequenceConfiguration, MissionNumberSequences

### Community 59 - "SmsTemplate"
Cohesion: 0.19
Nodes (8): SmsTemplate, Body, EventKey, EventType, IsActive, RowVersion, UpdatedAtUtc, SmsTemplates

### Community 60 - "SmsQueueStatus"
Cohesion: 0.18
Nodes (10): SmsQueueStatus, Accepted, Cancelled, Delivered, Failed, PermanentFailure, Queued, RetryScheduled (+2 more)

### Community 61 - "IRealtimeNotifier.NotifyUsersAsync"
Cohesion: 0.24
Nodes (4): NovaFleet.Infrastructure.Realtime, IRealtimeNotifier, RealtimeMessage, NoopRealtimeNotifier

### Community 62 - "NotificationMessage"
Cohesion: 0.36
Nodes (3): INotificationPublisher, NotificationMessage, NotificationPublisher

### Community 63 - "DirectoryGroupRoleMapping"
Cohesion: 0.18
Nodes (6): DirectoryGroupRoleMapping, GroupName, IsActive, RoleId, DirectoryGroupRoleMappingConfiguration, DirectoryGroupRoleMappings

### Community 64 - "OrganizationUnit"
Cohesion: 0.20
Nodes (5): OrganizationUnit, IsActive, Name, OrganizationUnitConfiguration, OrganizationUnits

### Community 65 - "NovaFleet.Domain.Missions"
Cohesion: 0.22
Nodes (5): NovaFleet.Domain.Missions, NovaFleet.Application.Timeline, NovaFleet.Infrastructure.Timeline, ITimelineService, TimelineService

### Community 66 - "AuditTrail"
Cohesion: 0.20
Nodes (8): EmptyRequestContext, ClientIpAddress, CorrelationId, Instance, IRequestContext, ClientIpAddress, CorrelationId, AuditTrail

### Community 68 - "VehicleEffectiveStatus"
Cohesion: 0.20
Nodes (6): VehicleOptionDto, VehicleEffectiveStatus, InMission, OutOfService, Ready, RepairShop

### Community 70 - "SmsOptions"
Cohesion: 0.18
Nodes (10): SmsOptions, Endpoint, IsSimulation, Mode, Password, SendAction, SenderNumber, StatusAction (+2 more)

### Community 71 - "SimulatedSmsGateway"
Cohesion: 0.24
Nodes (5): ISmsGateway, IsSimulation, SmsDeliveryResult, SimulatedSmsGateway, IsSimulation

### Community 72 - "EffectivePermissionSet"
Cohesion: 0.27
Nodes (4): EffectiveCodes, EffectivePermissionSet, Empty, Grants

### Community 75 - "IranianLicensePlate"
Cohesion: 0.31
Nodes (3): IranianLicensePlate, LicensePlate, PlateDisplay

### Community 76 - "ResolvedCurrentUser"
Cohesion: 0.25
Nodes (7): ResolvedCurrentUser, DisplayName, IsAuthenticated, IsLocalAdmin, Permissions, Snapshot, Username

### Community 77 - "UserRole"
Cohesion: 0.25
Nodes (5): UserRole, RoleId, UserId, UserRoleConfiguration, UserRoles

### Community 78 - "ManualVehicleStatus"
Cohesion: 0.25
Nodes (5): ManualStatusBody, ManualVehicleStatus, OutOfService, Ready, RepairShop

### Community 79 - "ICurrentUser"
Cohesion: 0.25
Nodes (8): CurrentUserHolder, User, ICurrentUser, DisplayName, IsAuthenticated, IsLocalAdmin, Permissions, Username

### Community 80 - "AnonymousCurrentUser"
Cohesion: 0.29
Nodes (7): AnonymousCurrentUser, DisplayName, Instance, IsAuthenticated, IsLocalAdmin, Permissions, Username

### Community 83 - "LocationStatus"
Cohesion: 0.33
Nodes (5): LocationStatus, Approved, Disabled, Pending, Rejected

### Community 84 - ".oxlintrc.json"
Cohesion: 0.33
Nodes (5): plugins, rules, react/only-export-components, react/rules-of-hooks, $schema

### Community 85 - "HttpContextRequestContext"
Cohesion: 0.40
Nodes (3): HttpContextRequestContext, ClientIpAddress, CorrelationId

### Community 86 - "IClock"
Cohesion: 0.50
Nodes (4): IClock, UtcNow, SystemClock, UtcNow

### Community 90 - "SmsDeliveryState"
Cohesion: 0.50
Nodes (4): SmsDeliveryState, Delivered, Failed, Unknown

## Knowledge Gaps
- **308 isolated node(s):** `IsConfigured`, `BaseDn`, `BindDn`, `BindPassword`, `Domain` (+303 more)
  These have ≤1 connection - possible missing edges. (Counts symbols only; 601 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **13 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `NovaFleetDbContext` connect `NovaFleetDbContext` to `CancellationToken`, `AuditRecord`, `Mission`, `ReportQuery`, `Driver`, `PagedResult`, `DateTimeOffset`, `ISettingsService`, `AuthService.LoginLdapAsync`, `Task`, `ApplicationUser`, `MissionChangeRequest`, `DashboardService.GetAsync`, `AuditEntry`, `Vehicle`, `Location`, `NovaFleet.Domain.Common`, `OrganizationUnitDto`, `NotificationService`, `UserAccessSnapshot`, `Entity`, `UserSession`, `UserPermission`, `Role`, `UserNotification`, `SetupService.InitializeAsync`, `MissionSplitReason`, `MissionNumberSequence`, `SmsTemplate`, `NotificationMessage`, `DirectoryGroupRoleMapping`, `OrganizationUnit`, `NovaFleet.Domain.Missions`, `AuditTrail`, `SmsDispatcher`, `SmsService`, `UserRole`, `VehicleService`, `Concurrency`?**
  _High betweenness centrality (0.087) - this node is a cross-community bridge._
- **Are the 2 inferred relationships involving `Mission` (e.g. with `MissionService.CreateAsync()` and `MissionService.SplitAsync()`) actually correct?**
  _`Mission` has 2 INFERRED edges - model-reasoned connections that need verification._
- **What connects `IsConfigured`, `BaseDn`, `BindDn` to the rest of the system?**
  _308 weakly-connected nodes found - possible missing edges._
- **Should `CancellationToken` be split into smaller, more focused modules?**
  _Cohesion score 0.08933601609657947 - nodes in this community are weakly interconnected._
- **Why does `Mission` connect `Mission` to `NovaFleet.Domain.Missions`, `AuditRecord`, `ReportQuery`, `EntityTypeBuilder`, `Mission.ApplyApprovedChanges`, `EndpointMapper.MapMissionEndpoints`, `DateTimeOffset`, `SmsDispatcher`, `Guid`, `Entity`, `MissionChangeRequest`, `NovaFleetDbContext`?**
  _High betweenness centrality (0.057) - this node is a cross-community bridge._
- **Should `Mission` be split into smaller, more focused modules?**
  _Cohesion score 0.04371584699453552 - nodes in this community are weakly interconnected._
- **Why does `EndpointMapper` connect `EndpointMapper` to `CancellationToken`, `EndpointMapper.cs`, `ReportQuery`, `Driver`, `EndpointMapper.MapMissionEndpoints`, `OrganizationUnitDto`, `NotificationService`, `ISettingsService`, `Guid`, `ManualVehicleStatus`, `AuthService.LoginLdapAsync`, `Task`, `NotificationModels.cs`, `DashboardService.GetAsync`, `SetupService.InitializeAsync`, `VehicleDto`?**
  _High betweenness centrality (0.037) - this node is a cross-community bridge._