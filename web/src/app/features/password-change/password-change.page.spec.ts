import { Component } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { authGuard, mustChangePasswordGuard } from '../../core/auth/auth.guard';
import { AuthStore } from '../../core/auth/auth.store';
import { appInterceptors } from '../../core/http/interceptors';
import { PasswordChangePage } from './password-change.page';

describe('PasswordChangePage', () => {
  let fixture: ComponentFixture<PasswordChangePage>;
  let http: HttpTestingController;
  let store: AuthStore;
  let router: Router;

  const element = (testId: string): HTMLElement | null =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);

  const type = (testId: string, value: string): void => {
    const input = element(testId) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const submit = async (): Promise<void> => {
    type('current-password', 'Gecici-Sifre-1234');
    type('new-password', 'Yeni-Sifre-45678');
    (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PasswordChangePage],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors(appInterceptors)),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
    router = TestBed.inject(Router);
    store.set({
      accessToken: 'temporary-session',
      accessTokenExpiresAtUtc: '2026-10-15T09:15:00+00:00',
      role: 'Participant',
      mustChangePassword: true,
    });

    fixture = TestBed.createComponent(PasswordChangePage);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('should continue on the fresh session and open the welcome page when the change succeeds', async () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    await submit();

    http.expectOne('/api/auth/change-password').flush({
      accessToken: 'fresh-session',
      accessTokenExpiresAtUtc: '2026-10-15T09:30:00+00:00',
      role: 'Participant',
      mustChangePassword: false,
    });
    await fixture.whenStable();

    // spec 0002 UX: "Zorunlu şifre değiştirme - Success (karşılama sayfası)", no second login.
    expect(store.accessToken()).toBe('fresh-session');
    expect(store.mustChangePassword()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/']);
  });

  it('should name the violated rule when the new password is refused', async () => {
    await submit();

    http.expectOne('/api/auth/change-password').flush(
      {
        status: 400,
        detail: 'Şifre en az 10 karakter olmalıdır.',
        code: 'auth.password_too_short',
        traceId: '00-abc-def-00',
      },
      { status: 400, statusText: 'x' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('error')?.textContent).toContain('en az 10 karakter');
    expect(element('error')?.getAttribute('role')).toBe('alert');
  });
});

@Component({ template: '<p>sayfa</p>' })
class StubPage {}

/** spec 0002, AC-23: while the temporary password stands, every other page leads back here. */
describe('mustChangePasswordGuard', () => {
  const routes = [
    { path: 'sifre-degistir', canActivate: [authGuard], component: StubPage },
    { path: 'kullanicilar', canActivate: [authGuard, mustChangePasswordGuard], component: StubPage },
    { path: '', canActivate: [authGuard, mustChangePasswordGuard], component: StubPage },
  ];

  const signIn = (mustChangePassword: boolean): void =>
    TestBed.inject(AuthStore).set({
      accessToken: 'token',
      accessTokenExpiresAtUtc: '2026-10-15T09:15:00+00:00',
      role: 'Admin',
      mustChangePassword,
    });

  beforeEach(() => TestBed.configureTestingModule({ providers: [provideRouter(routes)] }));

  it('should redirect back to the password page when navigating elsewhere', async () => {
    signIn(true);
    const harness = await RouterTestingHarness.create();

    for (const url of ['/', '/kullanicilar']) {
      await harness.navigateByUrl(url);
      expect(TestBed.inject(Router).url).toBe('/sifre-degistir');
    }
  });

  it('should let the user through once the password has been changed', async () => {
    signIn(false);
    const harness = await RouterTestingHarness.create();

    await harness.navigateByUrl('/kullanicilar');

    expect(TestBed.inject(Router).url).toBe('/kullanicilar');
  });
});
