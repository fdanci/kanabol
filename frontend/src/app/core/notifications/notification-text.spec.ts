import { TestBed } from '@angular/core/testing';
import { NotificationLine } from './notification-line';
import {
  badgeText,
  notificationIcon,
  notificationText,
  notificationTypeLabel,
} from './notification-text';
import { aNotification } from './notifications-testing';

/** docs/UI_UX_SPEC.md §7.6: the text comes from the type and the task, without who did it. */
describe('notificationText', () => {
  it('says a task is past its deadline', () => {
    expect(notificationText(aNotification({ type: 'TaskOverdue' }))).toBe(
      'KAN-12: Login SSO a depășit deadline-ul',
    );
  });

  it('says the user was assigned, without who assigned them', () => {
    expect(
      notificationText(
        aNotification({ type: 'TaskAssigned', message: 'Ana Pop assigned you KAN-12: Login SSO' }),
      ),
    ).toBe('Ai fost asignat pe KAN-12: Login SSO');
  });

  it('says there is a new comment, without its author', () => {
    expect(
      notificationText(
        aNotification({ type: 'CommentAdded', message: 'Ana Pop commented on KAN-12: Login SSO' }),
      ),
    ).toBe('Comentariu nou pe KAN-12: Login SSO');
  });

  it("shows the API's message for one without a task, or of a type it doesn't know", () => {
    expect(notificationText(aNotification({ task: null, message: 'Something else' }))).toBe(
      'Something else',
    );
    expect(
      notificationText(aNotification({ type: 'ProjectArchived' as never, message: 'Later' })),
    ).toBe('Later');
  });

  it("gives each type its icon, and the bell's to one it doesn't know", () => {
    expect(notificationIcon('TaskOverdue')).toBe('event_busy');
    expect(notificationIcon('TaskAssigned')).toBe('person_add');
    expect(notificationIcon('CommentAdded')).toBe('chat_bubble');
    expect(notificationIcon('Later' as never)).toBe('notifications');
  });

  it('names the types as the page filters them', () => {
    expect(notificationTypeLabel('TaskOverdue')).toBe('Deadline');
    expect(notificationTypeLabel('TaskAssigned')).toBe('Asignări');
    expect(notificationTypeLabel('CommentAdded')).toBe('Comentarii');
  });

  it('writes the badge: nothing at 0, the number up to 99, then 99+', () => {
    expect(badgeText(0)).toBe('');
    expect(badgeText(1)).toBe('1');
    expect(badgeText(99)).toBe('99');
    expect(badgeText(100)).toBe('99+');
  });
});

describe('NotificationLine', () => {
  async function line(unread: boolean): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(NotificationLine);
    fixture.componentRef.setInput(
      'notification',
      aNotification({ readAt: unread ? null : new Date().toISOString() }),
    );
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it("shows the type's icon, the text and the relative time", async () => {
    const element = await line(true);

    expect(element.querySelector('.icon')?.textContent.trim()).toBe('event_busy');
    expect(element.querySelector('.text')?.textContent.trim()).toBe(
      'KAN-12: Login SSO a depășit deadline-ul',
    );
    expect(element.querySelector('.time')?.textContent.trim()).toBe('acum 1 minut');
  });

  it('marks an unread one with its dot and a hidden word, a read one with neither', async () => {
    const unread = await line(true);
    expect(unread.querySelector('.dot')).not.toBeNull();
    expect(unread.querySelector('.kb-visually-hidden')?.textContent.trim()).toBe('Necitită');

    const read = await line(false);
    expect(read.querySelector('.dot')).toBeNull();
    expect(read.querySelector('.kb-visually-hidden')).toBeNull();
  });
});
