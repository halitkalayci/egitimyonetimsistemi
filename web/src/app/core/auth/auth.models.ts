export type UserRole = 'Admin' | 'Instructor' | 'FirmRepresentative' | 'Participant';

/** What /api/auth/login and /api/auth/refresh return. The refresh secret is NOT here: it lives in
 *  an HttpOnly cookie the browser sends on its own. */
export interface Session {
  readonly accessToken: string;
  readonly accessTokenExpiresAtUtc: string;
  readonly role: UserRole;
  readonly mustChangePassword: boolean;
}

export interface ManagedUser {
  readonly id: string;
  readonly fullName: string;
  readonly email: string;
  readonly role: UserRole;
  readonly memberFirmId: string | null;
  readonly isDisabled: boolean;
  readonly mustChangePassword: boolean;
}

export interface CreatedUser {
  readonly user: ManagedUser;
  readonly temporaryPassword: string;
}
