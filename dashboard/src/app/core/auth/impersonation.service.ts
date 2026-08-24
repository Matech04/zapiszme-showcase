import { inject, Injectable } from '@angular/core';
import { AdminImpersonationClient, StartImpersonationBody, StartImpersonationResult, ImpersonationStatusDto } from '@core/api/api-client';
import { Observable } from 'rxjs';

/**
 * Tryb wsparcia (support impersonation) — cienka warstwa nad wygenerowanym klientem.
 * Cookie sesji ustawia/kasuje backend; po starcie/zakończeniu robimy pełny reload, żeby
 * `AuthSessionService.hydrate()` pobrało świeży kontekst (salon docelowy / powrót do admina).
 */
@Injectable({ providedIn: 'root' })
export class ImpersonationService {
  private readonly client = inject(AdminImpersonationClient);

  start(tenantId: string, reason: string, readOnly: boolean): Observable<StartImpersonationResult> {
    const body = { tenantId, reason, readOnly } as StartImpersonationBody;
    return this.client.start(body);
  }

  end(): Observable<unknown> {
    return this.client.end();
  }

  current(): Observable<ImpersonationStatusDto> {
    return this.client.current();
  }
}
