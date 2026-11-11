import { BoardColumnDto, BoardTaskDto } from './models';

/** The cards of a column, from the top. */
export function cardsIn(tasks: readonly BoardTaskDto[], columnId: string): BoardTaskDto[] {
  return tasks
    .filter((task) => task.columnId === columnId)
    .sort((a, b) => a.position - b.position || a.number - b.number);
}

/**
 * The board after one card moved, as the API will have it (docs/SPEC.md §6.3): the card at its place
 * in the column, past the end meaning last, and the cards of the column it left and of the one it
 * entered at 0, 1, 2... Entering a done column completes it, leaving one opens it (§5.1).
 */
export function moveCard(
  tasks: readonly BoardTaskDto[],
  columns: readonly BoardColumnDto[],
  taskId: string,
  columnId: string,
  position: number,
  now: Date,
): BoardTaskDto[] {
  const moving = tasks.find((task) => task.id === taskId);
  const column = columns.find((found) => found.id === columnId);
  if (!moving || !column) {
    return [...tasks];
  }

  const placed = new Map<string, Partial<BoardTaskDto>>();
  const target = cardsIn(tasks, columnId).filter((task) => task.id !== taskId);
  const place = Math.max(0, Math.min(position, target.length));
  target.splice(place, 0, moving);
  target.forEach((task, index) => placed.set(task.id, { position: index, columnId }));
  if (moving.columnId !== columnId) {
    cardsIn(tasks, moving.columnId)
      .filter((task) => task.id !== taskId)
      .forEach((task, index) => placed.set(task.id, { position: index }));
  }
  // docs/SPEC.md §4.4: leaving a column forgets the pause's reason, and the counter runs only in În lucru.
  const left =
    moving.columnId === columnId
      ? {}
      : { pauseReason: null, isWorking: column.kind === 'InProgress' };
  placed.set(taskId, {
    columnId,
    position: place,
    completedAt: column.kind === 'Done' ? (moving.completedAt ?? now.toISOString()) : null,
    ...left,
  });

  return tasks.map((task) => {
    const change = placed.get(task.id);
    return change ? { ...task, ...change } : task;
  });
}

/**
 * The place in the whole column for a card dropped among the cards the filters let through: just
 * before the card it was dropped above, or just after the last one shown. Hidden cards keep theirs.
 */
export function placeAmong(
  column: readonly BoardTaskDto[],
  shown: readonly BoardTaskDto[],
  shownIndex: number,
): number {
  if (shownIndex < shown.length) {
    const above = column.findIndex((task) => task.id === shown[shownIndex].id);
    return above < 0 ? column.length : above;
  }
  const last = shown.at(-1);
  if (!last) {
    return column.length;
  }
  const after = column.findIndex((task) => task.id === last.id);
  return after < 0 ? column.length : after + 1;
}

/**
 * The columns with one of them at a new place from 0, the others closing up around it, numbered 0, 1, 2...
 * as the API numbers them (docs/SPEC.md §4.4).
 */
export function placeColumn(
  columns: readonly BoardColumnDto[],
  columnId: string,
  position: number,
): BoardColumnDto[] {
  const ordered = [...columns].sort((a, b) => a.position - b.position);
  const moving = ordered.find((column) => column.id === columnId);
  if (!moving) {
    return [...columns];
  }
  const others = ordered.filter((column) => column.id !== columnId);
  others.splice(Math.max(0, Math.min(position, others.length)), 0, moving);
  return others.map((column, index) => ({ ...column, position: index }));
}
