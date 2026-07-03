# Highlight 02 — Booking domain & anti-abuse

**The problem.** The public booking flow is **anonymous**, and confirming a
booking sends an **SMS OTP** — which costs real money (smsapi.pl). That combination
invites two attacks: squatting slots so real clients can't book, and driving SMS
sends to drain the salon's credits. The flow has to be pleasant for a real client
and hostile to an abuser at the same time.

---

## Slot holds with time-boxed leases

Picking a slot doesn't book it — it creates a **hold** with a short lease, well
before any OTP is sent. Two TTLs bound the flow:

- `HoldTtl = 60s` — the window to enter contact details after picking a slot.
- `OtpLeaseTtl = 3min` — the window to enter the code once the OTP is sent.

An abandoned hold simply expires and the slot frees itself; a background job
sweeps holds whose lease has passed. No slot is ever locked permanently.

```csharp
var lease = new HoldLease(Guid.NewGuid(), _timeProvider.GetUtcNow().UtcDateTime.Add(_holdOptions.HoldTtl));

var appointmentId = await _mediator.Send(
    new CreateAppointmentCommand(
        request.EmployeeId, request.ServiceIds, request.Date, request.StartTime,
        null, null,
        CreateAsBooked: false,
        CreateAsAwaitingOtp: true,   // status: AwaitingOtp until the code is verified
        HoldLease: lease,
        Source: AppointmentSource.Online),
    ct);
```

## Two layers of anti-abuse against slot hoarding

**Layer 1 — auto-cancel a session's own previous holds.** Hitting F5 and picking a
new slot shouldn't accumulate holds. The command auto-removes any earlier
`AwaitingOtp` holds for the same anonymous session before creating a new one:

```csharp
if (request.AnonSessionId is { } anonSessionId)
{
    await RemovePreviousHoldsForAnonSessionAsync(tenantId, anonSessionId, clientIp, ct);
}

// [M3] Hard cap on concurrent holds per IP — closes slot hoarding via AnonSessionId
// rotation / missing cookie (the auto-cancel above only works per session). Throws past the cap.
_otpProtection.RegisterHoldCreatedForIp(clientIp);
```

**Layer 2 — a hard per-IP cap on concurrent holds.** Auto-cancel keys on the
session cookie, so an abuser who rotates the session id or drops the cookie could
still stack holds. A per-IP counter caps concurrent holds regardless of session.
Crucially, releasing a hold (including the auto-cancel above) frees a slot **and**
decrements the IP counter, so a legitimate user who keeps re-picking slots never
drifts toward the cap:

```csharp
if (previous.Count > 0)
{
    _context.Appointments.RemoveRange(previous);

    // [M3] Free the per-IP slot so a legit user re-picking a slot doesn't hit the cap.
    foreach (var _ in previous)
        _otpProtection.ReleaseHoldForIp(clientIp);

    await _context.SaveChangesAsync(ct);
}
```

## Gating the write path, not just the read path

The read query that renders the booking page already reports whether a salon can
take bookings (subscription active? bookings paused?). But a crafted client can
skip the page and `POST` to the hold endpoint directly. If the gate lived only in
the read path, that would let someone force a fake booking — and an OTP SMS — on
an inactive or paused salon. So the **same gates are enforced again in the write
handler**:

```csharp
// [M4] Subscription gate in the hold write-flow: an inactive salon (PastDue /
// Canceled / expired Trial) can't create online bookings. The read query returned
// IsBookingAvailable=false, but a crafted client could call /hold directly, skipping it.
var tenant = await _context.Tenants.AsNoTracking()
    .FirstOrDefaultAsync(t => t.Id == tenantId, ct)
    ?? throw new NotFoundException("Tenant", tenantId);

var effectiveStatus = tenant.Subscription.EffectiveStatus;
if (effectiveStatus != SubscriptionStatus.Trial && effectiveStatus != SubscriptionStatus.Active)
    throw new BookingUnavailableException();

if (tenant.BookingPaused)
    throw new BookingPausedException();
```

This "validate at every trust boundary" habit — never assume the caller went
through the UI — runs through the whole booking surface.

## Availability as a pure domain service

Whether a slot can be booked is decided in the domain, independent of EF or HTTP,
which makes it exhaustively unit-testable. A slot is available if it doesn't
collide with an existing non-cancelled appointment **and** falls within the
employee's working hours:

```csharp
public async Task<bool> IsAvailableAsync(
    Employee employee, TimeRange timeRange, DateOnly date, Guid tenantId,
    Guid? ignoreAppointmentId = null, bool ignoreSchedule = false)
{
    var collision = await _appointmentRepository
        .HasCollisionAsync(employee.Id, timeRange, date, tenantId, ignoreAppointmentId);
    if (collision)
        return false;

    // Off-schedule save (staff panel): collision already checked — working hours/leave ignored.
    return ignoreSchedule || employee.IsAvailable(timeRange, date);
}
```

The same service also generates bookable start times, supporting two modes:
a regular working-window grid (`EmployeeAvailableSlotsList`) and a **fixed-slots**
mode where the operator hand-picks start times and overlaps are her deliberate
choice (`EmployeeFixedSlotsList`). Both handle the midnight wrap-around of
`TimeOnly` explicitly.

## Why this shape

- **Money is on the line.** Every SMS-adjacent path assumes a hostile caller.
- **Holds expire; nothing locks forever.** Good for real clients, useless for
  squatters.
- **Gates repeat at every boundary.** Read-path checks are UX; write-path checks
  are security.
- **Availability is pure domain logic.** No database needed to test the rules that
  matter most.
