import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthStore } from './auth.store';

/** No session, no page. */
export const authGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  const router = inject(Router);

  return store.isAuthenticated() ? true : router.createUrlTree(['/giris']);
};

/**
 * While the account carries a temporary password the only reachable page is the password screen
 * (spec 0002, AC-23). The server enforces the same rule, so this guard is convenience, not security.
 */
export const mustChangePasswordGuard: CanActivateFn = () => {
  const store = inject(AuthStore);
  const router = inject(Router);

  return store.mustChangePassword() ? router.createUrlTree(['/sifre-degistir']) : true;
};
