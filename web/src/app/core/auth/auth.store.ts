import { Injectable, computed, signal } from '@angular/core';

import { Session, UserRole } from './auth.models';

/**
 * The access token lives here and nowhere else: not in localStorage, not in sessionStorage
 * (docs/security.md - both are readable by an XSS payload). A page reload therefore starts with no
 * token, and the session is re-established from the refresh cookie instead.
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly session = signal<Session | null>(null);

  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly role = computed<UserRole | null>(() => this.session()?.role ?? null);
  /**
   * Who is signed in, read from the token's subject. Only for mirroring server rules in the UI
   * (e.g. no actions on one's own row); the server never trusts this value from the client.
   */
  readonly participantId = computed(() => subjectOf(this.session()?.accessToken));
  readonly mustChangePassword = computed(() => this.session()?.mustChangePassword ?? false);
  readonly canManageUsers = computed(() => {
    const role = this.role();
    return role === 'Admin' || role === 'FirmRepresentative';
  });

  accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  set(session: Session): void {
    this.session.set(session);
  }

  clear(): void {
    this.session.set(null);
  }
}

/** The `sub` claim of a JWT, or null when the token is not one we can read. */
function subjectOf(token: string | undefined): string | null {
  const payload = token?.split('.')[1];

  if (!payload) {
    return null;
  }

  try {
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    const sub: unknown = (JSON.parse(json) as { sub?: unknown }).sub;

    return typeof sub === 'string' ? sub : null;
  } catch {
    return null;
  }
}
