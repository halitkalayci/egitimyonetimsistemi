import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';

import { ApiError } from '../../core/http/api-error';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule],
  templateUrl: './login.page.html',
  styleUrl: './login.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly email = signal('');
  readonly password = signal('');
  readonly busy = signal(false);
  readonly errorMessage = signal('');
  readonly passwordVisible = signal(false);

  togglePasswordVisibility(): void {
    this.passwordVisible.update((visible) => !visible);
  }

  submit(): void {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.errorMessage.set('');

    this.auth.login(this.email(), this.password()).subscribe({
      next: (session) => {
        this.busy.set(false);
        // A temporary password sends the user straight to the password screen; the server would
        // refuse everything else anyway (AC-6).
        void this.router.navigate([session.mustChangePassword ? '/sifre-degistir' : '/']);
      },
      error: (error: ApiError) => {
        this.busy.set(false);
        this.errorMessage.set(error.message);
      },
    });
  }
}
