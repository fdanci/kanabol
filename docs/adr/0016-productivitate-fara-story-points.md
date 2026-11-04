# 0016. Productivitate fără story points

## Decizie
Productivitatea se urmărește prin trei metrici, pe interval, proiect și utilizator: task-uri finalizate, lead time și cycle time, respectarea deadline-urilor. Nu folosim story points.

## Motiv
- Toate trei se calculează din date care există deja (`completed_at`, `due_date`, `task_history_entries`).
- Nimeni nu trebuie să introducă ceva în plus ca să apară în rapoarte.

## Alternative
- Story points: ar cere o estimare pentru fiecare task.
- Tabelă materializată `task_column_stays`: amânată până când apar date reale și query-ul pe istoric devine lent.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/Arhivare în loc de ștergere|Arhivare în loc de ștergere]]
