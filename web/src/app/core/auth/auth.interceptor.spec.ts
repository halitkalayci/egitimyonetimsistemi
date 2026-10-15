import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ApiError } from '../http/api-error';
import { appInterceptors } from '../http/interceptors';
import { Session } from './auth.models';
import { AuthStore } from './auth.store';

const session = (accessToken: string): Session => ({
  accessToken,
  accessTokenExpiresAtUtc: '2026-10-15T09:15:00+00:00',
  role: 'Admin',
  mustChangePassword: false,
});

const problem = (status: number, detail: string) => ({
  type: 'https://egitimyonetimsistemi/errors/auth.session_not_renewable',
  title: 'Hata',
  status,
  detail,
  code: 'auth.session_not_renewable',
  traceId: '00-abc-def-00',
});

/**
 * Runs through the same interceptor list the application uses (core/http/interceptors.ts), so a
 * wrong ordering - the reason silent renewal once never ran - fails here (spec 0002, AC-10).
 */
describe('authInterceptor', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let store: AuthStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors(appInterceptors)), provideHttpClientTesting()],
    });

    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    store = TestBed.inject(AuthStore);
    store.set(session('expired-token'));
  });

  afterEach(() => http.verify());

  it('should renew the session and replay the request when the access token has expired', () => {
    let body: unknown;
    client.get('/api/users').subscribe((value) => (body = value));

    const first = http.expectOne('/api/users');
    expect(first.request.headers.get('Authorization')).toBe('Bearer expired-token');
    first.flush(problem(401, 'Oturumunuz sona erdi.'), { status: 401, statusText: 'x' });

    http.expectOne('/api/auth/refresh').flush(session('fresh-token'));

    const replay = http.expectOne('/api/users');
    expect(replay.request.headers.get('Authorization')).toBe('Bearer fresh-token');
    replay.flush([{ id: '1' }]);

    expect(body).toEqual([{ id: '1' }]);
    expect(store.accessToken()).toBe('fresh-token');
  });

  it('should renew only once when several requests expire at the same time', () => {
    const bodies: unknown[] = [];
    client.get('/api/users').subscribe((value) => bodies.push(value));
    client.get('/api/system-info').subscribe((value) => bodies.push(value));

    for (const url of ['/api/users', '/api/system-info']) {
      http.expectOne(url).flush(problem(401, 'Oturumunuz sona erdi.'), { status: 401, statusText: 'x' });
    }

    // Two parallel refreshes would present the same secret twice - the server's theft signal.
    http.expectOne('/api/auth/refresh').flush(session('fresh-token'));

    for (const url of ['/api/users', '/api/system-info']) {
      const replay = http.expectOne(url);
      expect(replay.request.headers.get('Authorization')).toBe('Bearer fresh-token');
      replay.flush({ url });
    }

    expect(bodies).toHaveLength(2);
  });

  it('should end the session and report a user message when renewal is refused', () => {
    let failure: ApiError | undefined;
    client.get('/api/users').subscribe({ error: (error: ApiError) => (failure = error) });

    http
      .expectOne('/api/users')
      .flush(problem(401, 'Oturumunuz sona erdi.'), { status: 401, statusText: 'x' });
    http
      .expectOne('/api/auth/refresh')
      .flush(problem(401, 'Oturumunuz sona erdi. Lütfen tekrar giriş yapın.'), {
        status: 401,
        statusText: 'x',
      });

    expect(store.isAuthenticated()).toBe(false);
    expect(failure).toEqual({ message: 'Oturumunuz sona erdi.' });
  });
});
