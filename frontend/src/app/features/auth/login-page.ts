import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthStore } from '../../core/auth/auth-store';
import { CHANGE_PASSWORD_URL } from '../../core/auth/auth-guard';
import { Lockup } from '../../shared/ui/lockup';

const required = (control: AbstractControl) => Validators.required(control);

/** Only an address inside the app: "//elsewhere.example" would leave it. */
export function safeReturnUrl(value: string | null): string {
  return value?.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\')
    ? value
    : '/';
}

/**
 * The login page (docs/UI_UX_SPEC.md §7.1): no shell, a 400px card. A refused login says only that
 * the email or the password is wrong, never which, nor that the account is locked (§4.5).
 */
@Component({
  selector: 'kb-login-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, Lockup],
  templateUrl: './login-page.html',
  styleUrl: './auth-card.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [required]],
    password: ['', [required]],
  });
  protected readonly passwordShown = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly submitted = signal(false);

  protected async signIn(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid || this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      const { email, password } = this.form.getRawValue();
      await this.auth.login(email.trim(), password);
      const next = this.auth.mustChangePassword()
        ? CHANGE_PASSWORD_URL
        : safeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
      await this.router.navigateByUrl(next);
    } catch (error) {
      this.error.set(this.messageFor(error));
      this.form.controls.password.reset();
    } finally {
      this.busy.set(false);
    }
  }

  private messageFor(error: unknown): string {
    const status = error instanceof HttpErrorResponse ? error.status : -1;
    if (status === 429) {
      return $localize`Prea multe încercări. Așteaptă un minut și reîncearcă.`;
    }
    if (status === 0 || status >= 500) {
      return $localize`Serverul nu răspunde. Verifică conexiunea și reîncearcă.`;
    }
    return $localize`Email sau parolă incorecte.`;
  }
}
