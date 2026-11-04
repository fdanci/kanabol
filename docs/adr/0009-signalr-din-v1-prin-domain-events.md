# 0009. SignalR din v1 prin domain events

## Decizie
Kanbanul se actualizează în timp real din prima versiune, prin SignalR: un hub cu grupuri per proiect, alimentat de domain events prin `IBoardNotifier`.

## Motiv
- Domain events decuplează istoricul, alertele și SignalR de handlere.
- Codul de domeniu nu știe de SignalR, pentru că `IBoardNotifier` e doar o interfață în `Application`.
- Alertele folosesc același canal ca să ajungă la clopoțelul din navbar.

## Alternative
- Reîncărcarea periodică a boardului: mutările colegilor s-ar vedea cu întârziere.
- Apeluri SignalR direct din handlere: le-ar lega de transport.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]], [[Kanban pe steroizi/Decisions/Alerte doar in-app|Alerte doar in-app]]
