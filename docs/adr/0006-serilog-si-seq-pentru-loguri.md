# 0006. Serilog și Seq pentru loguri

## Decizie
Logurile sunt structurate și merg prin Serilog în consolă și în Seq, cu un `CorrelationId` pe fiecare request. Istoricul task-urilor stă în baza de date (`task_history_entries`), nu în loguri.

## Motiv
- Un utilizator poate raporta codul de eroare afișat, iar requestul se găsește după el în Seq.
- Erorile din frontend ajung în Seq lângă cele din backend.
- Seq e gratuit pentru un singur utilizator.
- Istoricul e o funcție a produsului. Logurile sunt pentru operare și debugging, iar Seq le ține 30 de zile.

## Alternative
- Istoric reconstruit din loguri: s-ar pierde după 30 de zile.
- Doar consolă, cu logurile strânse de Docker: fără căutare după proprietăți.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]]
