import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Subject, of } from 'rxjs';
import { Confirmation } from '../../shared/ui/confirm-dialog';
import { UploadZone } from '../../shared/ui/upload-zone';
import { AttachmentDto, TaskItemDto } from './models';
import { AttachmentsApi, UploadProgress } from './attachments-api';
import { TaskAttachments, fileSize } from './task-attachments';
import { aTask, anAttachment, refused } from './task-testing';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TaskAttachments],
  template: `<kb-task-attachments
    [task]="task()"
    (attached)="attached.push($event)"
    (detached)="detached.push($event)"
  />`,
})
class Host {
  readonly task = signal<TaskItemDto>(aTask());
  readonly attached: AttachmentDto[] = [];
  readonly detached: string[] = [];
}

/**
 * docs/UI_UX_SPEC.md §6.3 and §7.5: the task's files, each downloaded under its name; kb-upload-zone for
 * whoever can change the task, with a row per file going up and its progress; a delete after asking.
 */
describe('TaskAttachments', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let confirmation: { ask: ReturnType<typeof vi.fn> };
  let fixture: ComponentFixture<Host>;
  let uploads: Subject<UploadProgress>[];

  beforeEach(() => {
    uploads = [];
    api = {
      upload: vi.fn(() => {
        const upload = new Subject<UploadProgress>();
        uploads.push(upload);
        return upload;
      }),
      download: vi.fn(() => of(new Blob(['%PDF']))),
      delete: vi.fn(() => of(undefined)),
    };
    confirmation = { ask: vi.fn(() => Promise.resolve(true)) };
    TestBed.configureTestingModule({
      providers: [
        { provide: AttachmentsApi, useValue: api },
        { provide: Confirmation, useValue: confirmation },
      ],
    });
  });

  async function shown(task: TaskItemDto = aTask()): Promise<HTMLElement> {
    fixture = TestBed.createComponent(Host);
    fixture.componentInstance.task.set(task);
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

  function choose(...files: File[]): void {
    const zone = fixture.debugElement.query(By.directive(UploadZone))
      .componentInstance as UploadZone;
    zone.chosen.emit(files);
  }

  function rows(): HTMLElement[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.attachment')];
  }

  function uploading(): HTMLElement[] {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('kb-upload-zone li'),
    ];
  }

  const withTwo = () =>
    aTask({
      attachments: [
        anAttachment({ id: 'a1', fileName: 'brief.pdf', size: 1_536, canDelete: true }),
        anAttachment({
          id: 'a2',
          fileName: 'schita.png',
          size: 2_621_440,
          uploadedBy: { userId: 'u2', displayName: 'Ana Pop' },
          canDelete: false,
        }),
      ],
    });

  it('lists the files with their size, who uploaded them and when', async () => {
    await shown(withTwo());

    const [first, second] = rows();
    expect(first.querySelector('.name')?.textContent.trim()).toBe('brief.pdf');
    expect(first.querySelector('.size')?.textContent.trim()).toBe('1,5 KB');
    expect(second.querySelector('.size')?.textContent.trim()).toBe('2,5 MB');
    expect(second.querySelector('.by')?.textContent).toContain('Ana Pop');
    expect(second.querySelector('time')?.getAttribute('datetime')).toBe('2026-11-07T08:00:00Z');
  });

  it('writes sizes as ro-RO does', () => {
    expect(fileSize(0)).toBe('0 B');
    expect(fileSize(1_023)).toBe('1.023 B');
    expect(fileSize(1_024)).toBe('1 KB');
    expect(fileSize(26_214_400)).toBe('25 MB');
  });

  it('downloads a file under its name when its name is chosen', async () => {
    await shown(withTwo());
    const created = vi.fn(() => 'blob:kanabol/1');
    const revoked = vi.fn();
    Object.assign(URL, { createObjectURL: created, revokeObjectURL: revoked });
    const clicked = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement,
    ) {
      expect(this.download).toBe('brief.pdf');
      expect(this.href).toBe('blob:kanabol/1');
    });

    rows()[0].querySelector<HTMLButtonElement>('button.name')!.click();
    await settle();

    expect(api['download']).toHaveBeenCalledWith('a1');
    expect(clicked).toHaveBeenCalledTimes(1);
    expect(revoked).toHaveBeenCalledWith('blob:kanabol/1');
    clicked.mockRestore();
  });

  it('sends each file chosen in a request of its own, with its progress, then lists it', async () => {
    await shown();

    choose(new File(['a'], 'unu.pdf'), new File(['b'], 'doi.txt'));
    await settle();

    expect(api['upload']).toHaveBeenCalledTimes(2);
    expect(uploading().map((row) => row.querySelector('.name')?.textContent.trim())).toEqual([
      'unu.pdf',
      'doi.txt',
    ]);
    uploads[0].next({ progress: 40, attachment: null });
    await settle();
    expect(
      uploading()[0].querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow'),
    ).toBe('40');

    const sent = anAttachment({ id: 'a9', fileName: 'unu.pdf' });
    uploads[0].next({ progress: 100, attachment: sent });
    uploads[0].complete();
    await settle();

    expect(fixture.componentInstance.attached).toEqual([sent]);
    expect(uploading().map((row) => row.querySelector('.name')?.textContent.trim())).toEqual([
      'doi.txt',
    ]);
  });

  it('refuses a file over 25 MB in its row, without sending it', async () => {
    await shown();
    const big = new File(['x'], 'mare.zip');
    Object.defineProperty(big, 'size', { value: 25 * 1024 * 1024 + 1 });

    choose(big);
    await settle();

    expect(api['upload']).not.toHaveBeenCalled();
    expect(uploading()[0].querySelector('.error')?.textContent).toContain('25 MB');
  });

  it('says in its row that the API refused a file, which can then be taken away', async () => {
    await shown();
    api['upload'].mockReturnValueOnce(refused(400, 'Validation', { file: ['no'] }));

    choose(new File(['a'], 'program.exe'));
    await settle();

    expect(uploading()[0].querySelector('.error')?.textContent).toContain('nu e acceptat');
    expect(uploading()[0].querySelector('.name')?.textContent.trim()).toBe('program.exe');
    uploading()[0].querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    expect(uploading()).toHaveLength(0);
  });

  it('stops a file going up when its row is taken away', async () => {
    await shown();

    choose(new File(['a'], 'unu.pdf'));
    await settle();
    uploading()[0].querySelector<HTMLButtonElement>('button')!.click();
    await settle();

    expect(uploads[0].observed).toBe(false);
    expect(uploading()).toHaveLength(0);
  });

  it('deletes a file after asking, and only where it may', async () => {
    await shown(withTwo());

    const [mine, theirs] = rows();
    expect(theirs.querySelector('button.delete')).toBeNull();
    mine.querySelector<HTMLButtonElement>('button.delete')!.click();
    await settle();

    expect(confirmation.ask).toHaveBeenCalledWith(
      expect.objectContaining({ title: 'Ștergi fișierul «brief.pdf»?', destructive: true }),
    );
    expect(api['delete']).toHaveBeenCalledWith('a1');
    expect(fixture.componentInstance.detached).toEqual(['a1']);
  });

  it('keeps a file when the delete is not confirmed, or refused', async () => {
    await shown(withTwo());
    confirmation.ask.mockResolvedValueOnce(false);

    rows()[0].querySelector<HTMLButtonElement>('button.delete')!.click();
    await settle();
    expect(api['delete']).not.toHaveBeenCalled();

    api['delete'].mockReturnValueOnce(refused(403, 'Attachment.NotYours'));
    rows()[0].querySelector<HTMLButtonElement>('button.delete')!.click();
    await settle();
    expect(fixture.componentInstance.detached).toEqual([]);
  });

  it('has no zone for whoever cannot change the task, and says when there are no files', async () => {
    const root = await shown(aTask({ canChange: false }));

    expect(root.querySelector('kb-upload-zone')).toBeNull();
    expect(root.querySelector('.empty')?.textContent.trim()).toBe('Niciun atașament încă.');
  });
});
