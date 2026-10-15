import { inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { AuthService } from './auth.service';

/**
 * Runs before the first navigation. The access token lives only in memory, so a reload starts
 * without one; the refresh cookie - which JavaScript cannot read - is offered to the server
 * instead, and a valid one brings the session back without a new login (spec 0002: "sayfa
 * yenilendiğinde oturum korunur"). No cookie or a refused one simply means starting signed out,
 * which the guards then handle; it is never an error the user sees.
 */
export function restoreSession(): Promise<void> {
  const auth = inject(AuthService);

  return firstValueFrom(auth.refresh()).then(
    () => undefined,
    () => undefined,
  );
}
