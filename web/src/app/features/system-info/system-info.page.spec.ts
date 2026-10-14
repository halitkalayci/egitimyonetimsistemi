import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SystemInfo } from '../../core/api/system-info.models';
import { errorInterceptor } from '../../core/http/error.interceptor';
import { SystemInfoPage } from './system-info.page';

const systemInfo = (entries: SystemInfo['entries']): SystemInfo => ({
  applicationName: 'Eğitim Yönetim Sistemi',
  applicationTimeUtc: '2026-10-14T09:00:00+00:00',
  databaseTimeUtc: '2026-10-14T09:00:01+00:00',
  entries,
});

describe('SystemInfoPage', () => {
  let fixture: ComponentFixture<SystemInfoPage>;
  let http: HttpTestingController;

  const element = (testId: string): HTMLElement | null =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SystemInfoPage],
      providers: [
        provideHttpClient(withInterceptors([errorInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SystemInfoPage);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('should show the loading state while the request is in flight, then the value', async () => {
    fixture.detectChanges();

    expect(element('loading')).not.toBeNull();
    expect(element('content')).toBeNull();

    http.expectOne('/api/system-info').flush(
      systemInfo([
        { id: '1', key: 'skeleton.status', value: 'çalışıyor', recordedAtUtc: '2026-10-14T09:00:00+00:00' },
      ]),
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('loading')).toBeNull();
    expect(element('application-name')?.textContent).toContain('Eğitim Yönetim Sistemi');
    expect(element('database-time')?.textContent).toContain('2026-10-14T09:00:01');
    expect(element('entries')?.textContent).toContain('skeleton.status');
  });

  it('should show the empty state, not an error, when the list comes back empty', async () => {
    fixture.detectChanges();
    http.expectOne('/api/system-info').flush(systemInfo([]));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('empty')?.textContent).toContain('Gösterilecek kayıt yok');
    expect(element('error')).toBeNull();
    expect(element('entries')).toBeNull();
  });

  it('should show a Turkish message and a retry button when the API fails, without leaking internals', async () => {
    fixture.detectChanges();
    http.expectOne('/api/system-info').flush(
      {
        type: 'https://egitimyonetimsistemi/errors/database.unavailable',
        title: 'Servis şu anda kullanılamıyor',
        detail: 'Veritabanına şu anda ulaşılamıyor. Lütfen birkaç saniye sonra tekrar deneyin.',
        status: 503,
        code: 'database.unavailable',
        traceId: '0HN7ABC',
      },
      { status: 503, statusText: 'Service Unavailable' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('error-message')?.textContent).toContain('Veritabanına şu anda ulaşılamıyor');

    const rendered = (fixture.nativeElement as HTMLElement).textContent ?? '';
    for (const forbidden of ['503', 'traceId', 'Npgsql', 'database.unavailable', 'at ']) {
      expect(rendered).not.toContain(forbidden);
    }

    expect(element('retry')).not.toBeNull();
  });

  it('should retry the request when the retry button is activated', async () => {
    fixture.detectChanges();
    http.expectOne('/api/system-info').flush({ detail: 'Hata' }, { status: 503, statusText: 'x' });
    await fixture.whenStable();
    fixture.detectChanges();

    element('retry')?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('loading')).not.toBeNull();
    http.expectOne('/api/system-info').flush(systemInfo([]));
    await fixture.whenStable();
  });

  it('should expose one h1, a polite live region and a keyboard operable retry control', async () => {
    fixture.detectChanges();
    http.expectOne('/api/system-info').flush({ detail: 'Hata' }, { status: 503, statusText: 'x' });
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;

    expect(host.querySelectorAll('h1').length).toBe(1);
    expect(host.querySelector('[aria-live="polite"]')).not.toBeNull();
    expect(host.querySelector('[role="alert"]')).not.toBeNull();

    // A real <button> is keyboard operable by construction: Enter and Space activate it and it is
    // in the tab order without a tabindex. A div with a click handler would not be.
    const retry = element('retry');
    expect(retry?.tagName).toBe('BUTTON');
    expect(retry?.getAttribute('type')).toBe('button');
    expect(retry?.hasAttribute('disabled')).toBe(false);
  });
});
