# 0001. RBAC pe două niveluri

## Decizie
Accesul se bazează pe două niveluri separate: roluri cu permisiuni la nivel de aplicație și roluri la nivel de proiect (`Owner`, `Membru`).

## Motiv
- Adminul trebuie să poată crea roluri noi fără schimbări în cod, de aceea codul verifică permisiuni, nu nume de roluri.
- Ce poate face cineva în aplicație e separat de proiectele la care are acces.
- Șeful poate vedea toate proiectele prin `projects.view_all`, fără să fie adăugat pe fiecare.
- Permisiunile de pe endpoint-uri se citesc din token, ca să nu coste o interogare la fiecare cerere. Accesul la proiect se citește din baza de date, pentru că întrebarea poate fi despre alt utilizator decât cel care face cererea.

## Alternative
Un singur nivel de roluri, la nivel de aplicație. E mai simplu, dar nu poate exprima cine e owner sau membru într-un anumit proiect.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]]
