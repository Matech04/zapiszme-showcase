# Highlight 03 — Passwordless demo mode

**The problem.** "Can I see it?" is the most important question a prospect (or a
recruiter) asks, and the worst possible answer is "sure, just register, confirm
your email, add some services, create a few appointments…". Friction there loses
people. I wanted a single link that drops anyone straight into a **realistic,
fully-working owner panel** — with zero signup and zero risk to real data.

**The approach.** `GET admin.zapisz.me/login?demo=1` provisions a brand-new,
fully-isolated tenant *per visitor*, seeds it with believable salon data, signs
the guest in passwordless, and lets a background job clean it up later. The demo
is the real product, not a mock — same routes, same handlers, same UI.

---

## One endpoint, one isolated tenant per visitor

The whole thing is a single anonymous, rate-limited endpoint. It creates a
passwordless user, a fresh tenant flagged `IsDemo`, seeds data, and issues a
session cookie:

```csharp
[AllowAnonymous]
[EnableRateLimiting("AuthSensitive")]
[HttpPost("start")]
public async Task<ActionResult<AuthSessionDto>> Start(CancellationToken ct)
{
    if (!IsDemoEnabled)
        return NotFound();

    // Hard cap on live demo tenants — anti-abuse (cleanup frees slots after TTL).
    var liveDemoCount = await _dbContext.Tenants.CountAsync(t => t.IsDemo, ct);
    if (liveDemoCount >= MaxLiveDemoTenants)
        return StatusCode(StatusCodes.Status503ServiceUnavailable, /* "demo temporarily busy" */ ...);

    var token = Guid.NewGuid().ToString("N")[..8];
    var slug  = $"demo-{token}";
    var email = $"demo-{token}@demo.zapisz.me";

    // Passwordless — the guest never types a password; they enter via SignInAsync below.
    // Confirmed flags = true so the account doesn't get swept by the unconfirmed-account cleanup.
    var user = new User(email, "Ania (demo)") { EmailConfirmed = true, PhoneNumberConfirmed = true };
    await _userManager.CreateAsync(user);
    await _userManager.AddToRoleAsync(user, OwnerRole);

    var tenant = new Tenant("Studio Stylizacji (DEMO)", slug, "Europe/Warsaw", "PLN");
    tenant.MarkAsDemo(nowUtc);   // flags the tenant: no real notifications, fast cleanup
    tenant.StartTrial();

    var owner = new Employee(tenant.Id, user.Id, "Ania", "Demo", email);
    _dbContext.Tenants.Add(tenant);
    _dbContext.Employees.Add(owner);

    _demoDataSeeder.Seed(_dbContext, tenant, owner, nowUtc);   // services, clients, appointments around "today"
    await _dbContext.SaveChangesAsync(ct);

    // Issues the session cookie — from here the guest is "logged in" as the demo salon owner.
    await _signInManager.SignInAsync(user, isPersistent: false);

    return Ok(new AuthSessionDto(user.Id, email, user.DisplayName, roles, tenant.Id, owner.Id, IsDemo: true));
}
```

Because the demo tenant is a **real** tenant, everything downstream —
multi-tenancy filters, the write guard, every handler — treats it exactly like a
paying customer. There is no separate "demo code path" to keep in sync, and no way
for a demo visitor to see or touch another tenant's data.

## Seeded to feel alive on first load

An empty panel demos badly. The `IDemoDataSeeder` fills the fresh tenant with a
realistic SOLO-salon dataset — a service catalogue, Polish client names, and
appointments spread **around "today"** — so the Calendar, Dashboard and Clients
screens are populated the moment the guest lands, not blank.

## Safe by construction

Several guards keep this from being a liability:

- **Flagged tenants send no real notifications.** `MarkAsDemo` means the SMS/email
  dispatcher skips demo tenants — a demo can never send a real message or spend a
  real SMS credit.
- **Auto-cleanup.** `DemoTenantCleanupHostedService` hard-deletes demo tenants
  past their TTL (a deliberate, documented exception to the app's soft-delete
  rule — demo data has no history worth keeping).
- **Two anti-abuse limits.** The endpoint is rate-limited (`AuthSensitive`) and
  capped at `MaxLiveTenants` concurrent demo tenants, returning `503` past the cap
  instead of unbounded provisioning.
- **Feature-flagged.** `Demo:Enabled` gates the whole thing (and the frontend
  "Try demo" button), so it can be turned off per environment.

## Why this shape

- **The demo is the product.** Reusing the real tenant machinery means the demo
  can't drift from reality and there's no second code path to maintain.
- **Isolation is free.** A per-visitor tenant inherits every isolation guarantee
  the rest of the system already has.
- **It's bounded.** Rate limit + live-tenant cap + TTL cleanup keep an open,
  anonymous "create me an account" endpoint from being abused.

This is also the feature that makes this very portfolio easy to evaluate:
[**admin.zapisz.me/login?demo=1**](https://admin.zapisz.me/login?demo=1).
