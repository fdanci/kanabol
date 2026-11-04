# 0010. Atașamente pe disc în spatele IFileStorage

## Decizie
Atașamentele stau pe disc local, într-un volum Docker, în spatele interfeței `IFileStorage`. Metadatele stau în tabela `attachments`.

## Motiv
- Aplicația e internă și are o singură instanță, așa că discul local ajunge.
- Implementarea se poate înlocui fără să atingem `Application`.
- Numele original nu ajunge niciodată pe disc, iar descărcarea trece prin API, cu verificarea accesului.

## Alternative
- Fișiere în baza de date: ar crește baza și backup-ul ei.
- Object storage: încă un serviciu de operat, pentru o singură instanță internă.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]]
