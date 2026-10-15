import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthStore } from '../../core/auth/auth.store';
import { appInterceptors } from '../../core/http/interceptors';
import { ManagedUser, UserRole } from '../../core/auth/auth.models';
import { UsersPage } from './users.page';

const user = (overrides: Partial<ManagedUser> = {}): ManagedUser => ({
  id: overrides.id ?? '1',
  fullName: overrides.fullName ?? 'Ayşe Yılmaz',
  email: overrides.email ?? 'ayse@firma.com',
  role: overrides.role ?? 'Participant',
  memberFirmId: overrides.memberFirmId ?? null,
  isDisabled: overrides.isDisabled ?? false,
  mustChangePassword: overrides.mustChangePassword ?? false,
});

describe('UsersPage', () => {
  let fixture: ComponentFixture<UsersPage>;
  let http: HttpTestingController;
  let store: AuthStore;

  const element = (testId: string): HTMLElement | null =>
    (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${testId}"]`);

  const signIn = (role: UserRole): void =>
    store.set({
      accessToken: 'token',
      accessTokenExpiresAtUtc: '2026-10-14T09:15:00+00:00',
      role,
      mustChangePassword: false,
    });

  /** A token shaped like the server's: only the subject matters to the page. */
  const signInAs = (role: UserRole, participantId: string): void =>
    store.set({
      accessToken: `header.${btoa(JSON.stringify({ sub: participantId }))}.signature`,
      accessTokenExpiresAtUtc: '2026-10-14T09:15:00+00:00',
      role,
      mustChangePassword: false,
    });

  const render = async (): Promise<void> => {
    fixture = TestBed.createComponent(UsersPage);
    fixture.detectChanges();
    await fixture.whenStable();
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [UsersPage],
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

  it('should show the empty state when no users exist', async () => {
    signIn('Admin');
    await render();
    http.expectOne('/api/users').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('empty')!.textContent).toContain('Henüz kullanıcı yok');
    expect(element('users')).toBeNull();
    expect(element('error')).toBeNull();
  });

  it('should list the users it received', async () => {
    signIn('Admin');
    await render();
    http.expectOne('/api/users').flush([user(), user({ id: '2', email: 'mehmet@firma.com' })]);
    await fixture.whenStable();
    fixture.detectChanges();

    const table = element('users')!.textContent ?? '';
    expect(table).toContain('ayse@firma.com');
    expect(table).toContain('mehmet@firma.com');
    expect(element('empty')).toBeNull();
  });

  it('should show a permission message and make no request when the role may not manage users', async () => {
    signIn('Participant');
    await render();

    expect(element('forbidden')!.textContent).toContain('yetkiniz yok');
    http.expectNone('/api/users');
  });

  it('should offer only the Participant role to a firm representative', async () => {
    signIn('FirmRepresentative');
    await render();
    http.expectOne('/api/users').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const options = Array.from(
      (element('new-role') as HTMLSelectElement).querySelectorAll('option'),
    ).map((option) => option.textContent?.trim());

    expect(options).toEqual(['Participant']);
  });

  it('should show the temporary password once, with a warning, and let it be dismissed', async () => {
    signIn('Admin');
    await render();
    http.expectOne('/api/users').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    (fixture.nativeElement as HTMLElement)
      .querySelector('.new-user form')!
      .dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectOne({ method: 'POST', url: '/api/users' }).flush({
      user: user({ id: '3', email: 'yeni@immib.org' }),
      temporaryPassword: 'Gecici-Sifre-1234',
    });
    http.expectOne('/api/users').flush([user({ id: '3', email: 'yeni@immib.org' })]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('temporary-password-value')!.textContent).toContain('Gecici-Sifre-1234');
    expect(element('temporary-password')!.textContent).toContain('bir daha gösterilmeyecek');

    element('dismiss')!.click();
    fixture.detectChanges();

    expect(element('temporary-password')).toBeNull();
  });

  const listed = async (users: ManagedUser[]): Promise<void> => {
    http.expectOne('/api/users').flush(users);
    await fixture.whenStable();
    fixture.detectChanges();
  };

  const settle = async (): Promise<void> => {
    await fixture.whenStable();
    fixture.detectChanges();
  };

  it('should change a user role from the list and refresh it', async () => {
    signIn('Admin');
    await render();
    await listed([user()]);

    const select = element('row-role') as HTMLSelectElement;
    select.value = 'Instructor';
    element('row-change-role')!.click();

    const request = http.expectOne({ method: 'PUT', url: '/api/users/1/role' });
    expect(request.request.body).toEqual({ role: 'Instructor' });
    request.flush(user({ role: 'Instructor' }));
    await listed([user({ role: 'Instructor' })]);

    expect(element('action-status')!.textContent).toContain('rol güncellendi');
  });

  it('should reset a password from the list and show the new one once', async () => {
    signIn('Admin');
    await render();
    await listed([user()]);

    element('row-reset-password')!.click();
    http
      .expectOne({ method: 'POST', url: '/api/users/1/reset-password' })
      .flush({ user: user(), temporaryPassword: 'Yeni-Gecici-9876' });
    await listed([user({ mustChangePassword: true })]);

    expect(element('temporary-password-value')!.textContent).toContain('Yeni-Gecici-9876');
    expect(element('temporary-password')!.textContent).toContain('bir daha gösterilmeyecek');
  });

  it('should disable an active account and re-open a disabled one', async () => {
    signIn('Admin');
    await render();
    await listed([user()]);

    expect(element('row-toggle-status')!.textContent).toContain('Devre dışı bırak');
    element('row-toggle-status')!.click();
    const disable = http.expectOne({ method: 'PUT', url: '/api/users/1/status' });
    expect(disable.request.body).toEqual({ disabled: true });
    disable.flush(user({ isDisabled: true }));
    await listed([user({ isDisabled: true })]);

    expect(element('row-toggle-status')!.textContent).toContain('Yeniden aç');
    element('row-toggle-status')!.click();
    const enable = http.expectOne({ method: 'PUT', url: '/api/users/1/status' });
    expect(enable.request.body).toEqual({ disabled: false });
    enable.flush(user());
    await listed([user()]);
  });

  it('should show the server message when an action is refused', async () => {
    signIn('FirmRepresentative');
    await render();
    await listed([user()]);

    element('row-reset-password')!.click();
    http.expectOne('/api/users/1/reset-password').flush(
      { status: 403, detail: 'Bu işlem için yetkiniz yok.', code: 'users.not_allowed' },
      { status: 403, statusText: 'x' },
    );
    await settle();

    expect(element('form-error')!.textContent).toContain('yetkiniz yok');
    expect(element('form-error')!.getAttribute('role')).toBe('alert');
  });

  it('should let an Admin place a new firm user in a firm and send no firm for staff', async () => {
    signIn('Admin');
    await render();
    await listed([]);

    const role = element('new-role') as HTMLSelectElement;
    role.value = 'Participant';
    role.dispatchEvent(new Event('change'));
    await settle();
    const firm = element('new-firm') as HTMLInputElement;
    firm.value = ' 0199f0aa-0000-7000-8000-000000000001 ';
    firm.dispatchEvent(new Event('input'));

    (fixture.nativeElement as HTMLElement).querySelector('.new-user form')!.dispatchEvent(new Event('submit'));
    await settle();
    const create = http.expectOne({ method: 'POST', url: '/api/users' });
    expect(create.request.body.memberFirmId).toBe('0199f0aa-0000-7000-8000-000000000001');
    create.flush({ user: user(), temporaryPassword: 'x' });
    await listed([user()]);

    role.value = 'Instructor';
    role.dispatchEvent(new Event('change'));
    await settle();

    // Admin and Instructor belong to no firm, so the field is not even offered.
    expect(element('new-firm')).toBeNull();
  });

  it('should never offer a firm field to a firm representative', async () => {
    signIn('FirmRepresentative');
    await render();
    await listed([]);

    expect(element('new-firm')).toBeNull();
  });

  it('should offer no actions on the signed-in Admin’s own row', async () => {
    signInAs('Admin', 'self');
    await render();
    await listed([user({ id: 'self', role: 'Admin' }), user({ id: 'other', role: 'Admin' })]);

    // The server refuses any change to one's own access (users.own_access).
    const own = element('row-self')!;
    const other = element('row-other')!;
    expect(own.querySelector('[data-testid="row-no-actions"]')).not.toBeNull();
    expect(own.querySelector('button')).toBeNull();
    expect(other.querySelector('[data-testid="row-toggle-status"]')).not.toBeNull();
  });

  it('should offer a firm representative actions on Participant rows only', async () => {
    signInAs('FirmRepresentative', 'rep');
    await render();
    await listed([
      user({ id: 'rep', role: 'FirmRepresentative' }),
      user({ id: 'colleague', role: 'FirmRepresentative' }),
      user({ id: 'participant', role: 'Participant' }),
    ]);

    // A representative manages Participant accounts of their own firm and nothing else (H-2).
    const participantRow = element('row-participant')!;
    expect(element('row-rep')!.querySelector('button')).toBeNull();
    expect(element('row-colleague')!.querySelector('button')).toBeNull();
    expect(participantRow.querySelector('[data-testid="row-reset-password"]')).not.toBeNull();
  });

  it('should name the firm field and send nothing when the firm identifier is malformed', async () => {
    signIn('Admin');
    await render();
    await listed([]);

    const firm = element('new-firm') as HTMLInputElement;
    firm.value = 'firma-1';
    firm.dispatchEvent(new Event('input'));
    (fixture.nativeElement as HTMLElement).querySelector('.new-user form')!.dispatchEvent(new Event('submit'));
    await settle();

    http.expectNone({ method: 'POST', url: '/api/users' });
    expect(element('new-firm-error')!.textContent).toContain('Üye firma kimliği');
    expect(firm.getAttribute('aria-invalid')).toBe('true');
    expect(firm.getAttribute('aria-describedby')).toContain('newFirmError');

    firm.value = '0199f0aa-0000-7000-8000-000000000001';
    firm.dispatchEvent(new Event('input'));
    await settle();

    // Correcting the value clears the message straight away.
    expect(element('new-firm-error')).toBeNull();
  });

  it('should show the error state with a retry that re-requests the list', async () => {
    signIn('Admin');
    await render();
    http.expectOne('/api/users').flush(
      { code: 'database.unavailable', detail: 'Liste alınamadı.', status: 503 },
      { status: 503, statusText: 'x' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('error')!.textContent).toContain('Liste alınamadı');

    element('retry')!.click();
    await fixture.whenStable();
    http.expectOne('/api/users').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element('empty')).not.toBeNull();
  });
});
