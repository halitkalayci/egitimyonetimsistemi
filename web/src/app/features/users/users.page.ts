import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';

import { ApiError } from '../../core/http/api-error';
import { AuthService } from '../../core/auth/auth.service';
import { AuthStore } from '../../core/auth/auth.store';
import { CreatedUser, ManagedUser, UserRole } from '../../core/auth/auth.models';

type PageState = 'loading' | 'ready' | 'error' | 'forbidden';

/** Roles that may belong to a member firm; Admin and Instructor never do (spec 0002, H-2). */
const FIRM_ROLES: readonly UserRole[] = ['FirmRepresentative', 'Participant'];

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

@Component({
  selector: 'app-users-page',
  imports: [FormsModule],
  templateUrl: './users.page.html',
  styleUrl: './users.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  private readonly auth = inject(AuthService);
  private readonly store = inject(AuthStore);

  readonly state = signal<PageState>('loading');
  readonly users = signal<readonly ManagedUser[]>([]);
  readonly errorMessage = signal('');
  readonly statusMessage = signal('');
  readonly busy = signal(false);

  readonly fullName = signal('');
  readonly email = signal('');
  readonly role = signal<UserRole>('Participant');
  readonly memberFirmId = signal('');
  readonly memberFirmError = signal('');

  /** A temporary password from creating an account or resetting one; shown exactly once. */
  readonly created = signal<CreatedUser | null>(null);

  readonly isAdmin = this.store.role() === 'Admin';

  /** A firm representative may only ever assign Participant (spec 0002, AC-32). */
  readonly assignableRoles = signal<readonly UserRole[]>(
    this.isAdmin ? ['Admin', 'Instructor', 'FirmRepresentative', 'Participant'] : ['Participant'],
  );

  /**
   * Only an Admin chooses the firm; a representative's own firm is applied by the server. Until
   * member firms get their own screens (spec 0004) the firm is entered by its identifier.
   */
  readonly showFirmField = computed(() => this.isAdmin && FIRM_ROLES.includes(this.role()));

  constructor() {
    if (!this.store.canManageUsers()) {
      this.state.set('forbidden');
      return;
    }

    this.load();
  }

  load(): void {
    this.state.set('loading');
    this.errorMessage.set('');

    this.auth.listUsers().subscribe({
      next: (users) => {
        this.users.set(users);
        this.state.set('ready');
      },
      error: (error: ApiError) => {
        this.errorMessage.set(error.message);
        this.state.set('error');
      },
    });
  }

  isEmpty(): boolean {
    return this.state() === 'ready' && this.users().length === 0;
  }

  /**
   * The server's targeting rule, mirrored so the list offers only what will be accepted: nobody
   * acts on their own row, and a representative only on Participant accounts (spec 0002, H-2).
   */
  canManage(user: ManagedUser): boolean {
    return user.id !== this.store.participantId() && (this.isAdmin || user.role === 'Participant');
  }

  setMemberFirmId(value: string): void {
    this.memberFirmId.set(value);
    this.memberFirmError.set('');
  }

  create(): void {
    const firm = this.showFirmField() ? this.memberFirmId().trim() : '';

    if (firm !== '' && !UUID.test(firm)) {
      this.memberFirmError.set(
        'Üye firma kimliği geçerli bir kimlik değil (örnek: 0199f0aa-0000-7000-8000-000000000001).',
      );
      return;
    }

    this.run(
      this.auth.createUser(this.fullName(), this.email(), this.role(), firm === '' ? null : firm),
      (created) => {
        // Shown exactly once: the server never returns it again (AC-18).
        this.created.set(created);
        this.fullName.set('');
        this.email.set('');
        this.memberFirmId.set('');
      },
    );
  }

  changeRole(user: ManagedUser, role: string): void {
    this.run(this.auth.assignRole(user.id, role as UserRole), () =>
      this.statusMessage.set(`${user.fullName} için rol güncellendi.`),
    );
  }

  toggleStatus(user: ManagedUser): void {
    const disable = !user.isDisabled;

    this.run(this.auth.setAccountStatus(user.id, disable), () =>
      this.statusMessage.set(
        disable ? `${user.fullName} devre dışı bırakıldı.` : `${user.fullName} yeniden açıldı.`,
      ),
    );
  }

  resetPassword(user: ManagedUser): void {
    this.run(this.auth.resetPassword(user.id), (issued) => this.created.set(issued));
  }

  dismissTemporaryPassword(): void {
    this.created.set(null);
  }

  /** One action at a time; every success refreshes the list so it shows the server's truth. */
  private run<T>(action: Observable<T>, onSuccess: (value: T) => void): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');
    this.statusMessage.set('');
    this.created.set(null);

    action.subscribe({
      next: (value) => {
        this.busy.set(false);
        onSuccess(value);
        this.load();
      },
      error: (error: ApiError) => {
        this.busy.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }
}
