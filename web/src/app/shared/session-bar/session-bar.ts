import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';

import { AuthService } from '../../core/auth/auth.service';
import { AuthStore } from '../../core/auth/auth.store';

/**
 * Shown on every page while signed in. "Çıkış" ends this device's session; "Tüm oturumları kapat"
 * ends every device's (spec 0002). Either way the local session is dropped even if the server
 * cannot be reached, so the button always does what it says on this device.
 */
@Component({
  selector: 'app-session-bar',
  templateUrl: './session-bar.html',
  styleUrl: './session-bar.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SessionBar {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly store = inject(AuthStore);
  readonly busy = signal(false);

  logout(): void {
    this.end(this.auth.logout());
  }

  logoutAll(): void {
    this.end(this.auth.logoutAll());
  }

  private end(request: Observable<void>): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    const leave = (): void => {
      this.busy.set(false);
      void this.router.navigate(['/giris']);
    };

    request.subscribe({ next: leave, error: leave });
  }
}
