import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { AuthStore } from '../../core/auth/auth-store';
import { ChangePasswordPage, passwordRules } from './change-password-page';
import { LoginPage, safeReturnUrl } from './login-page';

class FakeAuth {
  readonly mustChange = signal(false);
  mustChangePassword = () => this.mustChange();
  isSignedIn = () => false;
  login = vi.fn(() => Promise.resolve());
  changePassword = vi.fn(() => Promise.resolve());
  logout = vi.fn(() => Promise.resolve());
}

function refused(status: number, body: object | null = null): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: body, url: '/api/auth/x' });
}

function type(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

describe('auth pages', () => {
  let auth: FakeAuth;

  beforeEach(() => {
    auth = new FakeAuth();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'login', component: LoginPage },
          { path: 'change-password', component: ChangePasswordPage },
          { path: '**', children: [] },
        ]),
        { provide: AuthStore, useValue: auth },
      ],
    });
  });

  async function page(url: string) {
    const harness = await RouterTestingHarness.create(url);
    const element = harness.routeNativeElement!;
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
    return { harness, element, navigate };
  }

  function field(element: HTMLElement, autocomplete: string): HTMLInputElement {
    return element.querySelector<HTMLInputElement>(`input[autocomplete="${autocomplete}"]`)!;
  }

  async function submit(harness: RouterTestingHarness, element: HTMLElement): Promise<void> {
    element.querySelector<HTMLButtonElement>('button[type=submit]')!.click();
    for (let i = 0; i < 5; i++) {
      await Promise.resolve();
    }
    await harness.fixture.whenStable();
  }

  describe('LoginPage', () => {
    it('has the lockup, the two fields and the button, and nothing to remember the user', async () => {
      const { element } = await page('/login');

      expect(element.querySelector('kb-lockup')).not.toBeNull();
      expect(field(element, 'username').type).toBe('email');
      expect(field(element, 'current-password').type).toBe('password');
      expect(element.querySelector('button[type=submit]')?.textContent.trim()).toBe(
        'Intră în cont',
      );
      expect(element.querySelector('input[type=checkbox]')).toBeNull();
    });

    it('shows the password when asked', async () => {
      const { harness, element } = await page('/login');
      const show = element.querySelector<HTMLButtonElement>('button.show')!;

      show.click();
      await harness.fixture.whenStable();

      expect(field(element, 'current-password').type).toBe('text');
      expect(show.getAttribute('aria-pressed')).toBe('true');
      expect(show.textContent.trim()).toBe('ascunde');
    });

    it('asks for both fields before sending anything', async () => {
      const { harness, element } = await page('/login');

      await submit(harness, element);

      expect(auth.login).not.toHaveBeenCalled();
      expect(element.textContent).toContain('Scrie adresa de email.');
      expect(element.textContent).toContain('Scrie parola.');
    });

    it('signs in and goes back where the user was heading', async () => {
      const { harness, element, navigate } = await page('/login?returnUrl=%2Fprojects%2F7');
      type(field(element, 'username'), ' ana@example.com ');
      type(field(element, 'current-password'), 'o parola buna');

      await submit(harness, element);

      expect(auth.login).toHaveBeenCalledWith('ana@example.com', 'o parola buna');
      expect(navigate).toHaveBeenCalledWith('/projects/7');
    });

    it('goes to the password change first when one is due', async () => {
      auth.login.mockImplementation(() => {
        auth.mustChange.set(true);
        return Promise.resolve();
      });
      const { harness, element, navigate } = await page('/login?returnUrl=%2Fprojects');
      type(field(element, 'username'), 'ana@example.com');
      type(field(element, 'current-password'), 'parola initiala');

      await submit(harness, element);

      expect(navigate).toHaveBeenCalledWith('/change-password');
    });

    // docs/UI_UX_SPEC.md §7.1: not which one, and not that the account is locked.
    it('says only that the email or the password is wrong, and clears the password', async () => {
      auth.login.mockRejectedValue(refused(401, { code: 'Auth.InvalidCredentials' }));
      const { harness, element } = await page('/login');
      type(field(element, 'username'), 'ana@example.com');
      type(field(element, 'current-password'), 'gresita');

      await submit(harness, element);

      expect(element.querySelector('[role=alert]')?.textContent.trim()).toBe(
        'Email sau parolă incorecte.',
      );
      expect(field(element, 'current-password').value).toBe('');
      expect(field(element, 'username').value).toBe('ana@example.com');
    });

    it('says when there were too many tries, and when the server is away', async () => {
      const { harness, element } = await page('/login');
      type(field(element, 'username'), 'ana@example.com');

      auth.login.mockRejectedValueOnce(refused(429));
      type(field(element, 'current-password'), 'x');
      await submit(harness, element);
      expect(element.querySelector('[role=alert]')?.textContent).toContain('Prea multe încercări');

      auth.login.mockRejectedValueOnce(refused(0));
      type(field(element, 'current-password'), 'x');
      await submit(harness, element);
      expect(element.querySelector('[role=alert]')?.textContent).toContain('Serverul nu răspunde');
    });

    it('never sends the user to another site after the login', () => {
      expect(safeReturnUrl('/projects/7?tab=board')).toBe('/projects/7?tab=board');
      expect(safeReturnUrl('//evil.example')).toBe('/');
      expect(safeReturnUrl('/\\evil.example')).toBe('/');
      expect(safeReturnUrl('https://evil.example')).toBe('/');
      expect(safeReturnUrl(null)).toBe('/');
    });
  });

  describe('ChangePasswordPage', () => {
    it('ticks the rules as they are met', () => {
      expect(passwordRules('veche', '', '').map((rule) => rule.met)).toEqual([false, false, false]);
      expect(passwordRules('veche', 'scurta', 'scurta').map((rule) => rule.met)).toEqual([
        false,
        true,
        true,
      ]);
      expect(
        passwordRules('o parola buna', 'o parola buna', 'o parola buna').map((rule) => rule.met),
      ).toEqual([true, false, true]);
      expect(
        passwordRules('veche', 'o parola noua', 'o parola nou').map((rule) => rule.met),
      ).toEqual([true, true, false]);
      expect(passwordRules('veche', 'x'.repeat(257), 'x'.repeat(257))[0].met).toBe(false);
    });

    it('lists the rules on the page and ticks them live', async () => {
      const { harness, element } = await page('/change-password');

      type(field(element, 'current-password'), 'parola initiala');
      type(
        element.querySelectorAll<HTMLInputElement>('input[autocomplete=new-password]')[0],
        'o parola noua buna',
      );
      await harness.fixture.whenStable();

      const rules = Array.from(element.querySelectorAll('.rules li'), (rule) => ({
        label: rule.querySelector('span:not([aria-hidden], .state)')?.textContent.trim(),
        state: rule.querySelector('.state')?.textContent.replace(/s+/g, ' ').trim(),
        ticked: rule.classList.contains('met'),
      }));
      expect(rules).toEqual([
        { label: 'Cel puțin 10 caractere', state: ', îndeplinită', ticked: true },
        { label: 'Diferită de parola curentă', state: ', îndeplinită', ticked: true },
        { label: 'Scrisă la fel de două ori', state: ', neîndeplinită', ticked: false },
      ]);
    });

    it('changes the password and opens the app', async () => {
      const { harness, element, navigate } = await page('/change-password');
      const [next, repeated] = element.querySelectorAll<HTMLInputElement>(
        'input[autocomplete=new-password]',
      );
      type(field(element, 'current-password'), 'parola initiala');
      type(next, 'o parola noua buna');
      type(repeated, 'o parola noua buna');

      await submit(harness, element);

      expect(auth.changePassword).toHaveBeenCalledWith('parola initiala', 'o parola noua buna');
      expect(navigate).toHaveBeenCalledWith('/');
    });

    it('sends nothing until the rules are met', async () => {
      const { harness, element } = await page('/change-password');
      type(field(element, 'current-password'), 'parola initiala');
      type(
        element.querySelectorAll<HTMLInputElement>('input[autocomplete=new-password]')[0],
        'scurta',
      );

      await submit(harness, element);

      expect(auth.changePassword).not.toHaveBeenCalled();
      expect(element.querySelector('[role=alert]')).not.toBeNull();
    });

    it('puts the server refusals on their fields', async () => {
      const { harness, element } = await page('/change-password');
      const [next, repeated] = element.querySelectorAll<HTMLInputElement>(
        'input[autocomplete=new-password]',
      );
      type(field(element, 'current-password'), 'gresita');
      type(next, 'o parola noua buna');
      type(repeated, 'o parola noua buna');

      auth.changePassword.mockRejectedValueOnce(
        refused(400, { errors: { currentPassword: ['x'] } }),
      );
      await submit(harness, element);
      expect(element.querySelector('#current-error')?.textContent).toBe(
        'Parola curentă e greșită.',
      );
      expect(field(element, 'current-password').getAttribute('aria-invalid')).toBe('true');

      auth.changePassword.mockRejectedValueOnce(refused(400, { errors: { newPassword: ['x'] } }));
      await submit(harness, element);
      expect(element.querySelector('#next-error')?.textContent).toBe(
        'Parola e prea des folosită. Alege alta.',
      );
      expect(element.querySelector('#current-error')).toBeNull();
    });

    it('lets the user leave instead', async () => {
      const { harness, element, navigate } = await page('/change-password');

      element.querySelector<HTMLButtonElement>('button.leave')!.click();
      await Promise.resolve();
      await harness.fixture.whenStable();

      expect(auth.logout).toHaveBeenCalled();
      expect(navigate).toHaveBeenCalledWith('/login');
    });
  });
});
