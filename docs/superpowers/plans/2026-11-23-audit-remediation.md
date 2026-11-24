# Nova Fleet Audit Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. This repository adds a stricter gate: after each stage, report fresh evidence and wait for explicit user approval before committing or starting the next stage.

**Goal:** Fix every confirmed issue from the 2026-11-23 whole-repository audit without introducing paid SMS sends, exposing live data, weakening test guards, or overwriting pre-existing installer work.

**Architecture:** Keep the existing ASP.NET Core Minimal API / EF Core / React structure. Put shared authorization and validation rules in application/infrastructure helpers, make multi-row or multi-key writes transactional, and use SQL-level claiming for work shared by multiple processes. Preserve the current UI language and conventions. Execute twelve independently verifiable stages; each stage ends with a report and an approval-gated commit.

**Tech Stack:** .NET 10 target (`global.json` currently pins SDK 10.0.403), EF Core 10, SQL Server, xUnit, React 19, TypeScript, Vite, React Query, QuestPDF, ClosedXML, PowerShell, Inno Setup, IIS.

**Spec:** This file is also the audit baseline and design spec because the user explicitly requested one Markdown file. The authoritative audit transcript is `C:\Users\Shahab\.qoder\projects\D--Projects-ASP-net-Nova-Fleet-Management\cce50cdb-cd6d-48fe-a750-0ccf1afa6b67.jsonl`, record 846. Source paths and line numbers below refer to the state observed on 2026-11-23 and must be rechecked before editing.

## Global Constraints

- All new and changed behavior is developed test-first. Run the named regression test and observe it fail before implementation, then run it again and observe it pass.
- Never send a real or billable SMS. Use `Simulation`, fake gateways, or recorded fixtures. Do not click a real-gateway test button in the browser.
- Never authenticate as the user, paste/store/echo credentials, read or print `bootstrap.json`, or put secrets in test output or commits.
- Local database work is limited to LocalDB `(localdb)\MSSQLLocalDB` and explicitly guarded test databases. Never drop, reset, or point setup at `10.7.1.42/NovaFleetDb` or `novafleet_app`.
- Keep the maintenance integration guard unchanged: it requires both `DataSource == "localhost"` and `InitialCatalog == "NovaFleet_Maintenance_Test_20261116"`. Never weaken or spoof it.
- The remote database `10.7.1.42` is authorized only for browser acceptance against the isolated database `NovaFleet_Maintenance_Test_20261116`, never against the live IIS site or live database.
- Preserve pre-existing uncommitted installer work: `installer/Install-Iis.ps1`, `installer/NovaFleet.iss`, `installer/build.ps1`, `src/NovaFleet.Api/Program.cs`, `installer/Test-Upgrade.ps1`, `installer/Test-UpgradeSql.ps1`, `installer/Upgrade-Iis.ps1`, `src/NovaFleet.Api/Middleware/InstallerVerificationMiddleware.cs`, `tests/NovaFleet.IntegrationTests/InstallerVerificationMiddlewareTests.cs`, and the local `publish-new/` output. Inspect the current diff before each edit; do not revert or include unrelated changes.
- Do not push. After a stage is fully verified and explicitly approved, commit only that stage's named files. Approval for one commit is not approval for future commits or a push.
- Keep Persian UX: Jalali dates and Tehran time at UI boundaries, names rather than SIDs, grouped dropdowns, red `*` on required fields, icons where the current design uses icons, confirmation dialogs, and dark-mode-safe icons.
- Do not change `global.json` merely to make the installed SDK run. If SDK 10.0.403 is unavailable, use the already-installed SDK only as a documented build workaround; report the actual SDK and do not silently change the repository's SDK contract.
- Every stage must leave the application buildable and existing tests passing. No half-finished public interfaces or migrations.
- Before deleting supposedly unused code, rerun a whole-tree search including tests, scripts, docs, and dynamic/string references. Exclude generated `graphify-out/` artifacts from source searches but inspect string references in application code.

## Verification Commands

Run from `D:\Projects\ASP.net\Nova Fleet Management`. Use the first command when the pinned SDK is installed; the second is the known workaround on the current machine, not a replacement for the version contract.

```bash
dotnet restore NovaFleet.sln
dotnet build NovaFleet.sln --no-restore
dotnet test tests/NovaFleet.UnitTests/NovaFleet.UnitTests.csproj --no-build
dotnet test tests/NovaFleet.IntegrationTests/NovaFleet.IntegrationTests.csproj --no-build
```

```bash
dotnet "C:\Program Files\dotnet\sdk\11.0.100\MSBuild.dll" NovaFleet.sln -restore -p:Configuration=Debug -v:minimal
```

Frontend commands, from `web/`:

```bash
npm ci
npm run lint
npm run build
```

Installer verification, from the repository root:

```bash
pwsh -NoProfile -File installer/Test-Upgrade.ps1
pwsh -NoProfile -File installer/Test-UpgradeSql.ps1
```

A stage is not verified merely because a previous stage passed. Run all affected test projects, the frontend checks when frontend files changed, and the relevant integration/installer checks. Record command, exit code, counts, skipped tests, and any known limitation in the stage report. SQL integration tests that cannot run must be reported as skipped/blocked, never as passed.

## Stage Map and Commit Boundaries

| Stage | Scope | Commit when approved |
|---|---|---|
| 1 | Setup isolation, production HTTPS/Development fix, logo validation | `fix(security): secure first-run setup and production bootstrap` |
| 2 | Local-admin authorization, login lockout/rate limit, session revocation/termination permission, CSRF, AD groups | `fix(security): harden identity and session administration` |
| 3 | Shared `missions.view.own` enforcement and mission cache isolation | `fix(authorization): enforce own-mission visibility everywhere` |
| 4 | Atomic SMS settings, explicit recipient semantics, frontend failed-GET protection | `fix(settings): make service configuration atomic and explicit` |
| 5 | SMS atomic claim, indeterminate outcomes, stale recovery, stored gateway mode, reconciliation | `fix(sms): prevent duplicate sends and reconcile uncertain delivery` |
| 6 | Running-mission vehicle lock and optimistic change-request approval | `fix(missions): protect odometer and change-request integrity` |
| 7 | Resource serialization, exact overlap, split eligibility, archive races, worker bounds, withdrawal/timeline bounds | `fix(missions): serialize resource allocation and bound background work` |
| 8 | Requesting-group archive migration and safe rollback | `fix(migrations): preserve requesting-group history and safe rollback` |
| 9 | Reproducible installer, HTTPS-only production, fresh recovery, obsolete-file cleanup | `fix(installer): make production install and upgrade recoverable` |
| 10 | Excel text safety, complete totals/truncation contract, Jalali date boundaries and API/search fixes | `fix(reports): correct spreadsheet safety and date/API contracts` |
| 11 | Atomic composite saves, avatar payload reduction, route code splitting, unused settings/no-op realtime cleanup | `fix(app): remove unsafe and nonfunctional application paths` |
| 12 | PR/main CI, lint cleanup, documentation and final verification | `test(ci): enforce full verification and refresh documentation` |

## Audit Coverage Matrix

Each number maps to one of the twenty findings in the final audit. Extra findings are listed afterward.

| Audit finding | Planned stage(s) |
|---|---|
| 1. Fresh install HTTP/Development and anonymous setup | 1, 9 |
| 2. `users.manage` escalates to full administrator | 2 |
| 3. Password changes do not revoke sessions; view permission permits force-end | 2 |
| 4. Dashboard/reports ignore `missions.view.own` | 3 |
| 5. React Query cache leaks between accounts | 3 |
| 6. Failed settings GET can overwrite real configuration with defaults | 4 |
| 7. SMS settings writes are non-atomic and enable too early | 4 |
| 8. SMS queue can duplicate, lose, or falsely mark delivery | 5 |
| 9. Empty recipients re-enable defaults | 4 |
| 10. Excel formula injection | 10 |
| 11. Vehicle change during a running mission corrupts odometer history | 6 |
| 12. Stale change approval overwrites newer mission state | 6 |
| 13. Allocation races, inclusive boundaries, split eligibility, archive races | 7 |
| 14. RequestingGroup data loss and unsafe migration rollback | 8 |
| 15. Installer is not clean-checkout reproducible and stage is not cleaned | 9, 12 |
| 16. Withdrawal timeout uses planned end instead of request time | 7 |
| 17. Date end boundary and 1000-row totals are wrong | 10 |
| 18. Frontend/API mismatches and partial saves | 10, 11 |
| 19. Unbounded workers/timeline and missing SQL/frontend/CI coverage | 7, 12 |
| 20. Unused settings, no-op realtime, large frontend bundle | 11 |

Additional confirmed findings covered by this plan:

- `/api/auth/me` treats network/500 failures as logout (stage 3).
- AD group enumeration lacks authorization (stage 2).
- Logout and change-password are incorrectly CSRF-exempt (stage 2).
- No login rate limiting or persistent local-account lockout (stage 2).
- Organization logo has no validation (stage 1).
- Local-admin password reset and protected-role assignment are not restricted (stage 2).
- Local-admin disabling is already blocked by `ApplicationUser.Disable()`; stage 2 adds regression coverage and closes the stale sub-finding without reimplementing it.
- Fresh install has no recovery journal (stage 9).
- Upgrade leaves obsolete payload files (stage 9).
- README copy command can create `wwwroot/dist`, and several security/testing statements are stale (stage 12).
- `publish-new/` is not ignored (stage 9).
- Five unused public API candidates, duplicate domain helpers, and duplicate mission validation require re-verification before deletion (stage 11).
- The historical frontend lint baseline had 25 warnings and zero errors; stage 12 must measure and remove them, not assume the count.
- List/options APIs carry full avatar data URLs (stage 11).
- SMS `Api` mode is already implemented; stage 5 adds contract tests and closes the stale finding rather than adding another implementation.
- Real PayamSMS delivery behavior is not verified and must remain unverified without paid sends; simulation and fake-gateway tests are the acceptance evidence.

## Shared Interfaces Defined by This Plan

Later stages consume these exact names and contracts. A stage may move a type to a more appropriate existing file, but must not rename it without updating all consumers and this plan.

```csharp
public static IQueryable<Mission> Apply(
    IQueryable<Mission> source,
    bool canViewAll,
    string username);

public sealed record SettingWrite(string Key, string? Value, bool IsSecret);

public interface IRuntimeSettings
{
    Task SetBatchAsync(IReadOnlyList<SettingWrite> writes, CancellationToken cancellationToken = default);
}
```

```csharp
public sealed record SmsSendResult(
    bool Accepted,
    string? ProviderReference,
    string? Error,
    bool IsPermanentFailure,
    string? RawStatus,
    bool IsIndeterminate = false)
{
    public static SmsSendResult Indeterminate(string error, string? rawStatus = null) =>
        new(false, null, error, false, rawStatus, true);
}

public enum SmsGatewayMode
{
    Simulation = 0,
    Wsdl = 1,
    Api = 2
}

public enum SmsQueueStatus
{
    Queued = 1,
    Sending = 2,
    Accepted = 3,
    Delivered = 4,
    RetryScheduled = 5,
    Failed = 6,
    PermanentFailure = 7,
    Cancelled = 8,
    Indeterminate = 9
}
```

`SmsGatewayMode` moves from `SmsGatewayRouter` to `ISmsGateway.cs` so the domain queue item can store it without an Infrastructure dependency. `SmsQueueItem` gains `SmsGatewayMode GatewayMode`, `DateTimeOffset? SendStartedAtUtc`, and `DateTimeOffset? NextDeliveryPollAtUtc`. `Indeterminate` is never automatically resent. An authorized user may explicitly mark it failed (manual closure) or return it to queued only through a dedicated reconciliation endpoint after reviewing the provider reference.

```csharp
public sealed record MissionChangeRequestSnapshot(
    string BaseRowVersion,
    MissionStatus BaseStatus,
    DateTimeOffset BasePlannedStartAtUtc,
    DateTimeOffset BasePlannedEndAtUtc);
```

```csharp
public sealed record ReportTableDto(
    string Key,
    string Title,
    IReadOnlyList<ReportColumnDto> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyDictionary<string, object?> Totals,
    DateTimeOffset GeneratedAtUtc,
    int TotalCount,
    string? Subtitle = null,
    bool RowsTruncated = false,
    int? MaxRows = null);
```

## Stage 1 — First-Run and Production Bootstrap Security

**Problem and trigger:** Any anonymous network client can call `/api/setup/database` or `/api/setup/initialize` before the first user exists and create the local administrator. A configured database with zero users is also considered uninstalled. The production installer forces `Development`, disabling HTTPS/HSTS and secure-cookie behavior. Organization logos are accepted as arbitrary data URLs.

**Files:**
- Modify: `src/NovaFleet.Api/Endpoints/EndpointMapper.cs`
- Modify: `src/NovaFleet.Api/NovaFleet.Api.csproj` (friend assembly for the internal setup mapper)
- Modify: `src/NovaFleet.Infrastructure/Setup/SetupService.cs`
- Modify: `src/NovaFleet.Infrastructure/Settings/SettingsWriters.cs`
- Modify: `src/NovaFleet.Infrastructure/Settings/SettingsService.cs`
- Modify: `installer/web.config.template`
- Modify: `README.md` only in the setup subsection changed by this stage
- Test: `tests/NovaFleet.IntegrationTests/SetupEndpointSecurityTests.cs` (create)
- Modify: `src/NovaFleet.Infrastructure/Security/AvatarImageValidator.cs`
- Test: `tests/NovaFleet.UnitTests/OrganizationSettingsValidationTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/Security/PngFixture.cs` (create; shared deterministic PNG fixtures)
- Modify: `tests/NovaFleet.UnitTests/Setup/SetupWizardTests.cs` (replace invalid `AAA` logo fixture)

**Interfaces:**
- Consumes: existing setup/auth endpoints, existing ASP.NET connection metadata, existing organization DTO.
- Produces: setup mutations are allowed only when `Connection.RemoteIpAddress` is loopback; all non-loopback mutations return HTTP 403 before any service write. Status endpoints may remain anonymous and must not return secrets.

- [x] **Step 1: Add failing setup-boundary tests.** Build an endpoint test context with loopback and non-loopback remote addresses. Assert both `/api/setup/database` and `/api/setup/initialize` are rejected with 403 from a non-loopback address and perform zero user/settings writes. Add a test proving a database that already contains a user still returns conflict.
- [x] **Step 2: Run the setup tests and observe failure.** Build `tests/NovaFleet.IntegrationTests/NovaFleet.IntegrationTests.csproj`, then run `dotnet "C:/Program Files/dotnet/sdk/11.0.100/vstest.console.dll" tests/NovaFleet.IntegrationTests/bin/Debug/net10.0/NovaFleet.IntegrationTests.dll /TestCaseFilter:FullyQualifiedName~SetupEndpointSecurityTests`. The direct runner is required because `global.json` pins unavailable SDK 10.0.403. Expected: anonymous non-loopback setup currently reaches the handler and returns 200 instead of 403.
- [x] **Step 3: Enforce loopback before setup services.** Add an endpoint-group policy or minimal endpoint filter using the actual connection address, not a client-supplied forwarded header. Do not allow configuration to disable the restriction. Return `Results.StatusCode(StatusCodes.Status403Forbidden)`.
- [x] **Step 4: Add failing logo tests and implement bounded validation.** Reject non-PNG data URLs, malformed base64, images over 256 KiB decoded, non-square images, images over 256x256, and trailing PNG/IDAT data before persisting `LogoDataUrl`. Add `AvatarImageValidator.ValidateOrganizationLogo(string?)`; share the existing private PNG validation core by parameterizing its Persian error messages, so avatar behavior and messages remain unchanged. Accept null as no logo and normalize valid PNG data. Do not add an image dependency.
- [x] **Step 5: Remove Development from the production template.** Set `ASPNETCORE_ENVIRONMENT` to `Production` in `installer/web.config.template`; preserve explicit environment overrides for local development outside the installer template.
- [x] **Step 6: Verify.** Run the filtered tests, full unit tests, build, and template/installer checks affected by this change. Verify HTTPS/HSTS and secure-cookie branches in `Program.cs` are now selected by the template. Update only the changed setup documentation.
- [x] **Step 7: Report and wait.** Report commands, counts, files changed, and the operational consequence: initial setup is performed on the server (for example through RDP/localhost), not from an arbitrary remote browser. Do not commit until explicitly approved.

**Acceptance criteria:** Non-loopback setup mutation is impossible; valid first-run still works locally; invalid logos never reach persistence; installer template selects Production; no secret or real database is used in tests.

**Risk:** Remote first-run convenience is intentionally removed. Stage 9 will make the installer open `https://<certificate-hostname>:<port>/setup` on the server and document the supported remote-admin path.

**Dependencies:** None. Stage 9 depends on this production-environment decision.

**Stage 1 verification (2026-11-23):**
- Red phase: filtered organization/setup logo tests produced 25 expected failures and 15 passes, proving arbitrary logos were accepted and valid logos were not normalized.
- Independent review found an IDAT trailing-data bypass; its new regression test failed 1/20 before the fix. The final focused organization/setup tests passed 41/41, existing profile-photo tests passed 34/34, and a follow-up review found no remaining Critical or Important issues.
- Full solution build exited 0 with the installed SDK 11.0.100 workaround; pinned SDK 10.0.403 remains unchanged and unavailable on this machine.
- Unit tests passed 382/382. Integration tests passed 30, failed 0, and skipped 5 existing SQL Server-guarded tests.
- The web.config template parsed and rendered successfully with `ASPNETCORE_ENVIRONMENT=Production`; the existing `requireHttps` branch selects HSTS, HTTPS redirection, and always-secure session cookies.
- IIS installer end-to-end execution was not run; full HTTPS installer behavior remains Stage 9 scope.

## Stage 2 — Identity, Session, CSRF, and Login Protection

**Problem and trigger:** A user with only `settings.users.manage` can create/promote protected administrators, assign `SystemAdministrator`, or reset a local administrator's password. Password changes leave existing sessions valid. `security.sessions.view` incorrectly authorizes forced termination. Login has no throttling or persistent lockout. Logout/change-password bypass CSRF. Any authenticated user can enumerate AD groups.

**Files:**
- Modify: `src/NovaFleet.Application/Authorization/PermissionCatalog.cs`
- Modify: `src/NovaFleet.Infrastructure/Identity/UserService.cs`
- Modify: `src/NovaFleet.Infrastructure/Identity/AuthService.cs`
- Modify: `src/NovaFleet.Infrastructure/Identity/RoleService.cs`
- Modify: `src/NovaFleet.Infrastructure/Sessions/SessionService.cs`
- Modify: `src/NovaFleet.Domain/Identity/ApplicationUser.cs`
- Modify: `src/NovaFleet.Api/Endpoints/EndpointMapper.cs`
- Modify: `src/NovaFleet.Api/Program.cs`
- Modify: `src/NovaFleet.Api/Middleware/ApiExceptionHandlerMiddleware.cs`
- Create: `src/NovaFleet.Api/Middleware/LoginRateLimitFilter.cs`
- Modify: `src/NovaFleet.Infrastructure/Persistence/Configurations/IdentityConfiguration.cs`
- Create: one EF migration adding `AccessFailedCount` and `LockoutEndAtUtc` to `Users`
- Modify: `src/NovaFleet.Infrastructure/Setup/DatabaseInitializer.cs` for default-role mapping
- Modify: `web/src/pages/SecurityPage.tsx`, `web/src/components/UserProfileForm.tsx`, and `web/src/layout/AppLayout.tsx`
- Test: `tests/NovaFleet.UnitTests/ProtectedAdministratorTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/SessionSecurityTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/RoleServiceSecurityTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/LoginLockoutTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/LoginLockoutMigrationTests.cs` (create)
- Test: `tests/NovaFleet.IntegrationTests/CsrfProtectionTests.cs` (create)
- Test: `tests/NovaFleet.IntegrationTests/DirectoryGroupAuthorizationTests.cs` (create)
- Test: `tests/NovaFleet.IntegrationTests/LoginRateLimitTests.cs` (create)
- Test: `tests/NovaFleet.IntegrationTests/LoginLockoutConcurrencyTests.cs` (guarded SQL, create)
- Test: `tests/NovaFleet.IntegrationTests/ProtectedAdministratorConcurrencyTests.cs` (guarded SQL, create)
- Test: `tests/NovaFleet.IntegrationTests/UserStatusEndpointTests.cs` (create)

**Interfaces:**
- Consumes: `ICurrentUser`, existing sessions, password hasher, permission catalog.
- Produces: `PermissionCatalog.Security.SessionsManage = "security.sessions.manage"`; only an authenticated, active, database-backed local admin may create local admins, change protected role membership/default role/AD mapping, or reset a local-admin/protected-role account's password. No caller may disable a local administrator. Local login failure fields are `int AccessFailedCount` and `DateTimeOffset? LockoutEndAtUtc`.

- [x] **Step 1: Write failing authorization tests.** Cover create-local-admin, protected-role assignment/removal, local-admin password reset, and attempted disabling of a local administrator. Assert a non-local-admin with `users.manage` receives 403 and changes nothing. Assert an active local admin can create local admins, change protected role membership, and reset local-admin passwords, but no caller can disable any local administrator. There is no current promotion/demotion endpoint; do not add one, and keep `MarkLocalAdmin` unavailable to ordinary user-management callers.
- [x] **Step 2: Run tests and observe failure.** Use `--filter FullyQualifiedName~ProtectedAdministratorTests`.
- [x] **Step 3: Implement protected-administrator checks in shared service methods.** Define protected roles by ordinal-insensitive name `SystemAdministrator`. Require `currentUser.IsAuthenticated && currentUser.IsLocalAdmin` for protected changes. Load the caller from the database and require `!IsDisabled && IsLocalAdmin`; do not trust only a stale claim. Keep the domain invariant that every local administrator is disable-protected; do not weaken it to a last-admin-only check.
- [x] **Step 4: Close the stale disable finding with a regression test.** Assert `ApplicationUser.Disable()` still throws for `IsLocalAdmin`, and `users.manage` cannot disable any local admin.
- [x] **Step 5: Add failing session/CSRF tests and implement them.** Change forced termination to require `SessionsManage`; keep `SessionsView` for listing. Add a dedicated catalog entry and default-role mapping. After password change/reset, delete all sessions for that username before returning success. CSRF exemption is limited to anonymous login and loopback-protected setup mutation; logout and change-password require `X-Requested-With: XMLHttpRequest`.
- [x] **Step 6: Add failing login tests and implement persistent lockout.** Apply an endpoint rate limiter keyed by normalized username plus client IP, with fixed-window limits of 5 failed attempts per minute and 20 per hour per IP. Persist local-account failures: lock for 15 minutes after 5 consecutive failures; successful login resets count and lockout; disabled accounts remain rejected. Return a generic Persian credential error and audit lockout without revealing whether a directory account exists.
- [x] **Step 7: Restrict directory-group enumeration.** Require `PermissionCatalog.Administration.AuthorizationManage` on `/roles/directory-groups`; add endpoint tests for 401/403 and authorized access.
- [x] **Step 8: Update callers and migrate.** Add the permission to role defaults/UI and migration for lockout fields. In `SecurityPage.tsx`, replace `canForceEnd={can('settings.sessions')}` with `canForceEnd={can('security.sessions.manage')}` while keeping the sessions tab under `security.sessions.view`. Verify frontend sends the CSRF header for logout/change-password and handles 401 consistently.
- [x] **Step 9: Verify.** Run filtered tests, full unit/integration tests, frontend lint/build, and migration/build checks. Do not use a real AD account or user credentials.
- [x] **Step 10: Report and wait.** Include migration name, permission default mapping, rate-limit values, session-revocation behavior, review findings/fixes, and any skipped integration environment. Do not commit or begin Stage 3 before explicit approval.

**Acceptance criteria:** Protected administration cannot be reached through ordinary user-management permission; no local administrator can be disabled and protected-role password reset requires an active local admin; password change/reset invalidates old sessions even under concurrent login; CSRF and AD enumeration tests pass; lockout recovers automatically after 15 minutes.

**Risk:** A lost local-admin password can no longer be reset by a delegated user manager; recovery must use the documented database/admin recovery path. Rate limits can temporarily block shared-NAT users, so IP limits are deliberately looser than account limits. Current counters are process-local and must be replaced by a shared limiter before multi-instance deployment.

**Dependencies:** None. Stage 3 reuses session invalidation behavior.

**Stage 2 verification (2026-11-24):**
- Red-phase tests exposed ordinary-manager access to protected current/default/mapping state; the guarded SQL race reproduced deletion of a newly assigned `SystemAdministrator` membership. Current plus requested protected state is now authorized inside serializable transactions, and SQL Server same-user writes are serialized with `UPDLOCK, HOLDLOCK` while SQLite keeps the portable EF path.
- Final-review regression tests reproduced two password/session races: a login that verified the old password could create an active session after password change/reset deleted sessions. SQL Server user-row locking now serializes login-session creation with password mutation, and change-password revalidates the hash after locking. Both race tests failed before the fix and passed afterward, including three additional Release repetitions.
- Another failing test proved an ordinary user manager could reset the password of a non-local `SystemAdministrator` member. Reset now requires an active database-backed local admin for protected-role accounts as well as local-admin accounts. The reset transaction begins before the locked read and covers authorization, password update, and session deletion.
- Independent review also found the inverted frontend active/disabled request and hidden FleetManager Security navigation; both were corrected. A second review found rollback retained `security.sessions.view`, which base code accepted for force-end. `Down` now fails closed by revoking both session permissions; its regression test failed before the fix. The tradeoff is that an explicitly pre-existing FleetManager View grant is also removed on downgrade and can be re-granted intentionally afterward.
- A second independent read-only review after these fixes reported no remaining Critical or Important findings across authorization, password/session concurrency, permissions, CSRF, lockout/throttling, migration behavior, frontend mapping, and tests.
- Final focused administrator/session unit tests passed 20/20. The combined guarded LocalDB login/protected-administrator tests passed 5/5, including five simultaneous failed logins, the correct-password/lockout race, and both password/session races.
- Full Debug and Release builds exited 0 with zero warnings/errors using installed SDK 11.0.100 directly; pinned SDK 10.0.403 remains unchanged and unavailable on this machine.
- Debug and Release each passed 423/423 unit tests and 60 integration tests with 0 failures; 5 existing maintenance SQL tests were skipped because `NOVAFLEET_MAINTENANCE_TEST_SQL` is not configured.
- EF reported no pending model changes. Real LocalDB downgrade/re-upgrade with a seeded FleetManager role removed both session permissions on downgrade, restored exactly two on upgrade, and produced zero duplicate permission groups. After the final rerun, the isolated Stage 2 LocalDB database was removed and no temporary Stage 2 SQL scripts were found.
- Frontend lint completed with 0 errors and 25 existing warnings in unrelated files; changed frontend files linted with 0 warnings/errors. Production TypeScript/Vite build passed with the existing large-chunk warning.
- No real AD account, user authentication, paid SMS, remote organization database, or live IIS site was used. Authenticated UI/browser acceptance remains unverified; anonymous browser inspection was blocked by two indistinguishable connected Chrome instances. Failure-rate counters remain process-local and require a shared limiter before multi-instance deployment.

## Stage 3 — Own-Mission Visibility and Cross-Account Cache Isolation

**Problem and trigger:** Dashboard counts/chart, mission search, reports, exports, timeline, and detail paths accept `missions.view.own` but expose all missions. React Query is never cleared on logout/401 and notification keys are not scoped by user. `/api/auth/me` network/500 errors incorrectly log the user out.

**Files:**
- Create: `src/NovaFleet.Application/Missions/MissionVisibility.cs`
- Modify: `src/NovaFleet.Infrastructure/Missions/MissionService.cs`
- Modify: `src/NovaFleet.Infrastructure/Dashboard/DashboardService.cs`
- Modify: `src/NovaFleet.Infrastructure/Reports/ReportService.cs`
- Modify: `src/NovaFleet.Infrastructure/Timeline/TimelineService.cs`
- Modify: `MissionService.GetAsync` and `MissionService.ExportOrderPdfAsync` in `src/NovaFleet.Infrastructure/Missions/MissionService.cs` so detail and PDF honor the same visibility predicate
- Modify: `web/src/auth/AuthContext.tsx`
- Modify: `web/src/main.tsx`
- Modify: `web/src/lib/api.ts`
- Modify: `web/src/layout/AppLayout.tsx`
- Modify cache keys and invalidations in `web/src/pages/DashboardPage.tsx`, `MissionsPage.tsx`, `MissionDetailsPage.tsx`, `ReportsPage.tsx`, `TimelinePage.tsx`, `SmsPage.tsx`, `SecurityPage.tsx`, `SettingsPage.tsx`, `ProfilePage.tsx`, `web/src/components/ConnectionsSettings.tsx`, `UserProfileForm.tsx`, `MissionForm.tsx`, and `web/src/components/maintenance/MaintenanceReports.tsx`, `OdometerPanel.tsx`, `RecordDetails.tsx`
- Test: `tests/NovaFleet.UnitTests/MissionVisibilityTests.cs` (create)
- Modify: `web/package.json`, `web/package-lock.json`, `web/vite.config.ts`
- Create: `web/src/test/setup.ts`
- Test: `tests/NovaFleet.UnitTests/DashboardVisibilityTests.cs` (create)
- Test: `tests/NovaFleet.IntegrationTests/MissionVisibilityIntegrationTests.cs` (create)
- Test: `web/src/auth/AuthContext.test.tsx` and `web/src/lib/api.test.ts` (create)

**Interfaces:**
- Consumes: `ICurrentUser`, existing mission queries, React Query client.
- Produces: `MissionVisibility.Apply(IQueryable<Mission>, bool canViewAll, string username)`; keys involving private data include `user?.username`; only an HTTP 401 clears authentication.

- [x] **Step 1: Write failing backend visibility tests.** Seed missions for two requesters. A user with only `missions.view.own` must see only their own rows and counts in search, dashboard, weekly chart, each report, timeline, detail, and export. A user with `missions.view` sees all.
- [x] **Step 2: Run tests and observe failure.** Expected: own-only user currently receives other requesters' data.
- [x] **Step 3: Add and apply the shared predicate.** Implement `source.Where(item => item.RequesterUsername == username)` when `canViewAll` is false. Apply before counts, grouping, chart aggregation, paging, exports, and detail lookup. Keep server-side `canViewAll` derived from permissions, never a client flag.
- [x] **Step 4: Add the frontend test runner and write failing auth/cache tests.** From `web/`, run `npm install --save-dev vitest @testing-library/react @testing-library/jest-dom jsdom`; add `test: vitest run` to `package.json`, configure the `jsdom` environment and `src/test/setup.ts` in `vite.config.ts`. Simulate `/api/auth/me` returning network error and 500: existing user remains. Simulate 401: user clears and cache cleanup runs. Simulate logout and account switch: no cached notifications or private queries remain.
- [x] **Step 5: Fix auth/cache handling.** Distinguish 401 from other failures. On logout, 401, and login as a different username, call `queryClient.cancelQueries()` followed by `queryClient.clear()`. Scope notification keys as `['notifications', user?.username]`; include the username in private keys for `dashboard`, `missions`, `mission`, `reports`, `timeline`, `sms`, `sms-summary`, `sms-templates`, `users`, `sessions`, `audit`, `settings`, `connections-*`, `profile`, and `maintenance`. Public `setup-status` stays unscoped. Update matching invalidations in the same files.
- [x] **Step 6: Verify.** Run filtered backend tests, full backend tests, frontend tests/lint/build, and available integration tests. If browser access is available without logging in as the user, verify login/401 surfaces only; do not enter credentials.
- [x] **Step 7: Report and wait.**

**Acceptance criteria:** Own-only visibility is consistent across every mission surface; switching accounts cannot reveal prior cached data; transient `/me` failures do not log users out.

**Risk:** Shared dashboard/report query code must be updated together; missing one export path is a security failure. Stage 10 continues date/totals work in `ReportService` and must preserve this predicate.

**Dependencies:** None. Stage 10 consumes `MissionVisibility.Apply`.

**تأیید مرحله ۳ (۲۰۲۶-۱۱-۲۴):**
- شواهد RED شامل ۱۴ شکست انتظار شده برای دسترسی own-only، یک regression داشبورد (Expected `0`، Actual `1`) و اجرای کامل تست‌های فرانت‌اند روی base پیش از Stage 3 بود؛ در اجرای base، ۱۲ تست از ۱۳ تست auth/cache و query-key شکست خوردند و فقط کنترل مثبت same-user موفق بود. تست file-download 401 نیز روی base شکست خورد.
- بازبینی مستقل دو-round انجام شد. یافتن‌های refresh حساب، ساعت‌های freeze‌شده، RED ناقص و assertion ناقص PDF رفع شدند؛ mutation-proof ثابت کرد assertion mission-status نشت approved=1/pending=2/total=3 را می‌گیرد. بازبینی نهایی بدون finding Critical/Important تأیید شد.
- تأیید نهایی تازه با SDK نصب‌شده `11.0.100`: ساخت Debug و Release بدون هشدار/خطا؛ در هر پیکربندی ۴۲۸ تست واحد و ۷۷ تست یکپارچه گذشت و ۱۰ تست به‌علت نبود متغیرهای SQL محیطی skip شدند.
- `npm test` در `web/` با ۱۷/۱۷ تست سبز شد. lint کامل ۰ خطا و ۶ هشدار موجود و نامرتبط داشت؛ فایل‌های تغییرکرده Stage 3 بدون هشدار/خطا lint شدند. `npm run build` موفق بود و فقط هشدار قبلی chunk بزرگ را نشان داد.
- فقط HTTP 401 پاک‌سازی احراز هویت را فعال می‌کند؛ خطای شبکه/403/500 کاربر را حفظ می‌کند. خروج، 401 و تغییر حساب از جمله تغییر مشاهده‌شده در `refresh()` پس از `cancelQueries()` کش را `clear()` می‌کنند و `setup-status` بدون scope عمومی باقی ماند. هیچ ورود واقعی، SMS واقعی، دیتابیس production یا IIS زنده استفاده نشد.
- کامیت‌های Stage 3: `15df70e` (پیاده‌سازی)، `5247c11` (رفع یافتن‌های round اول) و `fe52c51` (تقویت شواهد و assertion‌های round دوم).

## Stage 4 — Atomic Service Settings and Recipient Semantics

**Problem and trigger:** Connection forms submit plausible defaults after a failed GET. SMS settings are enabled before connection/mode/secrets are saved, and each key commits separately. Empty/invalid recipient selections silently restore defaults, making it impossible to intentionally notify nobody.

**Files:**
- Modify: `src/NovaFleet.Application/Abstractions/IRuntimeSettings.cs`
- Modify: `src/NovaFleet.Infrastructure/Settings/RuntimeSettings.cs`
- Modify: `src/NovaFleet.Infrastructure/Settings/SettingsWriters.cs`
- Modify: `src/NovaFleet.Application/Notifications/SmsDefaults.cs`
- Modify: `src/NovaFleet.Infrastructure/Sms/SmsService.cs`
- Modify: `web/src/components/ConnectionsSettings.tsx`
- Modify: `web/src/pages/SmsPage.tsx` and `web/src/lib/types.ts` for explicit recipient selection
- Test: `tests/NovaFleet.UnitTests/RuntimeSettingsBatchTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/SmsRecipientSettingsTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/SmsSettingsWriterTests.cs` (create)
- Test: `web/src/components/ConnectionsSettings.test.tsx` (create)
- Test: `web/src/pages/SmsRecipients.test.ts` (create)

**Interfaces:**
- Consumes: `SettingWrite(Key, Value, IsSecret)` and `IRuntimeSettings.SetBatchAsync`.
- Produces: stored recipients are comma-separated known tokens, where empty/null key means defaults and the explicit token `none` means no recipients. Unknown tokens are validation errors.

- [ ] **Step 1: Write failing batch-write tests.** Use the SQLite test provider and a fake protector. Assert all supplied keys commit once; forced save failure leaves prior values unchanged; secret values are protected and never returned by ordinary reads.
- [ ] **Step 2: Run tests and observe failure**, then implement `SetBatchAsync`. Protect all secret plaintext values before one `SaveChangesAsync`. A null `Value` means leave unchanged; non-secret null stores empty string to preserve existing behavior.
- [ ] **Step 3: Write failing SMS-settings tests.** Assert enabling a real gateway with missing/invalid credentials fails and leaves `sms.enabled` false. Assert connection fields, mode, and secret are committed before enabled. Keep existing secret when the request omits it; blank submitted secret follows the existing clear behavior explicitly, never by accident.
- [ ] **Step 4: Implement atomic SMS writes.** Validate the resulting combined configuration, then write connection/mode/secrets and enabled state through one `SetBatchAsync`. No network call is made while validating.
- [ ] **Step 5: Write failing recipient tests.** Empty/unset setting returns defaults; `none` returns an empty list; `requester,driver` returns both; unknown or mixed unknown tokens throw `ValidationAppException`. Saving an intentionally empty UI selection stores `none`, not an empty string.
- [ ] **Step 6: Implement recipient semantics in both read and write paths.** Update UI validation and labels so «بدون گیرنده» is explicit. Do not silently filter unknown values.
- [ ] **Step 7: Write failing settings-form tests.** After GET failure, render a Persian error and Retry button; Save and Test are disabled; defaults are never submitted. After successful GET, re-enable them.
- [ ] **Step 8: Verify.** Run backend and frontend tests, full build/lint, and inspect that no test invokes the real SMS gateway.
- [ ] **Step 9: Report and wait.**

**Acceptance criteria:** Partial configuration writes are impossible; SMS cannot be enabled with unusable credentials; intentional no-recipient configuration remains empty; failed reads cannot overwrite settings.

**Risk:** Secret blank/omitted semantics must match the existing form. Stage 5 depends on the explicit recipient and batch-write contracts.

**Dependencies:** Stage 3 frontend test harness.

## Stage 5 — SMS Queue Safety and Delivery Reconciliation

**Problem and trigger:** Two workers can select the same queued item before it is marked `Sending`; final-save failure can strand it forever; `Sending` is cancellable; timeouts after provider acceptance are retried and may duplicate a paid message; polling uses current global mode and can poll every worker tick; switching to Simulation can falsely mark a real message delivered.

**Files:**
- Modify: `src/NovaFleet.Application/Abstractions/ISmsGateway.cs`
- Modify: `src/NovaFleet.Domain/Notifications/SmsQueueStatus.cs`
- Modify: `src/NovaFleet.Domain/Notifications/SmsQueueItem.cs`
- Modify: `src/NovaFleet.Infrastructure/Sms/SmsDispatcher.cs`
- Modify: `src/NovaFleet.Infrastructure/Sms/SmsGatewayRouter.cs`
- Modify: `src/NovaFleet.Infrastructure/Sms/PayamSmsGateway.cs`
- Modify: `src/NovaFleet.Infrastructure/Sms/PayamSmsRestGateway.cs`
- Modify: `src/NovaFleet.Infrastructure/Sms/SmsService.cs`
- Modify: `src/NovaFleet.Api/Endpoints/EndpointMapper.cs`
- Modify: `web/src/pages/SmsPage.tsx`, `web/src/lib/labels.ts`, and `web/src/lib/types.ts`
- Create: one EF migration for gateway mode, send start, next poll, and indeterminate state
- Test: `tests/NovaFleet.IntegrationTests/SmsQueueClaimIntegrationTests.cs` (create, SQL Server/LocalDB guarded)
- Test: `tests/NovaFleet.UnitTests/SmsDispatcherRecoveryTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/PayamSmsOutcomeTests.cs` (create)
- Test: `web/src/pages/SmsReconciliation.test.ts` (create)

**Interfaces:**
- Consumes: `SmsSendResult.IsIndeterminate`, `SmsQueueStatus.Indeterminate`, stored `GatewayMode`, `SendStartedAtUtc`, `NextDeliveryPollAtUtc`.
- Produces: an explicit reconciliation endpoint that either closes an indeterminate item as failed or requeues it after operator confirmation; it must not run automatically.

- [ ] **Step 1: Add failing claim and recovery tests.** Start two dispatchers against one due item in a guarded SQL test database; assert exactly one provider call. Simulate provider timeout after acceptance; assert indeterminate and no automatic resend. Simulate stale `Sending` older than 10 minutes; assert recovery to indeterminate, not queued. Assert cancellation is rejected once send starts.
- [ ] **Step 2: Run tests and observe failure.** If the SQL integration environment is unavailable, keep the task blocked and report it; do not claim the race is fixed from SQLite alone.
- [ ] **Step 3: Implement atomic claiming.** Move `SmsGatewayMode` to `ISmsGateway.cs` and update router references. In a short SQL Server transaction, claim a bounded batch using `UPDLOCK` and `READPAST`, setting `Status=Sending`, `SendStartedAtUtc`, `GatewayMode`, and `UpdatedAtUtc`. Commit the claim before calling the gateway. Store provider reference as soon as a response arrives. Provider I/O must never hold the claim transaction open.
- [ ] **Step 4: Implement idempotent completion.** Final update loads by ID and current status/version, and only transitions `Sending` to the result state. Catch save failures around the final update; the stale-recovery path handles abandoned rows. Do not overwrite an operator reconciliation or cancellation decision.
- [ ] **Step 5: Implement indeterminate outcomes.** Add the result factory and status. On timeout, unreadable response, or acceptance without a usable provider reference, record `Indeterminate`, error/raw status without secrets, and never schedule a retry. Add operator reconciliation UI/API with `sms.manage` authorization and audit record.
- [ ] **Step 6: Make polling mode-aware and scheduled.** Select the gateway from each item's stored `GatewayMode`, not current settings. Set `NextDeliveryPollAtUtc` after every poll using a bounded backoff (10 seconds, 30 seconds, 60 seconds, then 5 minutes, maximum 24 hours). Do not select items whose next poll is in the future. Simulation may mark delivered without polling a real gateway.
- [ ] **Step 7: Add stale-send recovery.** On worker startup and each bounded batch, move `Sending` rows older than 10 minutes to `Indeterminate` only when no completion occurred. Keep the provider reference and send start for investigation.
- [ ] **Step 8: Close the stale API-mode finding with contract tests.** Assert router `Api` calls REST gateway, `Wsdl` calls SOAP gateway, and stored mode remains stable across a settings switch. Do not send a real message.
- [ ] **Step 9: Verify.** Run fake-gateway unit tests, guarded SQL claim tests, migration/build, frontend tests/build, and full backend tests. Explicitly report that live PayamSMS delivery was not tested.
- [ ] **Step 10: Report and wait.**

**Acceptance criteria:** One queue item produces at most one in-flight send; uncertain paid outcomes are visible and never auto-resent; abandoned sends recover; polling uses the original gateway and bounded schedule; API mode is proven by tests.

**Risk:** Exactly-once external delivery cannot be proven after a network timeout. The safe contract is at-most-one automatic attempt plus explicit human reconciliation. SQL tests must not use the live database.

**Dependencies:** Stage 4 settings/recipient semantics. Stage 7 may reuse bounded batch patterns but must not change SMS claim semantics.

## Stage 6 — Mission Odometer and Change-Request Integrity

**Problem and trigger:** A running mission can change vehicle and erase its end odometer. A change request stores proposed JSON but no base version/schedule/status; approval can overwrite a newer mission, skip current validation, and leave `IsBackdated` stale.

**Files:**
- Modify: `src/NovaFleet.Domain/Missions/Mission.cs`
- Modify: `src/NovaFleet.Domain/Missions/MissionChangeRequest.cs`
- Modify: `src/NovaFleet.Infrastructure/Missions/MissionService.cs`
- Modify: `src/NovaFleet.Application/Missions/MissionModels.cs`
- Create: one EF migration for change-request base snapshot columns
- Modify: change-request UI if it displays/rejects stale proposals
- Test: `tests/NovaFleet.UnitTests/RunningMissionVehicleTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/MissionChangeApprovalTests.cs` (create)

**Interfaces:**
- Consumes: `MissionChangeRequestSnapshot` and existing serializable approval transaction.
- Produces: `Mission.ApplyChanges` throws while `Status == Running` if vehicle changes; approval requires exact base row-version/status/schedule and recomputes backdated state under current policy.

- [ ] **Step 1: Write failing running-mission tests.** Attempt vehicle change during `Running`, with and without a new start odometer. Assert domain rejection and unchanged vehicle/start/end odometer. Assert completed or not-started missions retain existing allowed behavior.
- [ ] **Step 2: Run tests and observe failure**, then add the smallest domain guard. A real handoff must close mission A and create mission B; do not add a hidden handoff feature.
- [ ] **Step 3: Write failing stale-approval tests.** Create a proposal, then modify mission schedule/status or another field so row-version changes. Approval must return conflict/409, leave request pending, and change nothing. Also test schedule crossing the current allowed backdate window after proposal creation.
- [ ] **Step 4: Run tests and observe failure**, then persist `BaseRowVersion`, `BaseStatus`, `BasePlannedStartAtUtc`, and `BasePlannedEndAtUtc` when requesting a change. Add migration with required/default handling for existing pending requests; existing pending requests without a snapshot must be rejected as stale and require resubmission.
- [ ] **Step 5: Validate at approval time.** Inside the existing serializable transaction, load the current mission, apply row-version, compare status and schedule, rerun current eligibility/conflict/backdate-window rules, apply changes, and recompute `IsBackdated` from the resulting start and current policy. Never approve from proposed JSON alone.
- [ ] **Step 6: Verify.** Run focused tests, full backend tests, migration/build, and frontend checks for changed DTOs. Add an isolated migration test if the existing migration harness supports it.
- [ ] **Step 7: Report and wait.**

**Acceptance criteria:** Running mission vehicle/odometer state cannot be rewritten; stale proposals return 409; current approval rules always run; backdated state reflects approved values.

**Risk:** Existing pending change requests may need to be recreated. This is safer than approving without a known base state and must be documented in the stage report.

**Dependencies:** None. Stage 7 reuses the serializable mission transaction and validation helpers.

## Stage 7 — Resource Races, Worker Bounds, and Time Windows

**Problem and trigger:** Extension conflict-check/save is not serializable; split checks only time, not driver/vehicle eligibility; padded overlap uses inclusive boundaries and rejects exact-gap adjacency; vehicle status/archive races with mission allocation; lifecycle worker loads unbounded due lists and one bad row aborts all; withdrawal timeout uses planned end; timeline has no range/result cap.

**Files:**
- Modify: `src/NovaFleet.Infrastructure/Missions/MissionService.cs`
- Modify: `src/NovaFleet.Infrastructure/Vehicles/VehicleService.cs`
- Modify: `src/NovaFleet.Domain/Missions/Mission.cs`
- Modify: `src/NovaFleet.Api/Workers/MissionLifecycleWorker.cs`
- Modify: `src/NovaFleet.Infrastructure/Timeline/TimelineService.cs`
- Modify: `src/NovaFleet.Api/Endpoints/EndpointMapper.cs` for timeline request validation
- Modify: timeline UI error copy if the cap is shown
- Create: one EF migration adding `WithdrawalRequestedAtUtc`
- Test: `tests/NovaFleet.UnitTests/MissionOverlapTests.cs` (create)
- Test: `tests/NovaFleet.IntegrationTests/MissionAllocationConcurrencyTests.cs` (create, guarded)
- Test: `tests/NovaFleet.UnitTests/MissionLifecycleWorkerTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/TimelineBoundsTests.cs` (create)

**Interfaces:**
- Consumes: existing serializable mission transaction, eligibility checks, lifecycle worker.
- Produces: withdrawal timing uses `WithdrawalRequestedAtUtc`; timeline accepts at most 31 days and at most 2,000 missions; exact-gap adjacency is valid.

- [ ] **Step 1: Write failing boundary/eligibility tests.** Exact end/start adjacency with zero padding must be allowed; any positive padded overlap must be rejected. Split must reject inactive/ineligible assigned driver or vehicle using the same rules as create. Vehicle archive/status change must fail while an active/future allocation exists.
- [ ] **Step 2: Run tests and observe failure.** Fix padded comparison to `item.PlannedStartAtUtc < endAtUtc + pad && item.PlannedEndAtUtc > startAtUtc - pad`. Reuse the existing eligibility validator in split.
- [ ] **Step 3: Write failing concurrency tests.** In a guarded SQL test database, run extension and vehicle archive/status operations concurrently with mission allocation. Assert one operation succeeds and the conflicting operation returns conflict; no inactive/archived resource is allocated.
- [ ] **Step 4: Implement a shared serializable allocation protocol.** Put extension conflict checks and save in one serializable transaction. Make vehicle status/archive changes use the same locking order and recheck active/future missions before save. Keep transactions short and do not perform external calls inside them.
- [ ] **Step 5: Write failing withdrawal tests.** Set planned end far in the future but request withdrawal in the past; timeout is calculated from request time. Every new withdrawal must persist `WithdrawalRequestedAtUtc`; the worker never invents a request time from planned end. Legacy rows with null request time are reported for manual review rather than auto-completed.
- [ ] **Step 6: Add the withdrawal timestamp and bounded worker.** Persist it when withdrawal is requested. Process at most 100 due missions per tick in deterministic order, isolate failures per row, and save each row or small independent batch. Log row ID/error without sensitive payloads.
- [ ] **Step 7: Add timeline limits.** Validate `fromUtc < toUtc`, maximum inclusive range 31 days, and maximum 2,000 results. Return a Persian validation error with the allowed range rather than silently truncating.
- [ ] **Step 8: Verify.** Run focused tests, guarded SQL concurrency tests, full backend tests, frontend build/lint, and migration checks. Report any unavailable SQL integration execution honestly.
- [ ] **Step 9: Report and wait.**

**Acceptance criteria:** Concurrent allocation cannot select conflicting resources; exact-gap adjacency works; split obeys eligibility; archive/status cannot race allocation; worker memory is bounded and one bad row does not block all; withdrawal and timeline time rules are correct.

**Risk:** SQL locking tests require LocalDB and careful transaction isolation. Stage 8 migration ordering must account for the new withdrawal column.

**Dependencies:** Stage 6 validation/transaction helpers. Stage 10 reuses timeline validation but handles frontend date boundaries.

## Stage 8 — Requesting-Group Migration Preservation

**Problem and trigger:** `20261115091010_DropMissionRequestingGroupOptionalPurpose` drops `RequestingGroup` permanently. Its `Down` makes `Purpose` non-null without backfilling existing NULLs and cannot restore the dropped group. Startup applies migrations automatically, so upgrade/rollback can lose data or fail.

**Files:**
- Modify: `src/NovaFleet.Infrastructure/Persistence/Migrations/20261115091010_DropMissionRequestingGroupOptionalPurpose.cs`
- Modify the original drop migration so fresh upgrades archive before dropping
- Create: `20261123090000_EnsureMissionRequestingGroupArchive.cs` for databases that already applied the old drop; it conditionally creates an empty archive when the column is absent and otherwise creates/copies it, without failing on either schema
- Modify: `src/NovaFleet.Infrastructure/Persistence/Migrations/NovaFleetDbContextModelSnapshot.cs`
- Create: `tests/NovaFleet.IntegrationTests/MissionRequestingGroupMigrationTests.cs` (LocalDB only)
- Update the existing migration snapshot/generator output through EF tooling where available
- Modify: `README.md` only with verified rollback limitations

**Interfaces:**
- Consumes: EF migrations, isolated LocalDB test database.
- Produces: archive table keyed by `MissionId`; rollback restores archived values when present, backfills empty `Purpose`, and only then removes the archive.

- [ ] **Step 1: Create a guarded LocalDB migration test.** Use a unique database name matching `^NovaFleet_Migration_Test_[a-f0-9]{32}$` and require `(localdb)\MSSQLLocalDB`. Apply migrations only through the pre-drop version, insert missions with non-null and null group/purpose values, then upgrade and rollback.
- [ ] **Step 2: Run the test and observe failure.** The current rollback should either fail on NULL `Purpose` or lose the group.
- [ ] **Step 3: Archive inside the original drop migration.** Before `DropColumn`, create `MissionRequestingGroupArchive` with primary key `MissionId` and `INSERT ... SELECT` non-null `RequestingGroup` values. This protects fresh upgrades and databases that have not yet applied the drop.
- [ ] **Step 4: Add the compatibility migration for already-dropped databases.** In `20261123090000_EnsureMissionRequestingGroupArchive`, use SQL `COL_LENGTH`/object checks: if `Missions.RequestingGroup` exists, create the archive if absent and copy values; if the column is absent, create only an empty archive. Its `Down` is a no-op so the original migration's rollback can still restore from the archive. Never insert a new earlier migration ID that would run after an already-applied drop and reference a missing column.
- [ ] **Step 5: Repair rollback.** Backfill `Purpose=''` where NULL; restore `RequestingGroup` from archive where available; make `Purpose` non-null; drop the archive only after restoration. Keep rollback deterministic and idempotent within EF's migration transaction.
- [ ] **Step 6: Test both upgrade paths and rollback.** From a pre-drop schema, assert archive values exist after upgrade and rollback restores them with NULL purposes backfilled. From a schema that already applied the old drop, assert the compatibility migration succeeds with an empty archive. Document that already-lost values cannot be reconstructed without backup/audit data.
- [ ] **Step 7: Verify.** Run the guarded migration test, model/build checks, full backend tests, and inspect generated SQL. Never run against the organization database.
- [ ] **Step 8: Report and wait.**

**Acceptance criteria:** Future upgrades preserve requesting-group history; rollback succeeds and restores available values; limitations for already-upgraded installations are explicit; test database guard is unchanged in strength.

**Risk:** Editing an already-applied migration protects fresh/pre-drop databases but cannot recreate lost values on installations that already applied the old drop. The compatibility migration creates an empty archive for those installations; historical recovery requires backup and must not be overclaimed.

**Dependencies:** None. Stage 9 installer backup must include the archive table automatically through SQL backup.

## Stage 9 — Reproducible, Recoverable Production Installer

**Problem and trigger:** The installer template previously forced Development; production binding is HTTP-only; fresh install has no rollback journal; upgrade leaves obsolete payload files; build stage is not cleaned and may mix old outputs; local `publish-new/` is untracked. Substantial upgrade/recovery work already exists and must be extended, not replaced.

**Files:**
- Modify: `installer/Install-Iis.ps1`
- Modify: `installer/Upgrade-Iis.ps1`
- Modify: `installer/NovaFleet.iss`
- Modify: `installer/build.ps1`
- Modify: `installer/web.config.template`
- Modify: `installer/Test-Upgrade.ps1`
- Modify: `.gitignore`
- Modify: `README.md` installer commands/requirements
- Test: `installer/Test-Upgrade.ps1`
- Test: `installer/Test-UpgradeSql.ps1`

**Interfaces:**
- Consumes: existing upgrade journal, backup/restore and verification middleware from pre-existing work.
- Produces: production requires an existing trusted certificate thumbprint in the local machine store and creates an HTTPS binding; HTTP is either absent or redirects to HTTPS. Fresh install uses the same prepare/finish/recover journal model as upgrade. `AllowInsecureHttp` is not a production option.

- [ ] **Step 1: Extend the mock installer tests first.** Add cases for Production environment, missing certificate thumbprint, HTTPS binding, fresh-install failure recovery, obsolete-file cleanup, and preserved configuration/data. Run and observe new cases fail.
- [ ] **Step 2: Implement HTTPS-only production setup.** Require `CertificateThumbprint`; validate the certificate exists in `LocalMachine\My`, has a private key, is not expired, and covers the hostname used by the installer. Create the IIS HTTPS binding and `netsh` SSL binding for the selected port, and change health checks to `https://<certificate-hostname>:<port>/health` with normal certificate validation against the trusted certificate store. Do not silently fall back to HTTP. Open setup on the server using the certificate hostname; remote administration uses an approved remote-desktop/admin path.
- [ ] **Step 3: Add fresh-install recovery.** Reuse the existing journal structure for file copy, IIS creation, configuration write, and verification. On pre-acceptance failure, restore/remove only resources created by this installer and restore prior files. Never delete an existing unrelated site or pool.
- [ ] **Step 4: Clean obsolete upgrade payload files safely.** Compare an explicit installer-owned file manifest from the previous and new package; remove only obsolete installer-owned files after backup and verification preparation. Never remove `data`, `.novafleet`, logs, configuration, user uploads, or `app_offline.htm`.
- [ ] **Step 5: Make builds clean/reproducible.** Remove `installer/stage` before publish, use `npm ci` consistently, restore/build/test before packaging, run both installer test scripts, and fail on any error. Do not run integration tests against an unavailable database as if they passed; report the environment requirement.
- [ ] **Step 6: Ignore generated publish output.** Add `publish-new/` to `.gitignore` without deleting the user's existing output.
- [ ] **Step 7: Verify.** Run `installer/Test-Upgrade.ps1`, guarded `installer/Test-UpgradeSql.ps1`, `installer/build.ps1` when prerequisites are available, and inspect the generated `web.config`/package manifest. Do not install to the live IIS site.
- [ ] **Step 8: Report and wait.**

**Acceptance criteria:** Production never silently starts HTTP/Development; certificate absence fails clearly; fresh failure restores prior state; upgrade removes only known obsolete files; clean stage builds reproducibly; existing upgrade work and test guards remain intact.

**Risk:** HTTPS requires a certificate before installation. This is an intentional security requirement; the stage report must state supported certificate preparation and must not claim a self-signed fallback is trusted.

**Dependencies:** Stage 1 Production template. Stage 12 adds CI enforcement around these scripts.

## Stage 10 — Spreadsheet Safety, Report Totals, and UI/API Contracts

**Problem and trigger:** Untrusted strings are written as Excel values and can become formulas. SMS/session reports take 1,000 rows then calculate totals from only those rows. UI end dates use hour 23 and exclude the last hour; backend comparisons are inclusive. Extension uses browser-local time and zeroes UTC minutes. SMS and location searches send `q` while APIs bind `search`.

**Files:**
- Modify: `src/NovaFleet.Infrastructure/Reports/ReportService.cs`
- Modify: report DTOs in `src/NovaFleet.Application/Reports`
- Modify: report/timeline endpoint queries in `src/NovaFleet.Api/Endpoints/EndpointMapper.cs`
- Modify: `web/src/pages/ReportsPage.tsx`
- Modify: `web/src/pages/TimelinePage.tsx`
- Modify: `web/src/pages/MissionDetailsPage.tsx`
- Modify: `web/src/pages/SmsPage.tsx`
- Modify: `web/src/pages/SettingsPage.tsx`
- Test: `tests/NovaFleet.UnitTests/ReportSpreadsheetSafetyTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/ReportTotalsTests.cs` (create)
- Test: `tests/NovaFleet.UnitTests/ReportDateBoundaryTests.cs` (create)
- Test: `web/src/pages/ReportContracts.test.ts` and `web/src/pages/MissionExtensionTime.test.ts` (create)

**Interfaces:**
- Consumes: `MissionVisibility.Apply`, `ReportTableDto(..., RowsTruncated, MaxRows)`.
- Produces: report rows are capped at 1,000 only for transfer, totals/counts use the full filtered set, `RowsTruncated=true` and `MaxRows=1000` when capped; end date is next-day start with backend exclusive `<`.

- [ ] **Step 1: Write failing spreadsheet tests.** Include values beginning with `=`, `+`, `-`, `@`, tab, and carriage return in titles, headers, user-supplied cells, and totals. Reopen the generated XLSX and assert the cell data type is Text and the exact string is preserved.
- [ ] **Step 2: Run tests and observe failure.** Set every untrusted string cell to `XLDataType.Text` before assigning its value; keep numeric/date cells typed. Do not prepend an apostrophe that changes displayed data.
- [ ] **Step 3: Write failing totals tests.** Seed more than 1,000 matching SMS/session rows with known totals. Assert `TotalCount` and aggregates cover all filtered rows, `Items.Count == 1000`, `RowsTruncated == true`, and `MaxRows == 1000`. Also test fewer than 1,000 rows.
- [ ] **Step 4: Implement full totals plus explicit truncation.** Run count/aggregate over the complete filtered query, then page/cap rows. Return the new metadata and show a Persian «نمایش ۱۰۰۰ ردیف اول از مجموع ...» notice.
- [ ] **Step 5: Write failing date tests.** A mission at 23:30 Tehran on the selected end date must be included; one at next-day 00:00 must be excluded. UI computes next-day start in Tehran/Jalali and sends UTC; backend uses `< exclusiveEndUtc`.
- [ ] **Step 6: Fix date boundaries and extension time.** Use the existing Jalali date/hour controls and `jalaliValueToUtc` for extension; remove browser-local `datetime-local` conversion and minute zeroing. Apply the same end-boundary helper to reports and timeline.
- [ ] **Step 7: Fix search contracts.** Change SMS and locations frontend requests to send `search`, matching `EndpointMapper.cs`. Add tests asserting the query string and backend filtering.
- [ ] **Step 8: Verify.** Run focused tests, full backend tests, frontend tests/lint/build, and generate/open representative XLSX files locally without user data.
- [ ] **Step 9: Report and wait.**

**Acceptance criteria:** No untrusted string becomes an Excel formula; totals are complete and truncation visible; selected end day includes all 24 hours in Tehran; extension preserves Jalali hour precision; search filters work.

**Risk:** Report DTO metadata changes all consumers; update frontend types and tests in the same stage. Stage 3 visibility predicates must remain applied before all aggregates.

**Dependencies:** Stage 3 visibility. Stage 7 timeline bounds.

## Stage 11 — Atomic UI Saves and Lean Application Cleanup

**Problem and trigger:** Driver profile/default-vehicle and vehicle/driver related saves use separate requests, so partial success leaves stale UI. List/options DTOs carry full avatars. All routes are statically imported. Several settings are persisted but unused. Realtime is a no-op and its event method is unused. Candidate dead APIs and duplicate helpers need a final reference check.

**Files:**
- Modify: `src/NovaFleet.Application/Drivers/DriverModels.cs`, `src/NovaFleet.Application/Vehicles/VehicleModels.cs`, `src/NovaFleet.Infrastructure/Drivers/DriverService.cs`, `src/NovaFleet.Infrastructure/Vehicles/VehicleService.cs`, and `src/NovaFleet.Api/Endpoints/EndpointMapper.cs`
- Modify: `web/src/pages/DriversPage.tsx`, `web/src/pages/VehiclesPage.tsx`
- Modify: `src/NovaFleet.Application/Drivers/DriverModels.cs`, `src/NovaFleet.Application/Identity/IdentityModels.cs`, `src/NovaFleet.Infrastructure/Drivers/DriverService.cs`, `src/NovaFleet.Infrastructure/Identity/UserService.cs`
- Modify: `src/NovaFleet.Application/Common/PagedResult.cs`, `src/NovaFleet.Application/Authorization/EffectivePermissionSet.cs`, `src/NovaFleet.Application/Authorization/PermissionCatalog.cs`, `src/NovaFleet.Application/Sessions/SessionModels.cs`, `src/NovaFleet.Infrastructure/Sessions/SessionService.cs`, `src/NovaFleet.Application/Abstractions/INotificationPublisher.cs`
- Delete after final reference check: `src/NovaFleet.Infrastructure/Realtime/NoopRealtimeNotifier.cs` and `src/NovaFleet.Application/Abstractions/IRealtimeNotifier.cs`
- Modify: `src/NovaFleet.Infrastructure/Notifications/NotificationPublisher.cs`, `src/NovaFleet.Api/Program.cs`
- Modify: `src/NovaFleet.Infrastructure/Settings/SettingsWriters.cs`, `src/NovaFleet.Infrastructure/Settings/ConnectionsSettingsService.cs`, `src/NovaFleet.Application/Settings/SettingKeys.cs`, `src/NovaFleet.Application/Settings/ConnectionModels.cs`, `src/NovaFleet.Infrastructure/Setup/SetupService.cs`, `web/src/components/ConnectionsSettings.tsx`, `web/src/pages/SettingsPage.tsx`, and `web/src/lib/types.ts`
- Modify: domain helpers in `SystemSetting.cs`, `Driver.cs`, `ApplicationUser.cs`, `Role.cs`, `Vehicle.cs`, `SmsQueueItem.cs`, `MissionChangeRequest.cs`, `Mission.cs`, `Location.cs`, `MaintenanceRules.cs`
- Modify: `web/src/main.tsx`, route/page files, `web/vite.config.ts`
- Test: `tests/NovaFleet.UnitTests/DriverVehicleAtomicSaveTests.cs` and `tests/NovaFleet.UnitTests/ListPayloadTests.cs` (create)
- Test: `web/src/routes/LazyRoutes.test.ts` (create)

**Interfaces:**
- Consumes: existing composite forms and services.
- Produces: one backend operation per multi-field save; list/options DTOs omit `AvatarDataUrl` while detail/profile responses retain it; route-level lazy imports with one shared Suspense fallback.

```csharp
public sealed record DriverCompositeCreateRequest(DriverCreateRequest Profile, Guid? DefaultVehicleId);
public sealed record DriverCompositeUpdateRequest(DriverUpdateRequest Profile, Guid? DefaultVehicleId);
public sealed record VehicleCompositeCreateRequest(VehicleCreateRequest Profile, Guid? DriverId);
public sealed record VehicleCompositeUpdateRequest(VehicleUpdateRequest Profile, Guid? DriverId);

Task<DriverDto> CreateCompositeAsync(DriverCompositeCreateRequest request, CancellationToken cancellationToken = default);
Task<DriverDto> UpdateCompositeAsync(Guid id, DriverCompositeUpdateRequest request, CancellationToken cancellationToken = default);
Task<VehicleDto> CreateCompositeAsync(VehicleCompositeCreateRequest request, CancellationToken cancellationToken = default);
Task<VehicleDto> UpdateCompositeAsync(Guid id, VehicleCompositeUpdateRequest request, CancellationToken cancellationToken = default);
```

Map these to `POST /api/drivers/composite`, `PUT /api/drivers/{id}/composite`, `POST /api/vehicles/composite`, and `PUT /api/vehicles/{id}/composite`; use the composite routes for both create and update forms so a selected default vehicle/driver is saved in the same transaction.

- [ ] **Step 1: Write failing atomic-save tests.** Force the second related write to fail and assert the first write is rolled back. Cover driver profile plus default vehicle, and vehicle plus related driver. Return one updated DTO to the UI.
- [ ] **Step 2: Run tests and observe failure**, then add the smallest composite service/endpoint transaction. Do not add a generic transaction framework. Update frontend to call the composite endpoint once and invalidate both queries from its result.
- [ ] **Step 3: Write failing payload tests.** Driver list/options and user list must not include full avatar data URLs; driver/user detail and own profile still include them. Replace list avatars with existing initials/name presentation; do not add an image library.
- [ ] **Step 4: Recheck and remove dead code.** Whole-tree search each candidate immediately before deletion: `PagedResult<T>.Empty`, `EffectivePermissionSet.HasAny`, `PermissionCatalog.Policy`, `ISessionService.ActiveCountAsync`, and `INotificationPublisher.PublishToRolesAsync` including implementations/fakes. Remove only if no caller or documented external contract remains.
- [ ] **Step 5: Remove no-op realtime.** Delete `IRealtimeNotifier`, `NoopRealtimeNotifier`, DI registration, and the per-notification no-op loop after confirming `PublishAsync` has no caller. Keep database notification persistence and polling unchanged.
- [ ] **Step 6: Remove nonfunctional settings.** Recheck the operations log-level, PDF font-path, and report paper-size keys and controls. The audit found no runtime consumer; remove their DTO fields, validation/writes, `SettingKeys` constants, setup defaults, and operations form fields. Do not retain a visible control that does nothing. If the recheck finds a real consumer, stop and report the contradiction instead of silently changing scope.
- [ ] **Step 7: Consolidate domain helpers only where behavior is identical.** Compare error messages and trimming/null rules first. Keep distinct helpers if consolidation changes behavior. Merge duplicate pending/approved mission validation into one private method only when tests prove identical invariants.
- [ ] **Step 8: Add route-level code splitting.** Replace static page imports in `main.tsx` with `React.lazy`, use one shared Persian loading fallback, and keep layout/auth providers outside the fallback. Verify login and initial route chunks shrink and direct-route navigation works.
- [ ] **Step 9: Verify.** Run focused backend/frontend tests, full backend tests, lint/build, inspect bundle manifest before/after, and search again for deleted symbols. Exercise driver/vehicle golden paths in a browser only if available without user credentials.
- [ ] **Step 10: Report and wait.**

**Acceptance criteria:** Composite saves are all-or-nothing; list payloads are smaller; dead/no-op code is gone; no visible setting is nonfunctional; initial frontend bundle is reduced without breaking direct routes.

**Risk:** Removing public methods can break unseen consumers. The final whole-tree search and build are mandatory; documented external APIs require explicit user approval before removal.

**Dependencies:** Stage 2 for user/session APIs; stage 10 for changed pages. No-op realtime removal must not alter database notifications.

## Stage 12 — CI, Lint, Documentation, and Final Verification

**Problem and trigger:** There is no PR/main workflow running backend tests, frontend lint/build, integration tests, and installer tests together. Historical lint had 25 warnings. README has stale copy/security/test/setup claims and may direct assets into `wwwroot/dist`.

**Files:**
- Create: `.github/workflows/ci.yml`
- Modify: `.github/workflows/release-installer.yml` only where it duplicates or conflicts with required checks
- Modify: `web/src/components/maintenance/OdometerPanel.tsx`, `RecordEditor.tsx`, `web/src/components/Plate.tsx`, `web/src/pages/ReportsPage.tsx`, `SettingsPage.tsx`, `MissionsPage.tsx`, `TimelinePage.tsx`, `web/src/components/MissionForm.tsx`, `JalaliDateField.tsx`, `ConnectionsSettings.tsx`, `web/src/auth/AuthContext.tsx`, and `web/src/main.tsx`
- Create: `web/src/lib/plate.ts`, `web/src/lib/jalali.ts`, `web/src/auth/useAuth.ts`, `web/src/auth/RequireAuth.tsx`
- Modify: `README.md`
- Modify: `installer/build.ps1` only for verified command alignment
- Test: workflow syntax/YAML validation, all project tests, frontend lint/build, installer tests

**Interfaces:**
- Consumes: all previous stages and verified command outputs.
- Produces: PR/main CI with separate backend, frontend, and guarded installer/SQL jobs; documentation matches measured behavior.

- [ ] **Step 1: Run fresh baseline checks.** Record backend test counts/skips, frontend lint warnings/errors, bundle size, installer mock test results, and guarded SQL/LocalDB test results. The plan-writing check on 2026-11-23 ran `npm run lint` and measured 25 warnings / 0 errors across 42 files; rerun it at execution time because earlier stages will change those files. Historical numbers are not completion evidence.
- [ ] **Step 2: Add failing workflow validation.** Create `.github/workflows/ci.yml` for pull requests and pushes to `main`: setup the pinned .NET SDK, restore/build/test backend, setup Node, `npm ci`, lint and build frontend, then run installer mock tests. SQL/LocalDB integration jobs run on Windows with an explicit isolated database and fail if guards are violated; unavailable external services are not faked.
- [ ] **Step 3: Validate workflow and observe current failures.** Fix code/configuration rather than disabling jobs. Keep release tag/manual behavior intact.
- [ ] **Step 4: Remove the measured frontend lint warnings.** Use lazy state initialization for default dates in `OdometerPanel`/`RecordEditor`; compute render-time dates from state initialized once and updated by an explicit timer where needed in `MissionForm`, `ReportsPage`, and `TimelinePage`; replace `useMemo(todayParts, [])` with an inline initializer. Move plate/Jalali helpers and `useAuth` to the new non-component modules, move `RequireAuth` to its own file, and remove or relocate `missionRouteHelpers` after checking callers. Hydrate settings/connection forms from query success into keyed form state instead of synchronous effect writes. The initial `/auth/me` bootstrap is a legitimate external synchronization; if the rule still flags it, use one narrowly scoped suppression with that reason rather than a broad disable. Re-run lint until zero errors and zero warnings.
- [ ] **Step 5: Correct README.** Fix the frontend copy command so `index.html` and assets land at `wwwroot` as the API expects, not `wwwroot/dist`. Update setup/HTTPS, lockout, CSRF, cache isolation, code splitting, SMS indeterminate behavior, migration limitation, test commands/counts, and installer certificate requirements only after fresh verification.
- [ ] **Step 6: Run final full verification.** Restore/build/test both backend projects; frontend lint/build; installer mock and guarded SQL tests; guarded migration tests; inspect working tree and ensure no generated output/secrets are staged. Record skipped/unexecuted live checks explicitly.
- [ ] **Step 7: Report and wait.** Provide the final coverage checklist, commands/results, remaining external limitations, and proposed final commit files. Commit only after explicit approval.

**Acceptance criteria:** CI enforces the complete verification set; frontend lint is clean; README commands are executable and accurate; no claim relies on historical results; no live SMS, user login, production DB, or live IIS action was performed.

**Risk:** Windows-only SQL/installer jobs may require a hosted-runner capability decision. Do not replace them with a green job that skips the actual checks; report the runner constraint if it blocks execution.

**Dependencies:** All stages 1–11.

## Execution Rules for Every Stage

1. Re-read the stage's files and current diff; preserve pre-existing changes.
2. Invoke the required execution skill (`superpowers:subagent-driven-development` or `superpowers:executing-plans`) and test-driven-development before implementation.
3. Create/claim stage tasks in the session task list; keep them current.
4. Add the smallest failing regression tests first and record the observed failure.
5. Implement the smallest complete fix, including every caller, DTO, migration, UI state, and fixture affected.
6. Run fresh focused and full verification; inspect actual diffs, not agent summaries.
7. Update this file's stage checkboxes and add a short Persian verification note under the stage.
8. Report evidence and wait for explicit approval.
9. After approval, inspect `git status` and staged diff, add only the stage's named files plus this plan's progress update, and create one new commit. Do not push.
10. Start the next stage only after the user approves moving on.

## Definition of Done

- Every audit-matrix row and additional finding has a completed, verified stage.
- Every new security, concurrency, financial/SMS, migration, or data-write rule has a regression test that was observed failing before the fix.
- No paid SMS, real user authentication, production database mutation, live IIS change, or secret exposure occurred.
- Backend, frontend, installer, and available guarded SQL/migration verification have fresh recorded results.
- The working tree contains no accidental generated artifacts or unrelated staged changes.
- Each stage has an explicit approval and its own commit; no push occurred without separate authorization.
