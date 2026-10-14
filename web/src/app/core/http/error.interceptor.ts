import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

import { ApiError, GENERIC_ERROR_MESSAGE } from './api-error';

/**
 * The single place where an HTTP failure becomes a user-facing message. It reads RFC 9457
 * ProblemDetails when the server sends one and falls back to a generic sentence otherwise;
 * nothing technical (status code, SQL, stack trace, provider name) is passed on.
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) =>
  next(request).pipe(
    catchError((response: unknown) => {
      const error: ApiError = { message: messageFrom(response) };
      return throwError(() => error);
    }),
  );

function messageFrom(response: unknown): string {
  if (!(response instanceof HttpErrorResponse)) {
    return GENERIC_ERROR_MESSAGE;
  }

  const body = response.error as { detail?: unknown } | null;
  const detail = typeof body?.detail === 'string' ? body.detail.trim() : '';

  return detail.length > 0 ? detail : GENERIC_ERROR_MESSAGE;
}
