# 0021. Vectori în PostgreSQL prin pgvector

## Decizie
Vectorii textelor stau în PostgreSQL-ul aplicației, prin extensia `pgvector`, într-o tabelă proprie, `task_embeddings`. Baza rulează pe o imagine proprie: `postgres` pe Alpine, fixat la versiune, cu `pgvector` construit din sursă. Căutarea filtrează întâi pe coloane și calculează distanța doar pe rândurile rămase, fără index pe vectori la început.

## Motiv
- Filtrele (proiect, asignați, perioadă, arhivat) și distanța stau în aceeași interogare SQL, cu indecșii care există deja. Datele task-urilor nu se copiază nicăieri.
- O singură bază de operat, cu un singur backup și o singură restaurare.
- Măsurat pe 2026-11-19, pe 5000 de rânduri cu vectori de 1536 de numere: fără filtru, 72 ms; cu filtru pe un proiect din 20, 4 ms. Fără index, rezultatul e exact.
- Imaginea rămâne pe Alpine, ca ordinea românească (`ro-x-icu`) să nu se schimbe sub o bază existentă. Proba a arătat aceeași versiune ICU și aceeași ordine.

## Alternative
- O bază separată de vectori (Azure AI Search, Qdrant, Pinecone): încă un serviciu de operat și de ținut sincron, iar filtrele ar trebui copiate lângă vectori.
- Imaginea gata făcută `pgvector/pgvector`: e pe Debian, deci schimbă sistemul și biblioteca ICU de sub o bază existentă.
- Un index HNSW de la început: e aproximativ și caută întâi vecinii, iar filtrul vine după, deci poate întoarce mai puține rezultate decât s-au cerut. Se încearcă abia în pasul 6, pe date de probă.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]] · [[Kanban pe steroizi/RAG spec|RAG spec]] · [[Kanban pe steroizi/Decisions/Modele AI în spatele unei interfețe|Modele AI în spatele unei interfețe]] · [[Kanban pe steroizi/Decisions/Postgres real în teste|Postgres real în teste]]
