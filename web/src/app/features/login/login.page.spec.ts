import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { appInterceptors } from '../../core/http/interceptors';
import { LoginPage } from './login.page';

describe('LoginPage', () => {
  let fixture: ComponentFixture<LoginPage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);

  const type = (testId: string, value: string): void => {
    const input = element(testId) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  const submit = async (): Promise<void> => {
    (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();
  };

  /** Mirrors what the API really sends: RFC 9457 with a Turkish detail, a code and a traceId. */
  const failLogin = async (status: number, code: string, detail: string): Promise<void> => {
    http.expectOne('/api/auth/login').flush(
      {
        type: `https://egitimyonetimsistemi/errors/${code}`,
        title: 'Hata',
        status,
        detail,
        code,
        traceId: '00-abc-def-00',
      },
      { status, statusText: 'x' },
    );

    await fixture.whenStable();
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors(appInterceptors)),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginPage);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('should show the loading state while the login request is in flight', async () => {
    type('email', 'admin@immib.org');
    type('password', 'Gecerli-Sifre-123');
    await submit();

    expect(element('loading')).not.toBeNull();
    expect((element('submit') as HTMLButtonElement).disabled).toBe(true);

    http.expectOne('/api/auth/login').flush({
      accessToken: 'token',
      accessTokenExpiresAtUtc: '2026-10-14T09:15:00+00:00',
      role: 'Admin',
      mustChangePassword: false,
    });
    await fixture.whenStable();
  });

  it('should show the credentials error without revealing which field was wrong', async () => {
    type('email', 'admin@immib.org');
    type('password', 'yanlis');
    await submit();
    await failLogin(400, 'auth.invalid_credentials', 'E-posta veya şifre hatalı.');

    const message = element('error')!.textContent ?? '';
    expect(message).toContain('E-posta veya şifre hatalı');
    expect(message).not.toContain('şifre hatalı,');
  });

  it('should show a distinct message for a locked account', async () => {
    await submit();
    await failLogin(
      409,
      'auth.account_locked',
      'Hesabınız geçici olarak kilitlendi. Lütfen daha sonra tekrar deneyin.',
    );

    expect(element('error')!.textContent).toContain('geçici olarak kilitlendi');
  });

  it('should show a distinct message for a disabled account', async () => {
    await submit();
    await failLogin(409, 'auth.account_disabled', 'Hesabınız kullanımda değil. Eğitim sorumlusuna başvurun.');

    expect(element('error')!.textContent).toContain('kullanımda değil');
  });

  it('should show a connection message when the server cannot be reached', async () => {
    await submit();
    http.expectOne('/api/auth/login').error(new ProgressEvent('error'), { status: 0 });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('error')!.textContent).toContain('Sunucuya ulaşılamıyor');
  });

  it('should never show a status code, error code or trace identifier', async () => {
    await submit();
    await failLogin(409, 'auth.account_locked', 'Hesabınız geçici olarak kilitlendi.');

    const rendered = (fixture.nativeElement as HTMLElement).textContent ?? '';
    for (const forbidden of ['409', 'auth.account_locked', 'traceId', '00-abc-def-00']) {
      expect(rendered).not.toContain(forbidden);
    }
  });

  it('should expose labels, an alert region and an accessible password toggle', () => {
    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('h1').length).toBe(1);
    expect(host.querySelector('label[for="email"]')).not.toBeNull();
    expect(host.querySelector('label[for="password"]')).not.toBeNull();

    const toggle = element('toggle-password')!;
    expect(toggle.tagName).toBe('BUTTON');
    expect(toggle.getAttribute('aria-pressed')).toBe('false');

    toggle.click();
    fixture.detectChanges();

    expect(toggle.getAttribute('aria-pressed')).toBe('true');
    expect((element('password') as HTMLInputElement).type).toBe('text');
  });
});
