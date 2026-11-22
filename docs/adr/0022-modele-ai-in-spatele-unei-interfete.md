# 0022. Modele AI în spatele unei interfețe

## Decizie
Aplicația cheamă modelele AI doar prin interfețe din Application, cu implementările în Infrastructure. Prima e `IEmbeddingModel`, pentru vectorizare. Implementarea de acum folosește Azure: o resursă Microsoft Foundry în Sweden Central, cu deployment-ul `text-embedding-3-small` (versiunea 1, 1536 de numere, Global Standard), plătit la consum. În Azure stau doar modelele: aplicația și baza rămân pe serverul actual.

Modelele sunt oprite implicit. Oprite, aplicația pornește fără nicio cheie.

## Motiv
- CI-ul nu are secrete, deci testele au nevoie de un model fals. Interfața îl face posibil fără nicio ramură în cod.
- Un model local sau alt furnizor înseamnă o clasă nouă în Infrastructure, nu schimbări în restul codului. Pasul 6 compară Azure cu un model local.
- Un model de vectorizare costă foarte puțin: o propoziție de 6 cuvinte în română a consumat 12 tokeni. Cu un buget lunar și o limită de tokeni pe minut, o greșeală nu poate costa mult.
- `text-embedding-3-small` a pus „raportul lunar” aproape de „situația pe luna asta” (0.70) și departe de „imprimanta” (0.30), deși primele două n-au cuvinte comune în afară de „client”.
- Modelul se retrage pe 2028-02-09. Un model de vectorizare nu se actualizează automat la altă versiune, deci vectorii nu se schimbă pe ascuns. La schimbarea modelului, worker-ul reface vectorii, pentru că fiecare își ține modelul.

## Alternative
- Pachetul Azure chemat direct din handlere: testele ar avea nevoie de chei, iar schimbarea furnizorului ar atinge tot codul.
- `text-embedding-3-large` (3072 de numere): mai bun pe texte multilingve, dar de 6,5 ori mai scump și cu vectori de două ori mai mari. Se poate încerca pe datele de probă.
- Un model local de la început: fără costuri și fără date trimise în afară, dar încă un serviciu de operat pe server. Rămâne comparația din pasul 6.
- Aplicația și baza în Azure: un proiect separat, care nu ține de chat.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]] · [[Kanban pe steroizi/RAG spec|RAG spec]] · [[Kanban pe steroizi/Decisions/Vectori în PostgreSQL prin pgvector|Vectori în PostgreSQL prin pgvector]] · [[Kanban pe steroizi/Decisions/Atașamente pe disc în spatele IFileStorage|Atașamente pe disc în spatele IFileStorage]]
