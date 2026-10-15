import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, defer, finalize, firstValueFrom, from, shareReplay, tap } from 'rxjs';

import { AuthStore } from './auth.store';
import { CreatedUser, ManagedUser, Session, UserRole } from './auth.models';

/** One name for every tab of this application, so their renewals queue behind each other. */
const REFRESH_LOCK = 'egitim-auth-refresh';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly store = inject(AuthStore);
  private inFlightRefresh: Observable<Session> | null = null;

  login(email: string, password: string): Observable<Session> {
    return this.http
      .post<Session>('/api/auth/login', { email, password })
      .pipe(tap((session) => this.store.set(session)));
  }

  /**
   * Called on startup and whenever a request comes back 401: the cookie does the talking.
   *
   * The server treats a refresh secret presented twice as theft and revokes the whole chain, so
   * renewals must never overlap. Within this tab, every caller shares the one request in flight;
   * across tabs, which share the cookie, a Web Lock lets one renewal run at a time - the next tab
   * then sends the cookie the previous renewal already replaced (spec 0002, AC-12).
   */
  refresh(): Observable<Session> {
    this.inFlightRefresh ??= this.exclusively(() =>
      this.http.post<Session>('/api/auth/refresh', {}),
    ).pipe(
      tap((session) => this.store.set(session)),
      finalize(() => (this.inFlightRefresh = null)),
      shareReplay({ bufferSize: 1, refCount: false }),
    );

    return this.inFlightRefresh;
  }

  /** Ends this device's session only; other devices keep theirs (spec 0002, AC-14). */
  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(finalize(() => this.store.clear()));
  }

  /** Ends every session of this user on every device (spec 0002, AC-15). */
  logoutAll(): Observable<void> {
    return this.http
      .post<void>('/api/auth/logout-all', {})
      .pipe(finalize(() => this.store.clear()));
  }

  /** Every other session ends; this device continues on the fresh session the server returns. */
  changePassword(currentPassword: string, newPassword: string): Observable<Session> {
    return this.http
      .post<Session>('/api/auth/change-password', { currentPassword, newPassword })
      .pipe(tap((session) => this.store.set(session)));
  }

  private exclusively(request: () => Observable<Session>): Observable<Session> {
    const locks = typeof navigator === 'undefined' ? undefined : navigator.locks;

    if (!locks) {
      return defer(request);
    }

    // lib.dom types the callback's result as the lock's result, so a promise-returning callback
    // reads as Promise<Promise<T>>; at runtime request() resolves with the awaited value.
    return defer(() =>
      from(
        locks.request(REFRESH_LOCK, () => firstValueFrom(request())) as unknown as Promise<Session>,
      ),
    );
  }

  listUsers(): Observable<readonly ManagedUser[]> {
    return this.http.get<readonly ManagedUser[]>('/api/users');
  }

  createUser(
    fullName: string,
    email: string,
    role: UserRole,
    memberFirmId: string | null,
  ): Observable<CreatedUser> {
    return this.http.post<CreatedUser>('/api/users', { fullName, email, role, memberFirmId });
  }

  assignRole(userId: string, role: UserRole): Observable<ManagedUser> {
    return this.http.put<ManagedUser>(`/api/users/${userId}/role`, { role });
  }

  setAccountStatus(userId: string, disabled: boolean): Observable<ManagedUser> {
    return this.http.put<ManagedUser>(`/api/users/${userId}/status`, { disabled });
  }

  /** The new temporary password is in the answer and nowhere else, ever again (spec 0002). */
  resetPassword(userId: string): Observable<CreatedUser> {
    return this.http.post<CreatedUser>(`/api/users/${userId}/reset-password`, {});
  }
}
