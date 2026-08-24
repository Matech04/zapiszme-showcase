# RTM Example (Backend)

## Purpose

This document is a practical `Requirements Traceability Matrix (RTM)` example for backend testing.
It helps ensure every requirement is mapped to tests and release decisions are evidence-based.

## Status Legend

- `NotStarted` - no tests mapped yet
- `InProgress` - test implementation in progress
- `Passed` - mapped tests green in CI/local run
- `Failed` - at least one mapped test failing
- `Blocked` - cannot verify due to external blocker

## Priority Legend

- `High` - release-critical, business/security impact
- `Medium` - important, but not blocking for all releases
- `Low` - non-critical, can be scheduled later

## Master RTM (High-Level)


| RequirementId | Domain        | Requirement                                                         | Priority | TestLevels               | TestCaseIds                              | Owner   | Status     | LastEvidence           | Gaps/Notes                               |
| ------------- | ------------- | ------------------------------------------------------------------- | -------- | ------------------------ | ---------------------------------------- | ------- | ---------- | ---------------------- | ---------------------------------------- |
| AUTH-001      | Auth          | User can sign in with valid credentials and receives token/session. | High     | Integration, API         | IT-AUTH-001, API-AUTH-001                | Backend | Passed     | CI #248 (2026-05-11)   | Add refresh-token rotation test.         |
| AUTH-003      | Auth          | After N failed attempts, login is rate-limited.                     | High     | Integration, Security    | API-AUTH-018, SEC-AUTH-006               | Backend | InProgress | Local run (2026-05-11) | Missing multi-instance limiter scenario. |
| BOOK-001      | Booking       | Customer can create booking only in available time slots.           | High     | Domain, Application, API | DOM-BOOK-012, APP-BOOK-021, API-BOOK-004 | Backend | Passed     | CI #248 (2026-05-11)   | Add DST boundary scenario.               |
| BOOK-005      | Booking       | Cross-tenant booking data access is denied.                         | High     | API, Security            | API-BOOK-017, SEC-MT-003                 | Backend | Passed     | CI #248 (2026-05-11)   | Validate all role combinations.          |
| APPT-002      | Appointments  | Appointment status transitions follow lifecycle rules.              | High     | Domain, Application      | DOM-APPT-004, APP-APPT-011               | Backend | Passed     | CI #248 (2026-05-11)   | Add cancellation edge timing test.       |
| TEN-004       | Tenants       | Tenant policy changes affect booking behavior immediately.          | Medium   | Application, API         | APP-TEN-008, API-TEN-006                 | Backend | InProgress | Local run (2026-05-11) | Missing cache invalidation scenario.     |
| NOTIF-002     | Notifications | Booking confirmation notification is sent once.                     | Medium   | Integration              | IT-NOTIF-003                             | Backend | NotStarted | -                      | Need test mailbox assertion strategy.    |
| SUB-003       | Subscription  | Plan limits block over-quota actions with clear error code.         | High     | Domain, API, Contract    | DOM-SUB-002, API-SUB-005, CT-SUB-001     | Backend | Failed     | CI #247 (2026-05-10)   | API error code mismatch to contract.     |


## Domain RTM Example: Booking


| RequirementId | ScenarioType | Scenario                                          | TestLevels       | TestCaseIds                | Status     | Evidence  | Notes                                    |
| ------------- | ------------ | ------------------------------------------------- | ---------------- | -------------------------- | ---------- | --------- | ---------------------------------------- |
| BOOK-001      | HappyPath    | Create booking in valid open slot.                | Application, API | APP-BOOK-021, API-BOOK-004 | Passed     | CI #248   | Covered for standard timezone.           |
| BOOK-001      | Negative     | Reject booking outside working hours.             | Domain, API      | DOM-BOOK-015, API-BOOK-009 | Passed     | CI #248   | Includes weekend case.                   |
| BOOK-001      | EdgeCase     | Handle DST transition hour correctly.             | Domain, API      | DOM-BOOK-020, API-BOOK-013 | InProgress | Local run | Needs Europe/Warsaw DST variant.         |
| BOOK-005      | Security     | User from tenant A cannot read tenant B bookings. | API, Security    | API-BOOK-017, SEC-MT-003   | Passed     | CI #248   | Verify admin override policy separately. |
| BOOK-007      | Validation   | Reject invalid payload with stable error shape.   | API, Contract    | API-BOOK-022, CT-BOOK-002  | Passed     | CI #248   | Contract snapshot stored in repo.        |


## Domain RTM Example: Auth


| RequirementId | ScenarioType    | Scenario                                                | TestLevels            | TestCaseIds                  | Status     | Evidence  | Notes                                       |
| ------------- | --------------- | ------------------------------------------------------- | --------------------- | ---------------------------- | ---------- | --------- | ------------------------------------------- |
| AUTH-001      | HappyPath       | Sign in returns token with expected claims.             | Integration, API      | IT-AUTH-001, API-AUTH-001    | Passed     | CI #248   | Add token expiry boundary assertion.        |
| AUTH-002      | Negative        | Invalid password returns correct status and error code. | API, Contract         | API-AUTH-007, CT-AUTH-001    | Passed     | CI #248   | Covered for invalid user and password.      |
| AUTH-003      | AbuseProtection | Login is throttled after N failures.                    | Integration, Security | API-AUTH-018, SEC-AUTH-006   | InProgress | Local run | Multi-instance limiter pending.             |
| AUTH-004      | Authorization   | Endpoint access is role-constrained.                    | API, Security         | API-AUTHZ-003, SEC-AUTHZ-001 | Passed     | CI #248   | Add tenant admin vs global admin diff case. |


## Suggested TestCase ID Convention

- `DOM-<DOMAIN>-NNN` for domain unit tests
- `APP-<DOMAIN>-NNN` for application unit/module tests
- `IT-<DOMAIN>-NNN` for integration tests
- `API-<DOMAIN>-NNN` for HTTP/API tests
- `CT-<DOMAIN>-NNN` for contract tests
- `SEC-<DOMAIN>-NNN` for security-focused tests
- `PERF-<DOMAIN>-NNN` for performance tests

Example:

- `DOM-BOOK-012`
- `API-AUTH-018`
- `SEC-MT-003`

## Release Exit Checklist (RTM-driven)

- All `High` priority requirements are `Passed`.
- No `Failed` requirements in Auth, Booking, Appointments, or Tenant Isolation.
- Every requirement has at least one mapped test case.
- Every `High` requirement has at least two levels of verification (for example `Application + API`).
- Security requirements have dedicated negative-path coverage.
- Contract tests for public API are green.
- Known gaps are explicitly accepted with mitigation and target date.

## How to Use This File

1. Keep this as `Master RTM`.
2. Add domain sections (or separate domain files) when rows become too many.
3. Update `LastEvidence` from CI after each merge.
4. For every production bug, add a new RTM row and a regression test ID.

