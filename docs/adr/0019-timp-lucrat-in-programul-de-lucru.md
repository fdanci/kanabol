# 0019. Timp lucrat în programul de lucru

## Decizie
Fiecare task are o estimare, în ore, și un counter de timp lucrat. Counter-ul curge doar cât task-ul stă în coloana „În lucru” și numără doar timpul din programul de lucru. Programul e implicit luni–vineri, 08:30–16:30, și se schimbă din pagina „Setări generale”, de către cine are permisiunea `settings.manage`.

- Estimarea se cere o dată, la prima intrare în „În lucru”, și se poate schimba apoi din task. Schimbările intră în istoric.
- Mutarea în orice altă coloană oprește counter-ul. Întoarcerea în „În lucru” îl pornește de unde a rămas.
- Rapoartele compară timpul lucrat cu estimarea, pe om și pe proiect.

## Motiv
- Timpul calendaristic ar aduna nopțile și weekendurile: un task estimat la 4 ore și lăsat în lucru peste noapte ar arăta 19 ore.
- Un counter legat de coloană nu cere nimănui să pornească și să oprească un cronometru.
- O permisiune separată pentru setări respectă regula după care codul verifică permisiuni, nu nume de roluri.

## Alternative
- Timp calendaristic, 24 din 24: mai simplu, dar cifrele n-ar putea fi comparate cu estimările.
- Cronometru pornit și oprit de om: mai exact, dar depinde de disciplina fiecăruia.
- Counter care curge și în coloanele proprii de după „În lucru”: ar număra și așteptarea, de exemplu la review.
- Sărbătorile legale în program: lăsate pentru mai târziu.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]] · [[Kanban pe steroizi/Decisions/Coloane fixe pe board|Coloane fixe pe board]] · [[Kanban pe steroizi/Decisions/Productivitate fără story points|Productivitate fără story points]]
