import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';

import { AuthService } from './auth.service';
import { AuthStore } from './auth.store';

/**
 * Endpoints that authenticate by credentials or by the refresh cookie, never by the access token.
 * Matched by exact path: a prefix match would also catch /api/auth/logout-all, which does need it.
 */
const AUTH_ENDPOINTS = ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'];

/**
 * Attaches the in-memory access token and, when a request comes back 401, renews the session once
 * through the refresh cookie and replays the request. The user keeps working across the fifteen
 * minute token lifetime without noticing (spec 0002, AC-10).
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const store = inject(AuthStore);
  const auth = inject(AuthService);
  const isAuthEndpoint = AUTH_ENDPOINTS.includes(request.url);
  const token = store.accessToken();

  const authorised = token && !isAuthEndpoint ? withBearer(request, token) : request;

  return next(authorised).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || isAuthEndpoint) {
        return throwError(() => error);
      }

      // One renewal attempt only: if refreshing also fails, the session is genuinely over. The
      // original 401 is passed on, not the renewal's failure - that one already went through the
      // whole pipeline as its own request and is a user message by now, not a response.
      return auth.refresh().pipe(
        catchError(() => {
          store.clear();
          return throwError(() => error);
        }),
        switchMap((session) => next(withBearer(request, session.accessToken))),
      );
    }),
  );
};

function withBearer(request: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}
