import { cardsIn, moveCard, placeAmong } from './board-moves';
import { COLUMNS, aBoard, aCard, titlesByColumn } from './board-testing';
import { BoardTaskDto } from './models';

/** docs/SPEC.md §6.3: the board after a move is the board the API will have. */
describe('board moves', () => {
  const now = new Date('2026-11-06T12:00:00Z');
  let tasks: BoardTaskDto[];

  beforeEach(() => {
    tasks = aBoard().tasks;
  });

  function moved(title: string, columnId: string, position: number): BoardTaskDto[] {
    const task = tasks.find((found) => found.title === title)!;
    return moveCard(tasks, COLUMNS, task.id, columnId, position, now);
  }

  function places(board: BoardTaskDto[]): string {
    return COLUMNS.slice()
      .sort((a, b) => a.position - b.position)
      .map((column) =>
        cardsIn(board, column.id)
          .map((task) => task.position)
          .join(''),
      )
      .join('|');
  }

  describe('cardsIn', () => {
    it('gives the cards of a column from the top, ties by number', () => {
      const tied = [aCard('KAN-9', 'c1', 1), aCard('KAN-2', 'c1', 1), aCard('KAN-5', 'c1', 0)];

      expect(cardsIn(tied, 'c1').map((task) => task.key)).toEqual(['KAN-5', 'KAN-2', 'KAN-9']);
    });
  });

  describe('moveCard', () => {
    it('moves a card down and up its column, the others closing up', () => {
      expect(titlesByColumn(moved('A', 'c1', 2), COLUMNS)).toBe('B C A | D | E');
      expect(titlesByColumn(moved('C', 'c1', 0), COLUMNS)).toBe('C A B | D | E');
      expect(places(moved('A', 'c1', 2))).toBe('012|0|0');
    });

    it('moves a card to another column, and both columns stay 0, 1, 2...', () => {
      const board = moved('B', 'c2', 0);

      expect(titlesByColumn(board, COLUMNS)).toBe('A C | B D | E');
      expect(places(board)).toBe('01|01|0');
    });

    it('puts a card past the end last, and one before the top first', () => {
      expect(titlesByColumn(moved('A', 'c2', 9), COLUMNS)).toBe('B C | D A | E');
      expect(titlesByColumn(moved('D', 'c1', -3), COLUMNS)).toBe('D A B C |  | E');
    });

    // docs/SPEC.md §5.1: entering a done column completes a task, leaving one opens it.
    it('completes a card entering a done column, and opens one leaving it', () => {
      const done = moved('A', 'c3', 0).find((task) => task.title === 'A')!;
      expect(done.completedAt).toBe(now.toISOString());

      const reopened = moved('E', 'c1', 0).find((task) => task.title === 'E')!;
      expect(reopened.completedAt).toBeNull();
    });

    it('keeps when a card was completed while it moves within a done column', () => {
      tasks.push(aCard('KAN-6', 'c3', 1, { title: 'F', completedAt: '2026-11-01T08:00:00Z' }));

      const kept = moved('F', 'c3', 0).find((task) => task.title === 'F')!;

      expect(kept.completedAt).toBe('2026-11-01T08:00:00Z');
    });

    it('changes nothing for a card or a column it does not know, nor the list it was given', () => {
      const before = structuredClone(tasks);

      expect(moveCard(tasks, COLUMNS, 'id-KAN-99', 'c2', 0, now)).toEqual(before);
      expect(moveCard(tasks, COLUMNS, 'id-KAN-1', 'c9', 0, now)).toEqual(before);
      moved('A', 'c2', 0);
      expect(tasks).toEqual(before);
    });
  });

  // A card dropped among filtered ones lands before the card it went above, so hidden ones keep their places.
  describe('placeAmong', () => {
    const column = [
      aCard('KAN-1', 'c1', 0),
      aCard('KAN-2', 'c1', 1),
      aCard('KAN-3', 'c1', 2),
      aCard('KAN-4', 'c1', 3),
    ];

    it('gives the place of the shown card it was dropped above', () => {
      const shown = [column[1], column[3]];

      expect(placeAmong(column, shown, 0)).toBe(1);
      expect(placeAmong(column, shown, 1)).toBe(3);
    });

    it('gives the place just after the last shown card when dropped below them all', () => {
      expect(placeAmong(column, [column[0], column[1]], 2)).toBe(2);
    });

    it('gives the end of the column when it shows nothing', () => {
      expect(placeAmong(column, [], 0)).toBe(4);
      expect(placeAmong([], [], 0)).toBe(0);
    });

    it('is the CDK index itself when nothing is hidden', () => {
      expect([0, 1, 2, 3, 4].map((index) => placeAmong(column, column, index))).toEqual([
        0, 1, 2, 3, 4,
      ]);
    });
  });
});
