# 0014. Alerte doar in-app

## Decizie
Alertele se livrează doar în aplicație: listă de notificări, clopoțel și push prin SignalR. În prima versiune nu există email și nici o abstracție pregătită pentru el.

## Motiv
- Prima versiune nu are integrări externe.
- Dacă apare nevoia, emailul se adaugă atunci ca un consumer suplimentar al acelorași domain events.

## Alternative
- Email din prima versiune: o integrare externă de care nu avem încă nevoie.
- `IEmailSender` pregătit de acum: o abstracție fără nicio utilizare.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/SignalR din v1 prin domain events|SignalR din v1 prin domain events]]
