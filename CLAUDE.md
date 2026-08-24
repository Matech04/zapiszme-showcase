# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Backend (.NET 10)

```bash
# Run API (from repo root or backend/src/App.Api)
cd backend/src/App.Api && dotnet run

# Run all tests
# NOTE: .NET 10 uses the Microsoft.Testing.Platform runner — the old positional-arg
# syntax no longer runs tests. A solution needs --solution; a project needs --project;
# `--nologo` is NOT supported (it prints help and reports "Zero tests ran").
dotnet test --solution backend/backend.slnx

# Run specific test project (note --project)
dotnet test --project backend/tests/App.Application.UnitTests/App.Application.UnitTests.csproj
dotnet test --project backend/tests/App.Domain.UnitTests/App.Domain.UnitTests.csproj
dotnet test --project backend/tests/App.Api.IntegrationTests/App.Api.IntegrationTests.csproj

# Run a single test by name (filter args go after `--`, passed to the test host)
dotnet test --project backend/tests/App.Application.UnitTests/App.Application.UnitTests.csproj -- --filter-method "*TestMethodName*"

# Add a migration (from repo root)
dotnet ef migrations add MigrationName --project backend/src/App.Infrastructure --startup-project backend/src/App.Api

# Apply migrations
dotnet ef database update --project backend/src/App.Infrastructure --startup-project backend/src/App.Api
```

### Dashboard (Angular 21)

```bash
cd dashboard
npm install
npm start          # dev server → http://localhost:4201, API → http://localhost:5141
npm test           # Vitest
npm run build
```

### Public Booking App (Astro 6 + Svelte 5)

```bash
cd web
npm install
npm run dev        # dev server → http://localhost:4321
npm test           # Vitest (single run)
npm run test:watch
```

### Infrastructure

```bash
# Start PostgreSQL for local dev
docker compose -f docker-compose.dev.yml up -d

# Production-like local stack (requires age key + .env.local)
make pull-env && make prod-local-up
make prod-local-rebuild   # rebuild API only (~20s)
make prod-local-reset-db  # wipe volumes

# Expose the dev stack on the LAN (open the app on a phone, e.g. for demos).
# WSL2 is NAT'd, so the phone reaches the Windows host IP, not the WSL IP.
# `up` points frontends at the host IP, binds servers to 0.0.0.0, and sets the
# Windows portproxy + firewall (elevated via a UAC prompt you must accept).
.claude/scripts/lan-dev.sh up [worktree]     # or the /lan-dev skill
.claude/scripts/lan-dev.sh down [worktree]   # revert to plain localhost dev
# Host IP is auto-detected (same-/24-gateway adapter); override: LAN_HOST_IP=192.168.x.y
```

### OpenAPI client regeneration

Backend build triggers NSwag post-build (`backend/src/App.Api/nswag.json` + `nswag.booking.json`). Generated files — **do not edit manually**:
- `dashboard/src/app/core/api/api-client.ts` (full staff API)
- `web/src/lib/booking-openapi-client.ts` (public booking API)

NSwag uses `noBuild: true` — the API project must be built first, otherwise clients silently regenerate from a stale assembly.

---

## Architecture

### Repository layout

```
backend/
  src/
    App.Domain/          # Aggregates, value objects, domain exceptions, domain services
    App.Application/     # CQRS handlers (MediatR), DTOs, FluentValidation, access checks
    App.Infrastructure/  # EF Core, repositories, migrations, email/OTP, background jobs
    App.Api/             # Controllers, middleware, auth setup, OpenAPI
  tests/
    App.Domain.UnitTests/
    App.Application.UnitTests/
    App.Api.IntegrationTests/
dashboard/               # Angular admin/staff panel
web/                     # Astro + Svelte public booking app
```

### Backend: Clean Architecture + CQRS

Every feature lives in `App.Application` as a command or query record + a handler class. All handlers for tenant-scoped data extend `TenantHandler<TRequest, TResponse>` which exposes a `TenantId` property (throws `NoTenantHeader` if unset). MediatR's pipeline includes a `ValidationBehavior` that runs FluentValidation before the handler.

```csharp
public record CreateFooCommand(string Name) : IRequest<Guid>;

public class CreateFooHandler : TenantHandler<CreateFooCommand, Guid>
{
    public override async Task<Guid> Handle(CreateFooCommand request, CancellationToken ct)
    {
        // TenantId available here
    }
}
```

### Multi-tenancy

All domain entities carry `TenantId` and implement `ITenantEntity`. Two enforcement layers:

1. **Read isolation** — `ApplicationDbContext.OnModelCreating` adds `HasQueryFilter` on every tenant entity using `ICurrentTenantService.TenantId`. Queries automatically exclude other tenants' data.
2. **Write isolation** — `SaveChangesAsync` override iterates `ChangeTracker.Entries<ITenantEntity>()` and throws `TenantViolation` if any Added/Modified entry's `TenantId` mismatches the current tenant.

`ICurrentTenantService` (scoped) is populated by:
- **Staff API** — `TenantIdentifierMiddleware` resolves tenant from the authenticated user's linked employee record.
- **Public booking API** — salon slug from the route (`/api/booking/{slug}/...`) resolves the tenant.

### Two API surfaces

| Surface | Base class | Auth | Route prefix |
|---|---|---|---|
| Staff panel | `ApiControllerBase` | JWT Bearer (.NET Identity) | `/api/...` |
| Public booking | `BookingApiControllerBase` | Anonymous | `/api/booking/{slug}/...` |

Authorization policies: `SystemAdminOnly` (Admin role), `BusinessManagement` (Owner), `StaffManagement` (Owner/Manager), `GeneralAccess` (Owner/Manager/Employee/Kiosk).

Fine-grained authorization lives in **`IStaffAccessPolicy`** (`App.Application/Common/Security/`), a scoped service injected into handlers — never into controllers. It combines two axes: the caller's role and the tenant's `StaffCalendarVisibilityPolicy` (`OwnCalendarOnly` / `TeamReadOnly` / `TeamFull`). Reads pass from `TeamReadOnly`; writes require `TeamFull`. The `Kiosk` ("Recepcja") account has no calendar of its own and bypasses the policy entirely. Controllers keep only `[Authorize(Policy = ...)]` — coarse role gating — and contain zero `Forbid()` calls.

### Soft delete

Entities that support soft delete implement `ISoftDelete` (`IsActive` flag). `DeletionService.DeleteAsync()` calls `entity.Deactivate()` — never hard-deletes. Global query filters exclude inactive records. Appointment history is preserved even when customers or services are deactivated.

### Appointment domain rules

- **Availability** — `AppointmentService.IsAvailableAsync(employee, timeRange, date, tenantId, ignoreAppointmentId)` checks (1) no collision with existing non-canceled appointments and (2) requested range falls within employee's working hours.
- **Status flow** — `Pending → Booked / InProgress / Completed / Canceled`. Public bookings additionally go through `AwaitingOtp`.
- **Hold lease** — public booking creates a temporary hold before OTP verification; this prevents slot squatting. TTLs: `HoldTtl = 60s` (window for entering email/phone), `OtpLeaseTtl = 3min` (window for entering the code).
- **Anti-abuse** — `CreateBookingAppointmentCommand` cancels existing Pending appointments for the same `AnonSessionId` before creating a new one.
- **Timezone** — `Tenant.TimeZoneId` (default `Europe/Warsaw`) is used to determine "today" and block past-date bookings.
- **Gap filling** — configurable per tenant (`GapFillingSettings`). `PreferAdjacent` marks slots adjacent to existing appointments as preferred. `AdjacentOnly` (strict) restricts slots to adjacent-only **only in the public booking flow** (`EnforceStrictGapFilter = true` in `GetBookingAvailableTimeSlotsQuery`); staff can always book any available slot.

### Dashboard (Angular)

Routes lazy-load feature components. Domains follow a `feature / ui / data-access` split under `dashboard/src/app/domains/`. Reusable base classes (`BaseReadComponent`, `BaseWriteComponent`, `BaseCrudComponent`, `BaseFormComponent`) reduce boilerplate for CRUD features.

`API_BASE_URL` token is provided from `environment.apiBaseUrl` in `app.config.ts`. The generated `api-client.ts` is injected via this token — do not hardcode URLs anywhere else.

HTTP interceptors (registered in `app.config.ts`): `credentialsInterceptor` (sends cookies), `xsrfInterceptor` (antiforgery token), `errorInterceptor` (maps error codes to Polish messages via `api-error-messages.ts`).

New forms prefer `@angular/forms/signals` (signal forms) — see `domains/services/feature/service-form.component.ts` for the pattern.

### Public booking (Astro + Svelte)

Astro pages live in `web/src/pages/` (`index.astro`, `[slug].astro`); interactive Svelte components in `web/src/components/booking/` (mount with `client:only="svelte"`). Slug is parsed from `window.location.pathname` in `BookingEntry.svelte` and passed down as a prop. New components use Svelte 5 runes (`$state`, `$derived`, `$props`), not Svelte 4 stores.

### Integration tests

`App.Api.IntegrationTests` uses **Testcontainers PostgreSQL** (real Postgres container per fixture — not InMemory) plus `IntegrationTestAuthenticationHandler` that reads `X-Integration-Test-UserId` and `X-Integration-Test-Roles` headers instead of real JWT. Helper: `AuthenticatedTestClient.CreateOwnerClient()` / `CreateAdminClient()`. Background hosted services are disabled in the Testing environment.

### Background jobs

- **`AppointmentReminderHostedService`** — every 5 min; sends reminders in 24h and 2h windows.
- **`AppointmentStatusLifecycleHostedService`** — transitions appointment statuses based on time.
- **`UnconfirmedAccountCleanupHostedService`** — removes unconfirmed registrations after TTL. **Disabled in Testing environment.**

All extend `BackgroundService` and use `IServiceScopeFactory` for per-cycle DB scope.

### Logging

Serilog → Console / File / Debug (dev) / Seq at `http://seq:80` (prod). `UseSerilogRequestLogging()` covers HTTP. EF query logging at Warning level. `SensitiveDataMaskingEnricher` masks emails/OTP codes in logs — **don't bypass it** to debug; use scoped local output instead.

---

## Key file references

Stable paths for common targets. **Line numbers drift — grep the symbol, don't trust line numbers in this file.**

| Concern | File |
|---|---|
| `TenantHandler<,>` base | `backend/src/App.Application/Common/TenantHandler.cs` |
| Query filters + tenant write check | `backend/src/App.Infrastructure/Persistence/ApplicationDbContext.cs` |
| `DeletionService` (soft-delete) | `backend/src/App.Application/Common/DeletionService.cs` |
| `IStaffAccessPolicy` (authz) | `backend/src/App.Application/Common/Security/StaffAccessPolicy.cs` |
| `AppointmentService` (availability) | `backend/src/App.Domain/Services/AppointmentService.cs` |
| Booking hold lease | `backend/src/App.Application/Booking/BookingAppointments/Commands/CreateBookingAppointmentCommand.cs` |
| Integration test auth | `backend/src/App.Api/Authentication/IntegrationTestAuthenticationHandler.cs` |
| Test client helpers | `backend/tests/App.Api.IntegrationTests/Support/AuthenticatedTestClient.cs` |
| NSwag configs | `backend/src/App.Api/nswag.json`, `nswag.booking.json` |
| EF migrations | `backend/src/App.Infrastructure/Migrations/` |
| Angular CRUD base classes | `dashboard/src/app/core/base/crud/` |
| Angular HTTP interceptors | `dashboard/src/app/core/interceptors/` |
| Polish error map | `dashboard/src/app/core/errors/api-error-messages.ts` |
| Angular DI + interceptor wiring | `dashboard/src/app/app.config.ts` |
| Angular routes | `dashboard/src/app/app.routes.ts` |
| Booking Svelte components | `web/src/components/booking/` |
| Booking API client | `web/src/lib/booking-openapi-client.ts` |

---

## Recipes

### R1: Add a backend Command (write operation)

1. **Record + Result** in `backend/src/App.Application/<Domain>/Commands/<Name>/<Name>.cs`:
   ```csharp
   public record FooCommand(...) : IRequest<FooResult>;
   public record FooResult(...);
   ```
2. **Handler** — extend `TenantHandler<FooCommand, FooResult>`, NOT `IRequestHandler<,>`. `TenantId` is available from base.
3. **Authz** — inject `IStaffAccessPolicy` and call the right guard BEFORE touching the entity:
   - employee **profile** (schedule, leaves, services) → `EnsureSelfOrStaffManager(employeeId)`
   - someone's **calendar** (appointments) → `EnsureCanMutateEmployeeCalendarAsync(employeeId, ct)` or `EnsureCanMutateAppointmentAsync(appointmentId, ct)`
   - reading a **list** of appointments → `ResolveCalendarReadScopeAsync(requestedEmployeeId, ct)` and filter the query by its result.
4. **Validator** — `class FooCommandValidator : AbstractValidator<FooCommand>` runs automatically via `ValidationBehavior`.
5. **Test** in `App.Application.UnitTests/<Domain>/` — at minimum: happy path + `TenantViolation` test (caller tries another tenant's data).
6. **Endpoint** — controller action in `backend/src/App.Api/Controllers/<Domain>Controller.cs` (extends `ApiControllerBase`).
7. **Rebuild backend** → NSwag regenerates both clients.

### R2: Add a Query (read operation)

Same as R1, minus the validator. Reads still need `IStaffAccessPolicy` when they expose another employee's data. Still extend `TenantHandler<TRequest, TResult>` so query filters resolve to the right tenant.

### R3: Add a domain entity with multi-tenancy

1. Aggregate in `backend/src/App.Domain/<Domain>/<Entity>.cs` — implement `ITenantEntity`. If soft-deletable, also `ISoftDelete`.
2. EF configuration in `backend/src/App.Infrastructure/Configurations/<Entity>Configuration.cs`.
3. **Critical:** add `HasQueryFilter` in `ApplicationDbContext.OnModelCreating`:
   ```csharp
   modelBuilder.Entity<Foo>()
     .HasQueryFilter(f => f.TenantId == _currentTenantService.TenantId
                       && f.IsActive);  // omit IsActive if not ISoftDelete
   ```
4. Migration: `dotnet ef migrations add Add<Entity> --project backend/src/App.Infrastructure --startup-project backend/src/App.Api`
5. Read the generated `.cs` and `.Designer.cs` manually — verify column types, TenantId index, FK delete behavior.
6. Tests: invariants in `App.Domain.UnitTests`, handler in `App.Application.UnitTests`.

### R4: Add an Angular dashboard feature

1. Folder `dashboard/src/app/domains/<feature>/` with `feature/`, `ui/`, `data-access/` split.
2. Data access via generated client from `core/api/api-client.ts` (or `BaseApiService<Dto>` subclass).
3. Components: standalone, signals preferred over RxJS for state.
4. Forms: signal forms (`@angular/forms/signals`) — see `domains/services/feature/service-form.component.ts`.
5. Route in `app.routes.ts` — always `loadComponent: () => import(...)` (lazy).
6. If backend returned new error codes, add Polish mappings to `core/errors/api-error-messages.ts`.

### R5: Add a public booking flow change

1. Endpoint in `backend/src/App.Api/Controllers/Booking/` extending `BookingApiControllerBase` (anonymous, slug-scoped).
2. Application handler in `App.Application/Booking/` — still `TenantHandler<,>` (slug → tenant via middleware).
3. Svelte component in `web/src/components/booking/` using Svelte 5 runes.
4. Rebuild backend → confirm `web/src/lib/booking-openapi-client.ts` regenerated (git diff).
5. Verify `PublicBookingWrite` rate-limit policy covers new write endpoints (`appsettings.json`).

### R6: Add a database migration

1. `dotnet ef migrations add <Name> --project backend/src/App.Infrastructure --startup-project backend/src/App.Api`
2. **Read** generated `.cs` + `.Designer.cs`. Common mistakes:
   - Missing index on `TenantId`
   - FK `DeleteBehavior.Cascade` where `Restrict` would be safer (cross-aggregate)
   - Nullable mismatch with domain
3. Do not apply to shared DB until reviewed.
4. Apply locally: `dotnet ef database update --project backend/src/App.Infrastructure --startup-project backend/src/App.Api`
5. Smoke: `dotnet test --solution backend/backend.slnx`

---

## Anti-patterns

Things that have bitten this codebase. Do not do these.

### Backend

- **Don't** inherit a handler from `IRequestHandler<,>` if it touches tenant data — use `TenantHandler<,>`. Why: `TenantId` access + write-side `TenantViolation` check require it.
- **Don't** add a `DbSet<T>` without a corresponding `HasQueryFilter` for `TenantId` in `ApplicationDbContext`. Why: silent cross-tenant data leak on read.
- **Don't** use `_context.Remove(entity)` for entities implementing `ISoftDelete`. Use `DeletionService.DeleteAsync()`. Why: hard delete breaks Appointment history (FK references) and is irreversible.
- **Don't** mutate employee data without calling `IStaffAccessPolicy.EnsureSelfOrStaffManager(targetEmployeeId)` first. Why: without it, an Employee role could modify other employees' records.
- **Don't** put authorization in a controller. It belongs in the handler, because handlers have more than one caller — the public booking flow reuses the appointment core (`PlaceAppointmentCommand`, `ApplyRescheduleCommand`), and a guard in `AppointmentsController` would silently not apply to it.
- **Don't** authorize inside `PlaceAppointmentCommand` / `ApplyRescheduleCommand`. They are the shared domain core with two callers: staff (authorized by the `CreateAppointmentCommand` / `RescheduleAppointmentCommand` wrappers) and anonymous public booking (guarded by slug→tenant, hold lease, OTP and rate limits).
- **Don't** register `IStaffAccessPolicy` as a singleton. It depends on scoped services and memoizes the tenant's policy per request; a singleton would leak the caller's identity across requests.
- **Don't** mock the database in `App.Api.IntegrationTests`. Tests use Testcontainers PostgreSQL — real Postgres, fresh per fixture. Why: past incident — mocked tests passed but a real migration broke prod.
- **Don't** edit a migration's `.Designer.cs` by hand. Re-generate or `migrations remove` + re-add.
- **Don't** assume past-date booking is blocked by the database. The check lives in domain code using `Tenant.TimeZoneId` — bypass it and you'll create appointments "yesterday".
- **Don't** loosen `SameSite=None` cookies without tracing the `LOCAL_PROD` flag path in `Program.cs` — auth flow across the Caddy edge breaks.
- **Don't** print raw OTP codes or emails to logs — `SensitiveDataMaskingEnricher` masks them. If you need them for debugging, use local scoped console output.

### Dashboard (Angular)

- **Don't** hardcode API URLs. Use the generated client (e.g., `AppointmentsClient`) — it already injects `API_BASE_URL`.
- **Don't** edit `dashboard/src/app/core/api/api-client.ts` by hand. Regenerated on every backend build.
- **Don't** bypass the interceptor chain — you'll skip CSRF and credential cookies. Use `HttpClient` from DI.
- **Don't** show raw `HttpErrorResponse` to the user. Add a code mapping in `api-error-messages.ts`; `errorInterceptor` will surface a Polish toast.
- **Don't** introduce RxJS state for new code — use signals.
- **Don't** use native HTML form controls (`<input type="month|date|time">`, `<select>`, native checkboxes) in dashboard components. Use PrimeNG: `p-date-picker` (month picker = `view="month" dateFormat="mm/yy"`), `p-select`, `p-multiselect`. Why: native controls render with browser/OS styling and look foreign next to the PrimeNG + Tailwind UI — recurring regression.

### Web (Astro + Svelte)

- **Don't** edit `web/src/lib/booking-openapi-client.ts` by hand. Regenerated on every backend build.
- **Don't** add Astro SSR to booking pages without verifying slug→tenant resolution works server-side without browser cookies.
- **Don't** use Svelte 4 stores (`writable`, `readable`) in new components. Use Svelte 5 runes.

### Cross-cutting

- **Don't** change a backend API contract without rebuilding the API project — without rebuild, generated clients silently keep the old shape and the frontends fail only at runtime.
- **Don't** skip pre-commit hooks (formatting/lint). If a hook fails, fix the root cause; never `--no-verify`.
- **Don't** commit `.env` or secrets. Production env is encrypted with `age` and pulled via `make pull-env`.

---

## Pre-flight checklists

Run through the relevant list before reporting a task as done.

### After changing `App.Application` or `App.Domain`
- [ ] `dotnet test --solution backend/backend.slnx` green
- [ ] New handler extends `TenantHandler<,>`
- [ ] New entity has `HasQueryFilter` in `ApplicationDbContext`
- [ ] Test covers `TenantViolation` for cross-tenant access
- [ ] If touching employee data: `EnsureSelfOrStaffManager` called

### After changing the DB model
- [ ] Migration generated
- [ ] Migration `.cs` read manually — types, nullability, indexes
- [ ] FK `DeleteBehavior` is `Restrict` (or explicitly intentional `Cascade`)
- [ ] Not applied to shared DB until reviewed
- [ ] `.Designer.cs` snapshot looks sane

### After changing API contracts
- [ ] `dotnet build backend/backend.slnx` succeeds (NSwag regen depends on a built assembly)
- [ ] `dashboard/src/app/core/api/api-client.ts` updated (git diff confirms)
- [ ] `web/src/lib/booking-openapi-client.ts` updated (git diff confirms)
- [ ] Dashboard and web both compile (`npm run build` in each)

### After changing public booking flow
- [ ] Slug → tenant resolution works (`/api/booking/<slug>/...` reaches handler)
- [ ] Hold TTL (60s) and OTP TTL (3min) respected
- [ ] OTP flow not bypassable
- [ ] `PublicBookingWrite` rate-limit policy covers any new write endpoint

### Before declaring a frontend feature done
- [ ] Lazy-loaded (entry in `app.routes.ts` uses `loadComponent`)
- [ ] No hardcoded URLs (uses generated client)
- [ ] New error codes mapped in `api-error-messages.ts`
- [ ] Vitest green: `npm test -w dashboard` or `npm test -w web`
- [ ] Tested in browser at least once — type-check is not feature-check

---

## Pause-and-ask zones

Slow down and confirm with the user before:

- Touching multi-tenancy enforcement (query filters, `TenantViolation` check, `ICurrentTenantService`).
- Changing `Identity` / `DataProtection` setup in `Program.cs` (misconfiguration invalidates all sessions, cookies, identity links).
- Editing pre-existing migrations (vs adding new ones).
- Modifying `Makefile`, Docker compose files, or production `appsettings`.
- Renaming public API endpoints or response shapes (breaks frontends — coordinate frontend change in same PR).
- Removing or changing intervals on background hosted services.
- Bumping major framework versions (.NET 10→11, Angular 21→22, Svelte 5→6).
- Anything labeled `// HACK` or `// WARNING` in the existing code — read context first.
