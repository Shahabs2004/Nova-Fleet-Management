# Graph Report - Nova-Fleet-Management  (2026-11-08)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 1881 nodes · 5580 edges · 90 communities (79 shown, 11 thin omitted)
- Extraction: 86% EXTRACTED · 14% INFERRED · 0% AMBIGUOUS · INFERRED: 789 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `35f147d7`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- CancellationToken
- ValidationAppException
- Mission
- ReportQuery
- Driver
- EndpointMapper.MapMissionEndpoints
- SessionCookieEvents
- DateTimeOffset
- TimelinePage.tsx
- SettingsService.GetBundleAsync
- MissionChangeInput
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
- NovaFleet.Domain.Missions
- NovaFleet.Application.Abstractions
- Location
- compilerOptions
- MissionLifecycleWorker
- DateTimeOffset
- Program.cs
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
- SmsService
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
- AuditRecord
- Driver
- ناوگان نوین — Nova Fleet Management
- OrganizationUnit
- SettingsService
- AuditTrail
- SessionService
- RolePermission
- Mission.ApplyApprovedChanges
- گشتی‌بر‌صفحه‌ها
- DriverEmploymentStatus
- EffectivePermissionSet
- SmsDispatcher
- PagedResult
- UserSessionLoginMethod
- ResolvedCurrentUser
- UserRole
- DesignTimeDbContextFactory
- ICurrentUser
- UserSessionStatus
- IPasswordHasher
- SettingsService.UpdateSessionPolicyAsync
- Entity
- .oxlintrc.json
- React + TypeScript + Vite
- IClock
- Concurrency
- tsconfig.json

## God Nodes (most connected - your core abstractions)
1. `Mission` - 95 edges
2. `NovaFleetDbContext` - 75 edges
3. `SmsQueueItem` - 50 edges
4. `AuditRecord` - 49 edges
5. `ApplicationUser` - 43 edges
6. `MissionDetailsDto` - 43 edges
7. `MissionService` - 43 edges
8. `NovaFleet.Application.Abstractions` - 41 edges
9. `Vehicle` - 34 edges
10. `EndpointMapper` - 30 edges

## Surprising Connections (you probably didn't know these)
- `محدودیت‌های‌شناخته‌شده` --references--> `AuditQueryService`  [INFERRED]
  README.md → src/NovaFleet.Infrastructure/Auditing/AuditQueryService.cs
- `محدودیت‌های‌شناخته‌شده` --references--> `RoleService`  [INFERRED]
  README.md → src/NovaFleet.Infrastructure/Identity/RoleService.cs
- `محدودیت‌های‌شناخته‌شده` --references--> `UserService`  [INFERRED]
  README.md → src/NovaFleet.Infrastructure/Identity/UserService.cs
- `کارهای‌انجام‌شده‌در‌این‌نسخه` --references--> `Mission`  [INFERRED]
  README.md → src/NovaFleet.Domain/Missions/Mission.cs
- `تقویم‌جلالی` --references--> `JalaliDate`  [INFERRED]
  README.md → src/NovaFleet.Domain/Common/JalaliDate.cs

## Import Cycles
- None detected.

## Communities (90 total, 11 thin omitted)

### Community 0 - "CancellationToken"
Cohesion: 0.06
Nodes (22): AuditEntryDto, AuditQuery, IAuditQueryService, CreateLocalUserRequest, DirectoryMappingDto, IRoleService, IUserService, PermissionAssignmentDto (+14 more)

### Community 1 - "ValidationAppException"
Cohesion: 0.13
Nodes (9): INotificationPublisher, NotificationMessage, AppException, ConflictAppException, ForbiddenAppException, ValidationAppException, MissionChangeRequestInput, MissionRequestInput (+1 more)

### Community 2 - "Mission"
Cohesion: 0.04
Nodes (43): Mission, ActualEndAtUtc, ActualStartAtUtc, ApprovedAtUtc, ApprovedBy, CargoDescription, ConsigneeName, DecisionReason (+35 more)

### Community 3 - "ReportQuery"
Cohesion: 0.07
Nodes (24): محدودیت‌های‌شناخته‌شده, کارهای‌انجام‌شده‌در‌این‌نسخه, گزارش‌ها‌و‌خروجی‌ها, IReportService, ReportColumnDto, ReportDefinitionDto, ReportQuery, ReportTableDto (+16 more)

### Community 4 - "Driver"
Cohesion: 0.17
Nodes (6): DriverCreateRequest, DriverDto, DriverOptionDto, DriverUpdateRequest, IDriverService, DriverService

### Community 5 - "EndpointMapper.MapMissionEndpoints"
Cohesion: 0.09
Nodes (16): IMissionService, MissionApprovalRequest, MissionAssignmentDto, MissionAvailabilityDto, MissionCancelRequest, MissionChangeDecision, MissionChangeRequestDto, MissionDetailsDto (+8 more)

### Community 7 - "DateTimeOffset"
Cohesion: 0.07
Nodes (22): SmsQueueItem, AcceptedAtUtc, AttemptCount, CanRetryManually, CreatedBy, DeliveredAtUtc, DeliveryPollCount, EventKey (+14 more)

### Community 8 - "TimelinePage.tsx"
Cohesion: 0.10
Nodes (39): jalaliValueToUtc(), todayParts(), asTehran(), div(), FA_DIGITS, faWeekdayShort, formatFaDate(), formatFaDateLong() (+31 more)

### Community 9 - "SettingsService.GetBundleAsync"
Cohesion: 0.24
Nodes (5): MissionOrderTemplateDto, MissionRulesDto, OrganizationProfileDto, SettingsBundleDto, UpdateSplitReasonRequest

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
Cohesion: 0.08
Nodes (31): ClosedXML (0.105.1), Microsoft.AspNetCore.OpenApi (10.0.14), Microsoft.EntityFrameworkCore.SqlServer (10.0.14), Microsoft.Extensions.Http (10.0.14), Microsoft.Extensions.Options (10.0.14), QuestPDF (2026.9.1), Scalar.AspNetCore (2.18.7), System.DirectoryServices.Protocols (10.0.14) (+23 more)

### Community 16 - "AuthService.LoginLdapAsync"
Cohesion: 0.16
Nodes (7): IPermissionResolver, UnauthorizedAppException, CurrentUserDto, IAuthService, LoginRequest, SessionContext, AuthService

### Community 17 - "Task"
Cohesion: 0.12
Nodes (4): NotFoundAppException, ILocationService, LocationDto, ISmsDispatcher

### Community 18 - "ApplicationUser"
Cohesion: 0.09
Nodes (18): ApplicationUser, DirectoryDistinguishedName, DirectoryGroupsJson, DisplayName, Email, FirstName, IsDirectoryUser, IsDisabled (+10 more)

### Community 19 - "MissionChangeRequest"
Cohesion: 0.10
Nodes (17): MissionChangeRequest, DecisionReason, MissionId, ProposedValuesJson, Reason, RequesterUsername, ReviewedAtUtc, ReviewerUsername (+9 more)

### Community 20 - "MissionsPage.tsx"
Cohesion: 0.15
Nodes (22): MissionForm(), Modal(), Pagination(), StatusBadge(), DRIVER_STATUS, LOCATION_STATUS, MISSION_STATUS, MISSION_TYPE (+14 more)

### Community 21 - "DashboardService.GetAsync"
Cohesion: 0.16
Nodes (7): DashboardChartPointDto, DashboardStatsDto, DashboardWorkloadDto, IDashboardService, MissionListItemDto, JalaliDate, DashboardService

### Community 22 - "NovaFleetDbContext"
Cohesion: 0.12
Nodes (9): راه‌اندازی‌کامل‌پس‌از‌دانلود, DirectoryGroupRoleMapping, GroupName, IsActive, RoleId, NovaFleetDbContext, DirectoryGroupRoleMappings, Missions (+1 more)

### Community 23 - "AuditEntry"
Cohesion: 0.14
Nodes (13): AuditEntry, Action, ActorUsername, AfterJson, BeforeJson, ClientIpAddress, CorrelationId, EntityId (+5 more)

### Community 24 - "ReportsPage.tsx"
Cohesion: 0.11
Nodes (19): JALALI_MONTHS, JalaliDateField(), JalaliDateValue, jalaliDaysInMonth(), DateTimeField(), DateValue, HOURS, MissionFormOutput (+11 more)

### Community 25 - "microsoft_entityframeworkcore"
Cohesion: 0.20
Nodes (8): NovaFleet.Domain.Vehicles, NovaFleet.Domain.Drivers, NovaFleet.Domain.Sessions, NovaFleet.Infrastructure.Settings, NovaFleet.Domain.Configuration, NovaFleet.Domain.Organization, NovaFleet.Infrastructure.Persistence.Configurations, NovaFleet.Infrastructure.Sessions

### Community 26 - "Vehicle"
Cohesion: 0.06
Nodes (26): IranianLicensePlate, ManualVehicleStatus, OutOfService, Ready, RepairShop, Vehicle, Brand, DisplayName (+18 more)

### Community 27 - "EndpointMapper"
Cohesion: 0.08
Nodes (11): ActiveBody, ArchiveBody, ChangeDecisionBody, ChangePasswordBody, DefaultVehicleBody, EndpointMapper, ManualStatusBody, NameBody (+3 more)

### Community 28 - "NovaFleet.Domain.Missions"
Cohesion: 0.14
Nodes (7): NovaFleet.Infrastructure.Missions, NovaFleet.Domain.Missions, NovaFleet.Infrastructure.Reports, NovaFleet.Application.Timeline, NovaFleet.Domain.Common, NovaFleet.Application.Reports, NovaFleet.Infrastructure.Timeline

### Community 30 - "Location"
Cohesion: 0.11
Nodes (13): Location, Name, ProposedBy, ReviewedAtUtc, ReviewedBy, ReviewReason, Status, LocationStatus (+5 more)

### Community 31 - "compilerOptions"
Cohesion: 0.10
Nodes (19): compilerOptions, allowArbitraryExtensions, allowImportingTsExtensions, erasableSyntaxOnly, jsx, lib, module, moduleDetection (+11 more)

### Community 32 - "MissionLifecycleWorker"
Cohesion: 0.19
Nodes (5): پشته‌فناوری, پیامک‌شبیه‌سازی‌و‌درگاه‌واقعی, ApiExceptionHandlerMiddleware, MissionLifecycleWorker, SmsQueueWorker

### Community 34 - "Program.cs"
Cohesion: 0.09
Nodes (20): NovaFleet.Application.Dashboard, NovaFleet.Infrastructure.Drivers, NovaFleet.Infrastructure.Locations, NovaFleet.Api.Endpoints, NovaFleet.Domain.Locations, NovaFleet.Infrastructure.Vehicles, NovaFleet.Api.Middleware, NovaFleet.Application.Missions (+12 more)

### Community 35 - "EntityTypeBuilder"
Cohesion: 0.16
Nodes (8): LocationConfiguration, DirectoryGroupRoleMappingConfiguration, MissionConfiguration, AuditEntryConfiguration, SmsQueueItemConfiguration, SmsTemplateConfiguration, SystemSettingConfiguration, UserSessionConfiguration

### Community 36 - "OrganizationUnitDto"
Cohesion: 0.27
Nodes (3): IOrganizationUnitService, OrganizationUnitDto, OrganizationUnitService

### Community 37 - "NovaFleet.Application.Common"
Cohesion: 0.21
Nodes (7): NovaFleet.Application.Notifications, NovaFleet.Infrastructure.Sms, NovaFleet.Application.Settings, NovaFleet.Domain.Notifications, NovaFleet.Infrastructure.Notifications, NovaFleet.Application.Common, SettingKeys

### Community 38 - "PermissionCatalog"
Cohesion: 0.13
Nodes (12): سطح‌API, مدل‌امنیتی, Administration, Dashboard, Drivers, Notifications, PermissionCatalog, Definitions (+4 more)

### Community 39 - "NotificationService"
Cohesion: 0.23
Nodes (4): INotificationService, NotificationDto, NotificationPageDto, NotificationService

### Community 40 - "types.ts"
Cohesion: 0.15
Nodes (15): ChangeRequestDto, DashboardStats, MissionDetails, MissionListItem, NotificationDto, PagedResult, SettingsBundle, SmsItem (+7 more)

### Community 41 - "compilerOptions"
Cohesion: 0.12
Nodes (16): compilerOptions, allowImportingTsExtensions, erasableSyntaxOnly, lib, module, moduleDetection, noEmit, noFallthroughCasesInSwitch (+8 more)

### Community 43 - "http"
Cohesion: 0.13
Nodes (15): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, applicationUrl, commandName (+7 more)

### Community 44 - "Entity"
Cohesion: 0.18
Nodes (7): SystemSetting, Description, Key, RowVersion, UpdatedAtUtc, Value, SystemSettings

### Community 45 - "UserSession"
Cohesion: 0.12
Nodes (11): UserSession, ClientIpAddress, EndedAtUtc, LastActivityAtUtc, LoginAtUtc, LoginMethod, Reason, SessionKey (+3 more)

### Community 46 - "20261103092928_InitialCreate.Designer.cs"
Cohesion: 0.14
Nodes (6): NovaFleet.Infrastructure.Auditing, NovaFleet.Application.Auditing, NovaFleet.Infrastructure.Persistence, NovaFleet.Api.Workers, NovaFleet.Infrastructure.Persistence.Migrations, NovaFleetDbContextModelSnapshot

### Community 47 - "SmsEventType"
Cohesion: 0.14
Nodes (13): SmsDefaults, SmsEventNames, SmsEventType, DriverOrVehicleChanged, ManualTest, MissionApproved, MissionCancelled, MissionChanged (+5 more)

### Community 48 - "SmsService"
Cohesion: 0.18
Nodes (6): ISmsService, SmsQuery, SmsQueueItemDto, SmsStatusSummaryDto, SmsTestRequest, SmsService

### Community 49 - "UserPermission"
Cohesion: 0.22
Nodes (6): UserPermission, Effect, Permission, UserId, UserPermissionConfiguration, UserPermissions

### Community 50 - "Role"
Cohesion: 0.15
Nodes (8): Role, Description, IsActive, IsSystem, Name, Title, RoleConfiguration, Roles

### Community 51 - "UserNotification"
Cohesion: 0.11
Nodes (14): IRealtimeNotifier, RealtimeMessage, UserNotification, EventKey, Message, ReadAtUtc, RecipientUsername, ReferenceId (+6 more)

### Community 52 - "NovaFleet.Application.Authorization"
Cohesion: 0.18
Nodes (6): NovaFleet.Application.Identity, NovaFleet.Application.Setup, NovaFleet.Domain.Identity, NovaFleet.Domain.Auditing, NovaFleet.Infrastructure.Identity, NovaFleet.Infrastructure.Setup

### Community 53 - "toFa"
Cohesion: 0.32
Nodes (13): recharts, recharts@^3.10.1, PageHeader(), StatCard(), faNumber(), formatFaDateTime(), toFa(), DashboardPage() (+5 more)

### Community 54 - "SetupService.InitializeAsync"
Cohesion: 0.27
Nodes (4): ISetupService, SetupRequest, SetupStatusDto, SetupService

### Community 55 - "VehicleDto"
Cohesion: 0.17
Nodes (6): IVehicleService, VehicleCreateRequest, VehicleDto, VehicleOptionDto, VehicleUpdateRequest, VehicleService

### Community 56 - "MissionSplitReason"
Cohesion: 0.16
Nodes (6): MissionSplitReason, IsActive, SortOrder, Title, MissionSplitReasonConfiguration, MissionSplitReasons

### Community 57 - "AdpSmsGateway"
Cohesion: 0.07
Nodes (23): ISmsGateway, IsSimulation, SmsDeliveryResult, SmsDeliveryState, Delivered, Failed, Unknown, SmsOutboundMessage (+15 more)

### Community 58 - "MissionNumberSequence"
Cohesion: 0.19
Nodes (6): MissionNumberSequence, LastNumber, RowVersion, Year, MissionNumberSequenceConfiguration, MissionNumberSequences

### Community 59 - "SmsTemplate"
Cohesion: 0.14
Nodes (10): SmsTemplateDto, UpdateSmsTemplateRequest, SmsTemplate, Body, EventKey, EventType, IsActive, RowVersion (+2 more)

### Community 60 - "SmsQueueStatus"
Cohesion: 0.18
Nodes (10): SmsQueueStatus, Accepted, Cancelled, Delivered, Failed, PermanentFailure, Queued, RetryScheduled (+2 more)

### Community 61 - "AuditRecord"
Cohesion: 0.29
Nodes (3): AuditRecord, IAuditTrail, LocationService

### Community 62 - "Driver"
Cohesion: 0.11
Nodes (11): Driver, DefaultVehicleId, DisplayName, EmploymentStatus, FirstName, IsArchived, LastName, MobilePhone (+3 more)

### Community 63 - "ناوگان نوین — Nova Fleet Management"
Cohesion: 0.12
Nodes (16): اجرای‌محیط‌توسعه, احراز‌هویت‌با‌active‌directory, تقویم‌جلالی, خط زمانی, داده‌نمونه‌و‌حساب‌های‌نمایشی, راه‌اندازی‌پایگاه‌داده, ساختارمخزن, عیب‌یابی (+8 more)

### Community 64 - "OrganizationUnit"
Cohesion: 0.20
Nodes (5): OrganizationUnit, IsActive, Name, OrganizationUnitConfiguration, OrganizationUnits

### Community 65 - "SettingsService"
Cohesion: 0.21
Nodes (3): ISettingsService, SplitReasonDto, SettingsService

### Community 66 - "AuditTrail"
Cohesion: 0.17
Nodes (10): HttpContextRequestContext, ClientIpAddress, CorrelationId, EmptyRequestContext, ClientIpAddress, CorrelationId, Instance, IRequestContext (+2 more)

### Community 67 - "SessionService"
Cohesion: 0.31
Nodes (4): ISessionService, SessionQuery, UserSessionDto, SessionService

### Community 68 - "RolePermission"
Cohesion: 0.22
Nodes (6): RolePermission, Effect, Permission, RoleId, RolePermissionConfiguration, RolePermissions

### Community 69 - "Mission.ApplyApprovedChanges"
Cohesion: 0.16
Nodes (5): MissionChanges, MissionRequestChanges, MissionType, Freight, Passenger

### Community 70 - "گشتی‌بر‌صفحه‌ها"
Cohesion: 0.22
Nodes (9): امنیت, تنظیمات, خط زمانی (تایم‌لاین), مأموریت‌ها, ناوگان: رانندگان و خودروها, ورود و داشبورد, پیامک, گزارش‌ها (+1 more)

### Community 71 - "DriverEmploymentStatus"
Cohesion: 0.22
Nodes (6): EmploymentStatusBody, DriverEmploymentStatus, Active, Inactive, OnLeave, Terminated

### Community 72 - "EffectivePermissionSet"
Cohesion: 0.14
Nodes (11): AnonymousCurrentUser, DisplayName, Instance, IsAuthenticated, IsLocalAdmin, Permissions, Username, EffectiveCodes (+3 more)

### Community 74 - "PagedResult"
Cohesion: 0.29
Nodes (3): PagedResult, TotalPages, Paging

### Community 75 - "UserSessionLoginMethod"
Cohesion: 0.33
Nodes (4): UserSessionLoginMethod, Ldap, Local, Windows

### Community 76 - "ResolvedCurrentUser"
Cohesion: 0.14
Nodes (9): ResolvedCurrentUser, DisplayName, IsAuthenticated, IsLocalAdmin, Permissions, Snapshot, Username, CurrentUserMiddleware (+1 more)

### Community 77 - "UserRole"
Cohesion: 0.25
Nodes (5): UserRole, RoleId, UserId, UserRoleConfiguration, UserRoles

### Community 78 - "DesignTimeDbContextFactory"
Cohesion: 0.40
Nodes (3): تست‌و‌مهاجرت, پیکربندی, DesignTimeDbContextFactory

### Community 79 - "ICurrentUser"
Cohesion: 0.18
Nodes (9): CurrentUserHolder, User, ICurrentUser, DisplayName, IsAuthenticated, IsLocalAdmin, Permissions, Username (+1 more)

### Community 80 - "UserSessionStatus"
Cohesion: 0.40
Nodes (5): UserSessionStatus, Active, Ended, Expired, Failed

### Community 83 - "Entity"
Cohesion: 0.50
Nodes (3): Entity, CreatedAtUtc, Id

### Community 84 - ".oxlintrc.json"
Cohesion: 0.33
Nodes (5): plugins, rules, react/only-export-components, react/rules-of-hooks, $schema

### Community 85 - "React + TypeScript + Vite"
Cohesion: 0.50
Nodes (3): Expanding the Oxlint configuration, React Compiler, React + TypeScript + Vite

### Community 86 - "IClock"
Cohesion: 0.50
Nodes (4): IClock, UtcNow, SystemClock, UtcNow

## Knowledge Gaps
- **317 isolated node(s):** `Deny`, `Grant`, `IsConfigured`, `BaseDn`, `BindDn` (+312 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 633 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **11 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `کارهای‌انجام‌شده‌در‌این‌نسخه` connect `ReportQuery` to `Mission`, `PermissionCatalog`, `TimelinePage.tsx`, `toFa`, `EndpointMapper`, `ناوگان نوین — Nova Fleet Management`?**
  _High betweenness centrality (0.155) - this node is a cross-community bridge._
- **Are the 3 inferred relationships involving `Mission` (e.g. with `کارهای‌انجام‌شده‌در‌این‌نسخه` and `MissionService.CreateAsync()`) actually correct?**
  _`Mission` has 3 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Deny`, `Grant`, `IsConfigured` to the rest of the system?**
  _317 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `CancellationToken` be split into smaller, more focused modules?**
  _Cohesion score 0.061938061938061936 - nodes in this community are weakly interconnected._
- **Why does `formatFaDateTime()` connect `toFa` to `ReportQuery`, `TimelinePage.tsx`, `types.ts`, `main.tsx`, `SecurityPage.tsx`, `MissionsPage.tsx`, `ReportsPage.tsx`?**
  _High betweenness centrality (0.136) - this node is a cross-community bridge._
- **Should `ValidationAppException` be split into smaller, more focused modules?**
  _Cohesion score 0.12773109243697478 - nodes in this community are weakly interconnected._
- **Why does `Mission` connect `Mission` to `DateTimeOffset`, `ValidationAppException`, `ReportQuery`, `EntityTypeBuilder`, `EndpointMapper.MapMissionEndpoints`, `Mission.ApplyApprovedChanges`, `SmsDispatcher`, `MissionChangeInput`, `Task`, `Entity`, `MissionChangeRequest`, `NovaFleetDbContext`, `NovaFleet.Domain.Missions`?**
  _High betweenness centrality (0.119) - this node is a cross-community bridge._