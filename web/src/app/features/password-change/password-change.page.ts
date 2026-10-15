import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import { ApiError } from '../../core/http/api-error';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-password-change-page',
  imports: [FormsModule],
  templateUrl: './password-change.page.html',
  styleUrl: './password-change.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PasswordChangePage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly currentPassword = signal('');
  readonly newPassword = signal('');
  readonly busy = signal(false);
  readonly errorMessage = signal('');
  readonly done = signal(false);

  submit(): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    this.auth.changePassword(this.currentPassword(), this.newPassword()).subscribe({
      next: () => {
        this.busy.set(false);
        this.done.set(true);
        // A fresh session came back with the change: straight on to the welcome page.
        void this.router.navigate(['/']);
      },
      error: (error: ApiError) => {
        this.busy.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }
}
