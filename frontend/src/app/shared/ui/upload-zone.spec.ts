import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { UploadItem, UploadZone } from './upload-zone';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UploadZone],
  template: `<kb-upload-zone
    [uploads]="uploads()"
    [multiple]="multiple()"
    (chosen)="chosen = $event"
    (removed)="removed = $event"
  />`,
})
class Host {
  readonly uploads = signal<UploadItem[]>([]);
  readonly multiple = signal(true);
  chosen: File[] = [];
  removed = '';
}

function filesOf(...names: string[]): DataTransfer {
  const files = names.map((name) => new File(['x'], name));
  return { files } as unknown as DataTransfer;
}

function dragEvent(type: string, dataTransfer?: DataTransfer): Event {
  const event = new Event(type, { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'dataTransfer', { value: dataTransfer });
  return event;
}

describe('UploadZone', () => {
  async function rendered() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    return { fixture, element, zone: element.querySelector('.zone')! };
  }

  it('asks to drop files or choose them, with a real button', async () => {
    const { element } = await rendered();

    expect(element.querySelector('.zone')?.textContent).toContain('Trage fișiere aici sau');
    expect(element.querySelector('button.choose')?.textContent).toBe('alege');
    expect(element.querySelector('input[type=file]')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('lights up while files are dragged over it', async () => {
    const { fixture, zone } = await rendered();

    zone.dispatchEvent(dragEvent('dragover'));
    await fixture.whenStable();
    expect(zone.classList).toContain('over');

    zone.dispatchEvent(dragEvent('dragleave'));
    await fixture.whenStable();
    expect(zone.classList).not.toContain('over');
  });

  it('hands over the files dropped on it', async () => {
    const { fixture, zone } = await rendered();

    const drop = dragEvent('drop', filesOf('plan.pdf', 'logo.png'));
    zone.dispatchEvent(drop);
    await fixture.whenStable();

    expect(drop.defaultPrevented).toBe(true);
    expect(fixture.componentInstance.chosen.map((file) => file.name)).toEqual([
      'plan.pdf',
      'logo.png',
    ]);
    expect(zone.classList).not.toContain('over');
  });

  it('takes only the first file when it takes one', async () => {
    const { fixture, zone } = await rendered();
    fixture.componentInstance.multiple.set(false);
    await fixture.whenStable();

    zone.dispatchEvent(dragEvent('drop', filesOf('plan.pdf', 'logo.png')));

    expect(fixture.componentInstance.chosen.map((file) => file.name)).toEqual(['plan.pdf']);
  });

  it('lists the files with their progress, and lets one be removed', async () => {
    const { fixture, element } = await rendered();
    fixture.componentInstance.uploads.set([
      { id: 'a', name: 'plan.pdf', progress: 40 },
      { id: 'b', name: 'mare.zip', progress: null, error: 'Fișierul trece de 25 MB.' },
    ]);
    await fixture.whenStable();

    const rows = element.querySelectorAll('li');
    expect(rows).toHaveLength(2);
    const bar = rows[0].querySelector('[role=progressbar]')!;
    expect(bar.getAttribute('aria-valuenow')).toBe('40');
    expect((bar.firstElementChild as HTMLElement).style.width).toBe('40%');
    expect(rows[1].querySelector('.error')?.textContent).toBe('Fișierul trece de 25 MB.');

    const remove = rows[1].querySelector('button')!;
    expect(remove.getAttribute('aria-label')).toBe('Elimină mare.zip');
    remove.click();
    expect(fixture.componentInstance.removed).toBe('b');
  });
});
