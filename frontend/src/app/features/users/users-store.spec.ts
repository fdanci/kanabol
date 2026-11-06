import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { Page } from '../../shared/models/page';
import { UserDto } from './models';
import { UsersApi } from './users-api';
import { UsersStore } from './users-store';

function user(id: string): UserDto {
  return {
    id,
    email: `${id}@kanabol.test`,
    displayName: id,
    roleId: 'r3',
    roleName: 'Membru',
    isActive: true,
    mustChangePassword: false,
    lockedUntil: null,
    createdAt: '2026-11-04T08:00:00Z',
  };
}

describe('UsersStore', () => {
  let answers: Subject<Page<UserDto>>[];
  let store: UsersStore;

  beforeEach(() => {
    answers = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: UsersApi,
          useValue: {
            list: () => {
              const answer = new Subject<Page<UserDto>>();
              answers.push(answer);
              return answer;
            },
          },
        },
      ],
    });
    store = TestBed.inject(UsersStore);
  });

  function answer(index: number, page: number, ids: string[]): void {
    answers[index].next({ items: ids.map(user), page, pageSize: 25, totalCount: 60 });
    answers[index].complete();
  }

  it('shows the last page asked for, even when an earlier answer arrives after it', async () => {
    const first = store.load(1);
    const second = store.load(2);

    answer(1, 2, ['b']);
    await second;
    answer(0, 1, ['a']);
    await first;

    expect(store.page()).toBe(2);
    expect(store.users().map((each) => each.id)).toEqual(['b']);
    expect(store.state()).toBe('ready');
  });

  it('keeps an earlier failure from hiding the page that arrived', async () => {
    const first = store.load(1);
    const second = store.load(2);

    answer(1, 2, ['b']);
    await second;
    answers[0].error(new Error('late'));
    await first;

    expect(store.state()).toBe('ready');
    expect(store.users().map((each) => each.id)).toEqual(['b']);
  });
});
