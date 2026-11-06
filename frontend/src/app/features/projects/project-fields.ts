import { HttpErrorResponse } from '@angular/common/http';
import { problemIn } from '../../shared/models/problem-details';
import { Field } from '../../shared/ui/field';

/** What the API takes (Project.MaxNameLength, MaxKeyLength, MaxDescriptionLength). */
export const PROJECT_NAME_MAX = 200;
export const PROJECT_KEY_MAX = 5;
export const PROJECT_DESCRIPTION_MAX = 2000;

const KEY = /^[A-Z]{2,5}$/;

export function projectNameProblem(value: string): string | null {
  return value.trim() === '' ? $localize`Scrie numele proiectului.` : null;
}

export function projectKeyProblem(value: string): string | null {
  return KEY.test(value.trim()) ? null : $localize`Cheia are 2–5 litere, de la A la Z.`;
}

export function projectDescriptionProblem(value: string): string | null {
  return value.length > PROJECT_DESCRIPTION_MAX
    ? $localize`Cel mult ${PROJECT_DESCRIPTION_MAX}:max: de caractere.`
    : null;
}

/** A key typed in any case is written in capitals, and only with the letters a key can have. */
export function asKey(value: string): string {
  return value
    .toUpperCase()
    .replace(/[^A-Z]/g, '')
    .slice(0, PROJECT_KEY_MAX);
}

/**
 * A key to start from, while the person hasn't typed one: the initials of a name of several words,
 * or the first three letters of one word, without diacritics. "Kanabol" is KAN, "Site nou" is SN.
 */
export function suggestKey(name: string): string {
  const words = name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toUpperCase()
    .split(/[^A-Z]+/)
    .filter((word) => word.length > 0);
  if (words.length === 0) {
    return '';
  }
  const key = words.length === 1 ? words[0].slice(0, 3) : words.map((word) => word[0]).join('');
  return key.slice(0, PROJECT_KEY_MAX);
}

/**
 * Puts what the API refused under the field it was about. Answers false when something was left
 * that no field explains.
 */
export function refuseProjectFields(
  error: unknown,
  fields: Record<string, Field | undefined>,
): boolean {
  const problem = error instanceof HttpErrorResponse ? problemIn(error) : null;
  if (problem?.code === 'Project.KeyTaken' && fields['key']) {
    fields['key'].refused.set($localize`Există deja un proiect cu această cheie, poate arhivat.`);
    return true;
  }
  if (!problem?.errors) {
    // Not about a field: errorInterceptor has shown it.
    return true;
  }

  const messages: Record<string, string> = {
    name: $localize`Numele nu e bun: e prea lung sau are caractere care nu se văd.`,
    key: $localize`Cheia are 2–5 litere, de la A la Z.`,
    description: $localize`Descrierea e prea lungă sau are caractere care nu se văd.`,
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
