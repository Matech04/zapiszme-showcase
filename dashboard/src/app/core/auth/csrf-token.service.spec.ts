import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CsrfTokenService } from './csrf-token.service';
import { environment } from '../../../environments/environment';

describe('CsrfTokenService', () => {
  const url = `${environment.apiBaseUrl}/api/auth/csrf`;
  let service: CsrfTokenService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [CsrfTokenService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CsrfTokenService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('pobiera token raz i reużywa cache przy kolejnych wywołaniach', () => {
    let first = '';
    service.getToken().subscribe((t) => (first = t));
    httpMock.expectOne(url).flush({ token: 'abc' });
    expect(first).toBe('abc');

    let second = '';
    service.getToken().subscribe((t) => (second = t));
    httpMock.expectNone(url); // brak drugiego GET — kluczowe dla usunięcia churnu połączeń
    expect(second).toBe('abc');
  });

  it('deduplikuje równoległe żądania do jednego GET', () => {
    const results: string[] = [];
    service.getToken().subscribe((t) => results.push(t));
    service.getToken().subscribe((t) => results.push(t));
    httpMock.expectOne(url).flush({ token: 'xyz' });
    expect(results).toEqual(['xyz', 'xyz']);
  });

  it('refresh() unieważnia cache i pobiera świeży token', () => {
    service.getToken().subscribe();
    httpMock.expectOne(url).flush({ token: 'old' });

    let refreshed = '';
    service.refresh().subscribe((t) => (refreshed = t));
    httpMock.expectOne(url).flush({ token: 'new' });
    expect(refreshed).toBe('new');

    let cached = '';
    service.getToken().subscribe((t) => (cached = t));
    httpMock.expectNone(url);
    expect(cached).toBe('new');
  });

  it('invalidate() wymusza ponowne pobranie', () => {
    service.getToken().subscribe();
    httpMock.expectOne(url).flush({ token: 'a' });

    service.invalidate();

    let after = '';
    service.getToken().subscribe((t) => (after = t));
    httpMock.expectOne(url).flush({ token: 'b' });
    expect(after).toBe('b');
  });

  it('zwraca pusty string i nie zapisuje cache przy błędzie (kolejne wywołanie próbuje ponownie)', () => {
    let val = 'unset';
    service.getToken().subscribe((t) => (val = t));
    httpMock.expectOne(url).flush('boom', { status: 500, statusText: 'Server Error' });
    expect(val).toBe('');

    let retry = '';
    service.getToken().subscribe((t) => (retry = t));
    httpMock.expectOne(url).flush({ token: 'ok' });
    expect(retry).toBe('ok');
  });
});
