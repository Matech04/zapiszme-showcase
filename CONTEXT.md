# 💈 Project Context: Barber Shop Booking & CMS

## 1. Project Overview
A multi-tenant SaaS for barber shops and salons. The system has an admin/staff dashboard for salon operations and a public booking flow for customers.

- **Backend:** .NET 10, ASP.NET Core, EF Core, PostgreSQL, MediatR, FluentValidation, NSwag.
- **Admin dashboard:** Angular 21, PrimeNG 21, Tailwind CSS, RxJS.
- **Public booking app:** Astro 6 + Svelte 5, Tailwind CSS, generated TypeScript API client.
- **Architecture:** Clean Architecture with strong tenant isolation, domain rules in aggregates/services, and CQRS handlers in Application.
- **Backend-Frontend communication:** TypeScript clients are generated from OpenAPI/NSwag; generated clients must not be edited manually.

## 2. Current Priority

### Authentication & Authorization Refactor
The current planned work is a refactor of Authentication and Authorization:

- **Direction:** resign from Microsoft Entra ID / MSAL and move to **.NET Identity**.
- **Current state:** backend uses JWT Bearer + Microsoft Identity Web configured from `AzureAd`; dashboard uses `@azure/msal-angular`; tenant resolution for authenticated staff is based on JWT `oid` mapped to `Employee.EntraId`.
- **Target expectation:** tenant and employee resolution should be based on the application identity model from `.NET Identity`, not Entra `oid`.
- **Areas affected:** `App.Api` authentication setup, authorization policies/claims, `TenantIdentifierMiddleware`, employee-user linking, dashboard login/session flow, generated API auth assumptions, tests and seed data.

## 3. Core Architectural Patterns

### Multi-tenancy (Shared Database)
- **Logic:** tenant-scoped entities carry `TenantId`.
- **Global Query Filters:** `ApplicationDbContext` filters tenant data through `ICurrentTenantService`.
- **Automatic Validation:** `SaveChangesAsync` prevents cross-tenant writes and throws `TenantViolation` on mismatches.
- **Tenant resolution:**
  - **Admin/staff API today:** authenticated user JWT `oid` -> `Employee.EntraId` -> `HttpContext.Items` -> `ICurrentTenantService`.
  - **Public booking API:** salon slug from `api/booking/{slug}/...` resolves the tenant.
  - **Planned:** replace Entra-based staff resolution with `.NET Identity` user/claims linked to employee and tenant.

### Soft Delete
- Entities that support soft delete use `ISoftDelete` / `IsActive`.
- `DeletionService` deactivates records instead of physically deleting them.
- Global filters hide inactive records where supported.

### Backend Architecture
- **Domain:** aggregates, value objects, domain exceptions, domain services such as `AppointmentService`.
- **Application:** CQRS handlers with MediatR, DTOs, FluentValidation, tenant-aware base handlers, access checks like `EmployeeMutationAccess`.
- **Infrastructure:** EF Core persistence, configurations, migrations, repositories, current tenant/user services, email/OTP infrastructure.
- **Api:** controllers, authentication/authorization setup, middleware, exception handling, OpenAPI document generation.

### Frontend Architecture
- **Dashboard domains:** `appointments`, `customers`, `employees`, `services`, `vatRates`, with `feature`, `ui`, and `data-access` organization where applicable.
- **Reusable dashboard base components:** `BaseReadComponent<T>`, `BaseWriteComponent<T>`, `BaseCrudComponent<T>`, `BaseFormComponent<T>`, `BaseCardComponent<T>`.
- **Dashboard UI:** PrimeNG components, Tailwind layout utilities, route guards, HTTP interceptors, toast/error handling.
- **Public booking UI:** Astro page per salon slug with Svelte booking components and a separate booking OpenAPI client.

## 4. Current Implementation Status

### Backend Modules
- **Tenants:** tenant catalog and public salon lookup by slug.
- **Salon settings:** current salon settings, including booking configuration such as slot step and customer verification channel.
- **Services catalog:** service categories and services with price, duration, VAT rate, ordering and soft delete.
- **Employees:** employee CRUD, assigned services, weekly schedules, special days/overrides, leaves and self-vs-manager access rules.
- **Customers:** customer CRUD, search, phone normalization and appointment history.
- **VAT rates:** CRUD/management for rates used by services.
- **Appointments:** staff booking engine with collision detection, availability checks, notes, reschedule, status changes and lifecycle rules.
- **Public booking:** public catalog, available slots, appointment hold/lease, OTP request/verification and booking update.

### Authorization Model Today
- Policies in `App.Api`:
  - `SystemAdminOnly`: `Admin`
  - `BusinessManagement`: `Owner`
  - `StaffManagement`: `Owner` or `Manager`
  - `GeneralAccess`: `Owner`, `Manager` or `Employee`
- Sensitive employee mutations use application-level checks so employees can manage their own resources, while Owner/Manager can act on staff within the tenant.
- Integration tests use a test authentication handler with headers for user id and roles.

### Booking & Appointment Features
- **Staff appointments:** create appointment for an existing customer, phone-based customer, or guest (`isGuest`, nullable `CustomerId`).
- **Public appointments:** create hold, request OTP, verify OTP and confirm/update public booking.
- **Scheduling rules:** business timezone `Europe/Warsaw`; no booking in the past; available slots omit past slots for today and return none for past calendar days.
- **Status flow:** `Pending` -> `Booked` / `InProgress` / `Completed` / `Canceled`.
- **Rebooking canceled slots:** database unique index on `(EmployeeId, Date, StartTime)` is partial for non-canceled appointments.
- **Admin schedule UI:** daily timeline, visit details, confirm/cancel, edit term, guest and phone-only customer display.

### Error Handling & Validation
- Backend maps domain/application exceptions to Problem Details.
- Frontend maps known error codes to user-facing Polish messages.
- `AppointmentBookingRuleException` and booking/OTP domain errors are part of the public API contract.

### Tests
- Backend has unit and integration tests across Domain, Application and Api projects.
- Coverage includes appointments, tenant rules, employees, services, customers, VAT rates, OTP flow, public booking API, staff panel API, authorization and rate limiting.

## 5. API Clients

- **Dashboard client:** generated by NSwag from `App.Api` during `dotnet build`; do not hand-edit generated `api-client.ts`.
- **Public booking client:** generated separately for booking controllers and used by the Astro/Svelte app; do not hand-edit `web/src/lib/booking-openapi-client.ts`.
- For local OpenAPI generation without PostgreSQL startup, the build uses `SKIP_DB_STARTUP=1` so `Program.cs` skips migrate/seed during codegen.

## 6. Development Guidelines

### Coding Standards
- Prefer strong typing: avoid `any` in TypeScript and `dynamic` in C#.
- Use DTOs for API communication.
- Put business operations behind MediatR handlers.
- Keep domain rules in aggregates or domain services.
- Preserve tenant boundaries in every query, command and repository method.
- Do not edit generated API clients; change controllers/DTOs and regenerate.

### Frontend Conventions
- Use domain folders and existing base components before introducing new abstractions.
- Use PrimeNG + Tailwind consistently with existing dashboard components.
- Keep public booking concerns in the `web` app and staff/admin concerns in the `dashboard` app.
- Keep Polish user-facing messages consistent with existing error/message maps.

## 7. Deployment & Tooling

- **Solution:** `backend/backend.slnx`.
- **Docker:** `docker-compose.yml` provides PostgreSQL/development services.
- **Migrations:** EF Core migrations live in Infrastructure. Apply with `dotnet ef database update` using `App.Api` as startup/design-time context as needed.
- **OpenAPI / NSwag:** generated on successful `App.Api` build.
- **Testing:** xUnit for backend unit/integration tests; Vitest is used in frontend packages where configured.
