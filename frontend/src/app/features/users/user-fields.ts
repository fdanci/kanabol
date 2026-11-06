import { HttpErrorResponse } from '@angular/common/http';
import { PASSWORD_MAX, PASSWORD_MIN } from '../../shared/models/password-policy';
import { problemIn } from '../../shared/models/problem-details';
import { Field } from '../../shared/ui/field';

/** The longest email and name the API takes (User.MaxEmailLength, User.MaxDisplayNameLength). */
export const EMAIL_MAX = 320;
export const NAME_MAX = 200;

/** The shape the API checks: one @ with something on both sides, and no spaces. */
export function emailProblem(value: string): string | null {
  const email = value.trim();
  if (email === '') {
    return $localize`Scrie adresa de email.`;
  }
  const at = email.indexOf('@');
  if (at <= 0 || at === email.length - 1 || at !== email.lastIndexOf('@') || /\s/.test(email)) {
    return $localize`Adresa de email nu e validă.`;
  }
  return null;
}

export function nameProblem(value: string): string | null {
  return value.trim() === '' ? $localize`Scrie numele.` : null;
}

export function roleProblem(value: string): string | null {
  return value === '' ? $localize`Alege un rol.` : null;
}

/** The policy of docs/SPEC.md §4.5. Whether a password is too common only the server knows. */
export function passwordProblem(value: string): string | null {
  if (value.length < PASSWORD_MIN) {
    return $localize`Cel puțin ${PASSWORD_MIN}:min: caractere.`;
  }
  if (value.length > PASSWORD_MAX) {
    return $localize`Cel mult ${PASSWORD_MAX}:max: de caractere.`;
  }
  return null;
}

/**
 * Puts what the API refused under the field it was about. Answers false when something was left
 * that no field explains: a 400 errorInterceptor left to the form, for it to say.
 */
export function refuseFields(error: unknown, fields: Record<string, Field | undefined>): boolean {
  const problem = error instanceof HttpErrorResponse ? problemIn(error) : null;
  if (problem?.code === 'User.EmailTaken' && fields['email']) {
    fields['email'].refused.set($localize`Există deja un utilizator cu această adresă.`);
    return true;
  }
  if (!problem?.errors) {
    // Not about a field: errorInterceptor has shown it.
    return true;
  }

  const messages: Record<string, string> = {
    email: $localize`Adresa de email nu e validă.`,
    displayName: $localize`Numele nu e bun: e prea lung sau are caractere care nu se văd.`,
    roleId: $localize`Rolul nu mai există. Alege altul.`,
    initialPassword: $localize`Parola e prea des folosită. Alege alta.`,
  };
  let explained = true;
  for (const key of Object.keys(problem.errors)) {
    const field = fields[key];
    if (field && key in messages) {
      field.refused.set(messages[key]);
    } else {
      explained = false;
    }
  }
  return explained;
}
