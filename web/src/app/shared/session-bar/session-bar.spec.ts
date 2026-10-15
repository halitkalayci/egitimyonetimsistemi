import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthStore } from '../../core/auth/auth.store';
import { appInterceptors } from '../../core/http/interceptors';
import { SessionBar } from './session-bar';

describe('SessionBar', () => {
  let fixture: ComponentFixture<SessionBar>;
  let http: HttpTestingController;
  let store: AuthStore;
  let navigate: ReturnType<typeof vi.spyOn>;

  const element = (testId: string): HTMLElement | null =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SessionBar],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors(appInterceptors)),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpTestingController);
    store = TestBed.inject(AuthStore);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    store.set({
      accessToken: 'live-token',
      accessTokenExpiresAtUtc: '2026-10-15T09:15:00+00:00',
      role: 'Participant',
      mustChangePassword: false,
    });

    fixture = TestBed.createComponent(SessionBar);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('should end only this device session on "Çıkış" and go to the login page', async () => {
    element('logout')!.click();

    const request = http.expectOne('/api/auth/logout');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(store.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/giris']);
  });

  it('should send the access token for "Tüm oturumları kapat", which requires it', async () => {
    element('logout-all')!.click();

    // A prefix match on /api/auth/logout once sent this request without a token.
    const request = http.expectOne('/api/auth/logout-all');
    expect(request.request.headers.get('Authorization')).toBe('Bearer live-token');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();

    expect(store.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/giris']);
  });

  it('should still end the local session when the server cannot be reached', async () => {
    element('logout')!.click();

    http.expectOne('/api/auth/logout').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(store.isAuthenticated()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/giris']);
  });

  it('should render nothing while signed out', () => {
    store.clear();
    fixture.detectChanges();

    expect(element('logout')).toBeNull();
    expect(element('logout-all')).toBeNull();
  });
});
