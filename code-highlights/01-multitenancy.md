# Highlight 01 — Multi-tenancy done in depth

**The problem.** In a booking SaaS, every salon shares one database. The single
worst bug I could ship is leaking one salon's clients, appointments or revenue to
another. A single forgotten `WHERE tenant_id = ?` would do it.

**The approach.** Don't rely on remembering to filter. Enforce isolation in two
independent layers so a mistake in one is caught by the other.

---

## Layer 1 — Read isolation via global query filters

Every tenant-scoped entity implements `ITenantEntity`. In
`ApplicationDbContext.OnModelCreating` each one gets a `HasQueryFilter` bound to
the current request's tenant. Reads physically cannot see another tenant's rows —
there is no code path that "forgets" the filter, because it lives on the model,
not in the handlers.

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

    modelBuilder.Entity<Employee>()
      .HasQueryFilter(e => e.TenantId == _currentTenantService.TenantId && e.IsActive);

    modelBuilder.Entity<Customer>()
      .HasQueryFilter(c => c.TenantId == _currentTenantService.TenantId && c.IsActive);

    modelBuilder.Entity<Appointment>()
      .HasQueryFilter(a => a.TenantId == _currentTenantService.TenantId);

    // ...one per tenant entity. Soft-deletable ones also fold in `&& e.IsActive`,
    // so inactive rows disappear from every query by default.
}
```

Note the filter also composes **soft-delete** (`IsActive`) for entities that
support it, so deactivated records vanish from normal queries while their history
(FK references from appointments) stays intact.

### The deliberate exceptions are documented, not hidden

A blanket rule is only trustworthy if its exceptions are explicit. A few entities
are intentionally *platform-global* (shared across all tenants) or accessed from
contexts with no ambient tenant. Each is commented at the exact spot it deviates:

```csharp
// PromoCodeRedemption is tenant-scoped (filter on). But PromoCode itself is
// GLOBAL — a conscious exception: every tenant looks codes up by `Code` without
// a tenant context, so no query filter.
modelBuilder.Entity<PromoCodeRedemption>()
  .HasQueryFilter(r => r.TenantId == _currentTenantService.TenantId);

// SmsTemplate is tenant-scoped. The admin moderation queue (cross-tenant) and the
// send-time resolver (background jobs, no ambient tenant) deliberately use
// IgnoreQueryFilters with an explicit Where(TenantId == ...) — isolation kept by
// the explicit filter.
modelBuilder.Entity<SmsTemplate>()
  .HasQueryFilter(t => t.TenantId == _currentTenantService.TenantId);
```

## Layer 2 — Write isolation via a SaveChanges guard

Query filters protect *reads*. They do nothing for a handler that fetches an
entity in one tenant's context and tries to persist it under another. So the
`DbContext` overrides `SaveChangesAsync` and inspects every changed
`ITenantEntity` before it hits the database:

```csharp
public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    var currentTenantId = _currentTenantService.TenantId;

    foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
    {
        switch (entry.State)
        {
            case EntityState.Added:
            case EntityState.Modified:
            // Deleted is covered too: hard-deleting a non-ISoftDelete entity
            // (e.g. Appointment) used to slip past the guard — a handler without
            // a manual check could delete another tenant's row with no violation.
            case EntityState.Deleted:
                if (currentTenantId.HasValue && entry.Entity.TenantId != currentTenantId.Value)
                {
                    // Critical: an attempt to breach data isolation.
                    throw new TenantViolation();
                }
                break;
        }
    }

    return base.SaveChangesAsync(cancellationToken);
}
```

Two details I care about here:

- **`Deleted` is in the switch.** An earlier version only guarded add/modify. A
  hard-delete of an entity that isn't soft-deletable (like `Appointment`) could
  therefore bypass the check. Closing that gap was a real fix, not a hypothetical.
- **`currentTenantId == null` fails open, on purpose.** Owner registration creates
  a `Tenant` + `Employee` *before* any tenant context exists, and background jobs
  operate cross-tenant via `IgnoreQueryFilters`. Both are commented at the guard
  so the exception reads as intentional, not as a hole.

## The base class that makes it ergonomic

Handlers don't touch any of this plumbing. They extend `TenantHandler<,>`, which
exposes a `TenantId` and throws a typed error if a request somehow arrived with no
tenant:

```csharp
public abstract class TenantHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    protected readonly ICurrentTenantService CurrentTenantService;
    protected Guid TenantId => CurrentTenantService.TenantId ?? throw new NoTenantHeader();

    protected TenantHandler(ICurrentTenantService currentTenantService)
        => CurrentTenantService = currentTenantService;

    public abstract Task<TResponse> Handle(TRequest request, CancellationToken ct);
}
```

## Why this shape

- **Two layers beat one.** Read filters and the write guard fail independently;
  it takes two mistakes, not one, to leak data.
- **Enforcement lives on the model, not the developer.** The safe path is the
  default path.
- **Exceptions are visible.** Every `IgnoreQueryFilters` and every global entity
  is commented where it happens, so an auditor can find them in minutes.

Every tenant-scoped handler ships with a cross-tenant `TenantViolation` test in
addition to its happy path — the guarantee is regression-tested, not assumed.
