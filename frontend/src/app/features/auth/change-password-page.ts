import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButton } from '@angular/material/button';
import { Router } from '@angular/router';
import { AuthStore } from '../../core/auth/auth-store';
import { problemIn } from '../../shared/models/problem-details';
import { Lockup } from '../../shared/ui/lockup';

/** The password policy of docs/SPEC.md §4.5. Whether it's too common only the server knows. */
export const PASSWORD_MIN = 10;
export const PASSWORD_MAX = 256;

export interface PasswordRule {
  label: string;
  met: boolean;
}

export function passwordRules(current: string, next: string, repeated: string): PasswordRule[] {
  return [
    {
      label: $localize`Cel puțin ${PASSWORD_MIN}:min: caractere`,
      met: next.length >= PASSWORD_MIN && next.length <= PASSWORD_MAX,
    },
    { label: $localize`Diferită de parola curentă`, met: next.length > 0 && next !== current },
    { label: $localize`Scrisă la fel de două ori`, met: next.length > 0 && next === repeated },
  ];
}

/**
 * The forced password change (docs/UI_UX_SPEC.md §7.1): the login card, with the rules ticked as the
 * user types. Until it's done the API opens nothing else (docs/SPEC.md §4.5).
 */
@Component({
  selector: 'kb-change-password-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, MatButton, Lockup],
  templateUrl: './change-password-page.html',
  styleUrl: './auth-card.scss',
})
export class ChangePasswordPage {
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({
    current: [''],
    next: [''],
    repeated: [''],
  });
  private readonly values = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });
  protected readonly rules = computed(() => {
    const { current = '', next = '', repeated = '' } = this.values();
    return passwordRules(current, next, repeated);
  });
  protected readonly busy = signal(false);
  protected readonly currentError = signal<string | null>(null);
  protected readonly nextError = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected async change(): Promise<void> {
    if (
      this.busy() ||
      this.rules().some((rule) => !rule.met) ||
      !this.form.controls.current.value
    ) {
      this.error.set($localize`Completează câmpurile după regulile de mai jos.`);
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    this.currentError.set(null);
    this.nextError.set(null);
    try {
      const { current, next } = this.form.getRawValue();
      await this.auth.changePassword(current, next);
      await this.router.navigateByUrl('/');
    } catch (error) {
      this.explain(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected async leave(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }

  private explain(error: unknown): void {
    const problem = error instanceof HttpErrorResponse ? problemIn(error) : null;
    if (problem?.errors?.['currentPassword']) {
      this.currentError.set($localize`Parola curentă e greșită.`);
    } else if (problem?.errors?.['newPassword']) {
      // The rules above were met, so what's left is the server's list of common passwords.
      this.nextError.set($localize`Parola e prea des folosită. Alege alta.`);
    } else if (!problem?.errors) {
      // errorInterceptor has shown what went wrong already.
      this.error.set($localize`Parola nu a fost schimbată.`);
    }
  }
}
