# Dashboard Role Model

## Role landing

- Owner -> `/admin/resources`
- Manager -> `/admin/schedule`
- Employee -> `/admin/schedule/:employeeId` (when session includes `employeeId`)
- Kiosk -> `/admin/schedule/:employeeId` (when session includes `employeeId`)

## Navigation visibility

- Plan dnia: owner, manager, employee, kiosk
- Moje uslugi: employee
- Panel wlasciciela: owner, manager
- Stawki VAT: owner, manager
- Klienci: owner, manager
- Zespol i Uslugi: owner, manager

## Route access policy

- Staff management scope (`staffManagementGuard`): owner, manager
  - `/admin/salon`
  - `/admin/vat-rates`
  - `/admin/customers/**`
  - `/admin/resources/**`
- General authenticated scope (`authGuard`): all logged roles
  - `/admin/schedule/**`
  - `/admin/appointment/**`
  - `/admin/my-services`

## UX principles per role

- Owner: strategic + operational, full scope.
- Manager: daily operations and team/customer service, no owner-only governance actions outside staff management scope.
- Employee: focus on own schedule execution.
- Kiosk: fast intake and schedule handling with reduced cognitive load.
