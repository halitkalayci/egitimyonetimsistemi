import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';

import { Session } from './auth.models';
import { AuthService } from './auth.service';

/**
 * The browser's Web Locks, reduced to what the service relies on: one holder per name at a time,
 * the others wait in order. jsdom has no navigator.locks, so without this stub the cross-tab path
 * would never run in a test.
 */
class QueueingLockManager {
  readonly requested: string[] = [];
  private tail: Promise<unknown> = Promise.resolve();

  request(name: string, callback: () => Promise<unknown>): Promise<unknown> {
    this.requested.push(name);
    const held = this.tail.then(() => callback());
    this.tail = held.catch(() => undefined);

    return held;
  }
}

const session = (accessToken: string): Session => ({
  accessToken,
  accessTokenExpiresAtUtc: '2026-10-15T09:15:00+00:00',
  role: 'Admin',
  mustChangePassword: false,
});

/** Lets queued promise callbacks run, so a waiting lock holder gets its turn. */
const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve));

describe('AuthService refresh across tabs', () => {
  let http: HttpTestingController;
  let locks: QueueingLockManager;
  let original: PropertyDescriptor | undefined;

  beforeEach(() => {
    locks = new QueueingLockManager();
    original = Object.getOwnPropertyDescriptor(navigator, 'locks');
    Object.defineProperty(navigator, 'locks', { value: locks, configurable: true });

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();

    if (original) {
      Object.defineProperty(navigator, 'locks', original);
    } else {
      delete (navigator as { locks?: unknown }).locks;
    }
  });

  it('should send the second tab’s renewal only after the first one has finished', async () => {
    // Two tabs: two services that share nothing but the cookie and the lock name.
    const firstTab = TestBed.runInInjectionContext(() => new AuthService());
    const secondTab = TestBed.runInInjectionContext(() => new AuthService());

    const first = firstValueFrom(firstTab.refresh());
    const second = firstValueFrom(secondTab.refresh());
    await settle();

    // Overlapping renewals would present the same secret twice - the server's theft signal.
    const running = http.match('/api/auth/refresh');
    expect(running).toHaveLength(1);
    running[0].flush(session('first-token'));
    expect((await first).accessToken).toBe('first-token');

    await settle();
    http.expectOne('/api/auth/refresh').flush(session('second-token'));
    expect((await second).accessToken).toBe('second-token');

    expect(locks.requested).toEqual(['egitim-auth-refresh', 'egitim-auth-refresh']);
  });

  it('should let the next tab renew when the previous renewal was refused', async () => {
    const firstTab = TestBed.runInInjectionContext(() => new AuthService());
    const secondTab = TestBed.runInInjectionContext(() => new AuthService());

    const first = firstValueFrom(firstTab.refresh());
    const second = firstValueFrom(secondTab.refresh());
    await settle();

    http
      .expectOne('/api/auth/refresh')
      .flush({ code: 'auth.session_not_renewable' }, { status: 401, statusText: 'Unauthorized' });
    await expect(first).rejects.toBeTruthy();

    // A failed holder must release the lock, or every other tab would hang forever.
    await settle();
    http.expectOne('/api/auth/refresh').flush(session('second-token'));
    expect((await second).accessToken).toBe('second-token');
  });
});
