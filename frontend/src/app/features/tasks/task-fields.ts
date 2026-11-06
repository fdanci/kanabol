/** What the API takes (TaskItem.MaxTitleLength, MaxDescriptionLength). */
export const TASK_TITLE_MAX = 300;
export const TASK_DESCRIPTION_MAX = 20_000;

export function taskTitleProblem(value: string): string | null {
  if (value.trim() === '') {
    return $localize`Scrie titlul task-ului.`;
  }
  return value.length > TASK_TITLE_MAX
    ? $localize`Cel mult ${TASK_TITLE_MAX}:max: de caractere.`
    : null;
}

export function taskDescriptionProblem(value: string): string | null {
  return value.length > TASK_DESCRIPTION_MAX
    ? $localize`Cel mult ${TASK_DESCRIPTION_MAX}:max: de caractere.`
    : null;
}

/** What the API refused, in words for the field it was about (the API keeps its own in English). */
export function refusedFieldMessage(field: string): string {
  switch (field) {
    case 'title':
      return $localize`Titlul nu e bun: e prea lung sau are caractere care nu se văd.`;
    case 'description':
      return $localize`Descrierea e prea lungă sau are caractere care nu se văd.`;
    case 'assigneeId':
      return $localize`Poate fi asignat doar un membru activ al proiectului.`;
    case 'columnId':
      return $localize`Coloana nu mai e pe board-ul proiectului. Reîncarcă pagina.`;
    default:
      return $localize`Valoarea nu a fost primită. Reîncarcă pagina și reîncearcă.`;
  }
}

/**
 * A deadline is a day: the end of that day where the person is, so a task due today isn't late
 * until midnight. Back in the panel it shows as that day.
 */
export function endOfDay(date: Date): string {
  const end = new Date(date.getFullYear(), date.getMonth(), date.getDate(), 23, 59, 59, 999);
  return end.toISOString();
}
