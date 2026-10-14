/**
 * What the UI is allowed to know about a failure: a Turkish message, nothing else. Status codes,
 * raw bodies and stack traces never travel past the interceptor (docs/conventions.md).
 */
export interface ApiError {
  readonly message: string;
}

export const GENERIC_ERROR_MESSAGE =
  'Bilgiler alınamadı. Lütfen birkaç saniye sonra tekrar deneyin.';
