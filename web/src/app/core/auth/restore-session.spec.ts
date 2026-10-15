import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { appInterceptors } from '../http/interceptors';
import { authGuard } from './auth.guard';
import { AuthStore } from './auth.store';
import { restoreSession } from './restore-session';

/** A reload starts with an empty in-memory store; the refresh cookie decides what happens next. */
describe('restoreSession', () => {
  let http: HttpTestingController;
  let store: AuthStore;

  const start = (): Promise<void> => TestBed.runInInjectionContext(() => restoreSession());

  const guard = (): boolean | UrlTree =>
    TestBed.runInInjectionContext(
      () =>
        authGuard({} as ActivatedRouteSnapshot, {} as RouterStateSnapshot) as boolean | UrlTree,
    );

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors(appInterceptors)),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
  });

  afterEach(() => http.verify());

  it('should bring the session back on startup when the refresh cookie is still valid', async () => {
    const started = start();

    http.expectOne('/api/auth/refresh').flush({
      accessToken: 'restored-token',
      accessTokenExpiresAtUtc: '2026-10-15T09:15:00+00:00',
      role: 'Admin',
      mustChangePassword: false,
    });
    await started;

    expect(store.accessToken()).toBe('restored-token');
    expect(guard()).toBe(true);
  });

  it('should start signed out and send the user to the login page when there is no valid cookie', async () => {
    const started = start();

    http
      .expectOne('/api/auth/refresh')
      .flush(
        { status: 401, detail: 'Oturumunuz sona erdi.', code: 'auth.session_not_renewable' },
        { status: 401, statusText: 'x' },
      );
    await expect(started).resolves.toBeUndefined();

    expect(store.isAuthenticated()).toBe(false);
    expect(String(guard())).toBe('/giris');
  });
});
