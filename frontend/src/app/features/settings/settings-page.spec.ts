import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';
import { Snackbar } from '../../shared/ui/snackbar';
import { SettingsApi, WorkScheduleChange, WorkScheduleDto } from './settings-api';
import { SettingsPage } from './settings-page';

const WEEKDAYS: WorkScheduleDto = {
  days: [1, 2, 3, 4, 5],
  start: '08:30',
  end: '16:30',
  timeZone: 'Europe/Bucharest',
};

/** docs/UI_UX_SPEC.md §7.9 (group 22): the page "Setări generale", its card "Program de lucru". */
describe('SettingsPage', () => {
  let api: { workSchedule: ReturnType<typeof vi.fn>; saveWorkSchedule: ReturnType<typeof vi.fn> };
  let snackbar: { show: ReturnType<typeof vi.fn>; error: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<SettingsPage>;

  beforeEach(() => {
    api = {
      workSchedule: vi.fn((): Observable<WorkScheduleDto> => of(WEEKDAYS)),
      saveWorkSchedule: vi.fn((change: WorkScheduleChange) =>
        of({ ...change, timeZone: 'Europe/Bucharest' }),
      ),
    };
    snackbar = { show: vi.fn(), error: vi.fn() };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: SettingsApi, useValue: api },
        { provide: Snackbar, useValue: snackbar },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  async function shown(): Promise<HTMLElement> {
    fixture = TestBed.createComponent(SettingsPage);
    await settle();
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    for (let round = 0; round < 3; round++) {
      await Promise.resolve();
      fixture.detectChanges();
      await fixture.whenStable();
    }
  }

  function page(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function chips(): HTMLButtonElement[] {
    return [...page().querySelectorAll<HTMLButtonElement>('.day')];
  }

  function save(): HTMLButtonElement {
    return [...page().querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent.trim() === 'Salvează',
    )!;
  }

  async function type(id: string, value: string): Promise<void> {
    const input = page().querySelector<HTMLInputElement>(`#${id}`)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle();
  }

  it('shows the card with its days, its hours, the time zone to read and Salvează off', async () => {
    await shown();

    expect(page().querySelector('h1')?.textContent.trim()).toBe('Setări generale');
    expect(page().querySelector('h2')?.textContent.trim()).toBe('Program de lucru');
    expect(page().querySelector('.lead')?.textContent.replace(/\s+/g, ' ').trim()).toBe(
      'Timpul lucrat pe task-uri se numără doar în acest program. O schimbare se aplică de acum înainte; task-urile aflate în lucru se socotesc cu programul nou.',
    );
    // docs/UI_UX_SPEC.md §7.9 (group 23): the full name as hidden text, the short one aria-hidden, no aria-label,
    // so the name read holds the text seen.
    expect(
      chips().map(
        (chip) =>
          `${chip.querySelector('[aria-hidden="true"]')?.textContent.trim() ?? ''} ${chip.querySelector('.kb-visually-hidden')?.textContent.trim() ?? ''} ${chip.getAttribute('aria-pressed') ?? ''}`,
      ),
    ).toEqual([
      'L Luni true',
      'Ma Marți true',
      'Mi Miercuri true',
      'J Joi true',
      'V Vineri true',
      'S Sâmbătă false',
      'D Duminică false',
    ]);
    expect(chips().filter((chip) => chip.hasAttribute('aria-label'))).toEqual([]);
    expect(page().querySelector<HTMLInputElement>('#kb-work-start')?.value).toBe('08:30');
    expect(page().querySelector<HTMLInputElement>('#kb-work-end')?.value).toBe('16:30');
    expect(page().querySelector<HTMLInputElement>('#kb-work-end')?.type).toBe('time');
    const zone = page().querySelector('.zone')!;
    expect(zone.textContent.trim()).toBe('Europe/Bucharest');
    expect(zone.closest('.kb-form-row')?.querySelector('.kb-form-label')?.textContent.trim()).toBe(
      'Fus orar',
    );
    expect(page().querySelector('.zone + .kb-form-help')?.textContent.trim()).toBe(
      'Se schimbă din configurația serverului.',
    );
    expect(save().disabled).toBe(true);
  });

  it('turns Salvează on once something changed, and off again when it goes back', async () => {
    await shown();

    chips()[5].click();
    await settle();
    expect(chips()[5].getAttribute('aria-pressed')).toBe('true');
    expect(save().disabled).toBe(false);

    chips()[5].click();
    await settle();
    expect(save().disabled).toBe(true);
  });

  it('saves the days chosen and the hours, and says so', async () => {
    await shown();

    chips()[5].click();
    await type('kb-work-end', '17:00');
    save().click();
    await settle();

    expect(api.saveWorkSchedule).toHaveBeenCalledWith({
      days: [1, 2, 3, 4, 5, 6],
      start: '08:30',
      end: '17:00',
    });
    expect(snackbar.show).toHaveBeenCalledWith('Programul de lucru a fost salvat.');
    expect(save().disabled).toBe(true);
  });

  it('says what is wrong, and saves nothing', async () => {
    await shown();

    for (const chip of chips().slice(0, 5)) {
      chip.click();
    }
    await type('kb-work-start', '17:00');
    save().click();
    await settle();

    expect(
      [...page().querySelectorAll('.kb-field-error')].map((error) => error.textContent.trim()),
    ).toEqual([
      'Alege cel puțin o zi.',
      'Ora de început trebuie să fie înaintea celei de sfârșit.',
    ]);
    expect(api.saveWorkSchedule).not.toHaveBeenCalled();
  });

  // docs/UI_UX_SPEC.md §7.9: 409 Settings.ChangedMeanwhile reads the page again; the interceptor says why.
  it('reads the schedule again when someone else changed it meanwhile', async () => {
    await shown();
    api.saveWorkSchedule.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 409, error: { code: 'Settings.ChangedMeanwhile' } }),
      ),
    );
    api.workSchedule.mockReturnValue(of({ ...WEEKDAYS, start: '09:00' }));

    chips()[6].click();
    await settle();
    save().click();
    await settle();

    expect(api.workSchedule).toHaveBeenCalledTimes(2);
    expect(page().querySelector<HTMLInputElement>('#kb-work-start')?.value).toBe('09:00');
    expect(chips()[6].getAttribute('aria-pressed')).toBe('false');
    expect(snackbar.show).not.toHaveBeenCalled();
  });

  it('says when the schedule could not be read, and reads it again on Reîncearcă', async () => {
    api.workSchedule.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 500 })));
    await shown();

    expect(page().textContent).toContain('Nu am putut încărca setările.');
    [...page().querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent.trim() === 'Reîncearcă')!
      .click();
    await settle();

    expect(page().querySelector('h2')?.textContent.trim()).toBe('Program de lucru');
  });
});
