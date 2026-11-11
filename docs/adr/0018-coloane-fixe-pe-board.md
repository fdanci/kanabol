# 0018. Coloane fixe pe board

## Decizie
Fiecare proiect are patru coloane fixe pe board, în ordinea asta: „Backlog”, „În lucru”, „În pauză” și „Gata”. Ele nu se șterg, nu se redenumesc și nu se mută. Ownerul poate adăuga coloane proprii, dar numai între Backlog și Gata. A cincea coloană, „Arhivate”, o văd doar cei cu permisiunea `tasks.archive`.

- Un task nou pornește mereu în Backlog.
- Intrarea în „În lucru” cere o estimare, dacă task-ul n-are una.
- Intrarea în „În pauză” cere un motiv, care rămâne în istoricul task-ului.
- Un task ajuns în „În lucru” nu se mai întoarce într-o coloană dinaintea ei.
- Terminate sunt task-urile din „Gata”. Marcajul „coloană de task-uri terminate”, pe care ownerul îl putea pune pe orice coloană, dispare.

## Motiv
- Cu marcajul liber, orice coloană putea deveni „terminată”, deci rapoartele puteau fi umflate dintr-un click, fără urmă în istoric.
- Rapoartele și timpul lucrat au nevoie să știe sigur care coloană înseamnă „în lucru”, „pe pauză” și „gata”, în orice proiect.
- Motivul pauzei și estimarea se cer exact în clipa în care contează, la mutare.

## Alternative
- O singură coloană „terminată” la alegerea ownerului: rezolva doar umflarea rapoartelor, nu și timpul lucrat.
- Doar coloanele fixe, fără coloane proprii: mai simplu, dar echipele care au un pas de review n-ar mai avea unde să-l pună.
- Arhivatele rămase doar în lista proiectului: restaurarea ar fi rămas departe de board.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]] · [[Kanban pe steroizi/Decisions/Arhivare în loc de ștergere|Arhivare în loc de ștergere]] · [[Kanban pe steroizi/Decisions/Timp lucrat în programul de lucru|Timp lucrat în programul de lucru]]
