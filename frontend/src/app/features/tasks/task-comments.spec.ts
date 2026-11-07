import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CommentsApi } from './comments-api';
import { TaskComments } from './task-comments';
import { aComment, refused } from './task-testing';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TaskComments],
  template: `<kb-task-comments [taskId]="taskId()" [canComment]="canComment()" />`,
})
class Host {
  readonly taskId = signal('t1');
  readonly canComment = signal(true);
}

/**
 * docs/UI_UX_SPEC.md §7.5: a comment is an avatar, a name, a relative time with the full date in its
 * tooltip, and its text; "Editează" only on the caller's own; a form at the end for whoever can comment.
 */
describe('TaskComments', () => {
  let api: Record<string, ReturnType<typeof vi.fn>>;
  let fixture: ComponentFixture<Host>;

  beforeEach(() => {
    api = {
      list: vi.fn(() =>
        of([
          aComment({
            id: 'cm1',
            body: 'Merge cu **Azure AD**',
            author: { userId: 'u2', displayName: 'Ana Pop' },
            canEdit: false,
          }),
          aComment({ id: 'cm2', body: 'Al meu', canEdit: true }),
        ]),
      ),
      add: vi.fn((_taskId: string, body: string) =>
        of(aComment({ id: 'cm3', body, canEdit: true })),
      ),
      edit: vi.fn((id: string, body: string) =>
        of(aComment({ id, body, canEdit: true, updatedAt: '2026-11-07T10:00:00Z' })),
      ),
    };
    TestBed.configureTestingModule({ providers: [{ provide: CommentsApi, useValue: api }] });
  });

  async function shown(): Promise<HTMLElement> {
    fixture = TestBed.createComponent(Host);
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

  function comments(): HTMLElement[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.comment')];
  }

  function buttonIn(root: HTMLElement, label: string): HTMLButtonElement | undefined {
    return [...root.querySelectorAll('button')].find(
      (candidate) => candidate.textContent.trim() === label,
    );
  }

  function type(field: HTMLTextAreaElement, text: string): void {
    field.value = text;
    field.dispatchEvent(new Event('input'));
  }

  it('shows each comment with its author, when, and its text in markdown, from the oldest', async () => {
    await shown();

    expect(api['list']).toHaveBeenCalledWith('t1');
    const [first, second] = comments();
    expect(first.querySelector('.author')?.textContent.trim()).toBe('Ana Pop');
    expect(first.querySelector('kb-avatar')).not.toBeNull();
    expect(first.querySelector('time')?.getAttribute('datetime')).toBe('2026-11-07T08:00:00Z');
    expect(first.querySelector('kb-markdown strong')?.textContent).toBe('Azure AD');
    expect(second.textContent).toContain('Al meu');
  });

  it('offers "Editează" only on the caller’s own comments', async () => {
    await shown();

    const [theirs, mine] = comments();
    expect(buttonIn(theirs, 'Editează')).toBeUndefined();
    expect(buttonIn(mine, 'Editează')).toBeDefined();
  });

  it('edits a comment in place: Salvează sends it, Anulează puts it back', async () => {
    await shown();
    const mine = comments()[1];

    buttonIn(mine, 'Editează')!.click();
    await settle();
    type(comments()[1].querySelector('textarea')!, 'Al meu, schimbat');
    buttonIn(comments()[1], 'Salvează')!.click();
    await settle();

    expect(api['edit']).toHaveBeenCalledWith('cm2', 'Al meu, schimbat');
    expect(comments()[1].querySelector('textarea')).toBeNull();
    expect(comments()[1].textContent).toContain('Al meu, schimbat');
    expect(comments()[1].querySelector('.edited')?.textContent.trim()).toBe('editat');

    buttonIn(comments()[1], 'Editează')!.click();
    await settle();
    type(comments()[1].querySelector('textarea')!, 'Altceva');
    buttonIn(comments()[1], 'Anulează')!.click();
    await settle();

    expect(api['edit']).toHaveBeenCalledTimes(1);
    expect(comments()[1].textContent).toContain('Al meu, schimbat');
  });

  it('adds a comment from the form at the end and empties it, the button waiting for some text', async () => {
    const root = await shown();
    const field = root.querySelector<HTMLTextAreaElement>('.new textarea')!;
    const send = buttonIn(root.querySelector('.new')!, 'Comentează')!;
    expect(send.disabled).toBe(true);

    type(field, '   ');
    await settle();
    expect(send.disabled).toBe(true);
    type(field, 'Și la mine');
    await settle();
    send.click();
    await settle();

    expect(api['add']).toHaveBeenCalledWith('t1', 'Și la mine');
    expect(
      comments().map((comment) => comment.querySelector('kb-markdown')?.textContent.trim()),
    ).toEqual(['Merge cu Azure AD', 'Al meu', 'Și la mine']);
    expect(root.querySelector<HTMLTextAreaElement>('.new textarea')!.value).toBe('');
  });

  it('sends the form with Ctrl+Enter, while Enter alone starts a new line', async () => {
    const root = await shown();
    const field = root.querySelector<HTMLTextAreaElement>('.new textarea')!;
    type(field, 'Cu tastatura');
    await settle();

    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await settle();
    expect(api['add']).not.toHaveBeenCalled();
    field.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true }));
    await settle();

    expect(api['add']).toHaveBeenCalledWith('t1', 'Cu tastatura');
  });

  it('says a comment is too long without sending it, and keeps a refused one in the form', async () => {
    const root = await shown();
    const field = root.querySelector<HTMLTextAreaElement>('.new textarea')!;

    type(field, 'a'.repeat(5_001));
    await settle();
    buttonIn(root.querySelector('.new')!, 'Comentează')!.click();
    await settle();

    expect(api['add']).not.toHaveBeenCalled();
    expect(root.querySelector('.new .kb-field-error')?.textContent).toContain('5.000');

    api['add'].mockReturnValueOnce(refused(409, 'Task.Archived'));
    type(field, 'Pe arhivat');
    await settle();
    buttonIn(root.querySelector('.new')!, 'Comentează')!.click();
    await settle();

    expect(root.querySelector<HTMLTextAreaElement>('.new textarea')!.value).toBe('Pe arhivat');
  });

  it('has no form, and no "Editează", for whoever cannot comment', async () => {
    fixture = TestBed.createComponent(Host);
    fixture.componentInstance.canComment.set(false);
    api['list'].mockReturnValueOnce(of([aComment({ canEdit: false })]));
    await settle();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('.new')).toBeNull();
    expect(buttonIn(root, 'Editează')).toBeUndefined();
  });

  it('says when there are none yet', async () => {
    api['list'].mockReturnValueOnce(of([]));

    const root = await shown();

    expect(root.querySelector('.empty')?.textContent.trim()).toBe('Niciun comentariu încă.');
  });

  it('reads the comments of another task when the panel shows another', async () => {
    await shown();

    fixture.componentInstance.taskId.set('t2');
    await settle();

    expect(api['list']).toHaveBeenLastCalledWith('t2');
  });
});
