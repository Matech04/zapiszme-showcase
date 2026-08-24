import { HttpInterceptorFn } from '@angular/common/http';

/** Tenant w API ustala middleware z JWT (`Employee`); nagłówek `X-Tenant-Id` nie jest używany przez backend. */
export const tenantInterceptor: HttpInterceptorFn = (req, next) => next(req);
