# 0005. Angular Signals și Material, fără NgRx

## Decizie
Frontendul folosește Angular standalone, cu starea în servicii și Signals: câte un store per feature, fără store extern. Componentele vin din Angular Material și CDK.

## Motiv
- Lucrez singur, iar NgRx clasic are prea mult boilerplate.
- Signals merg împreună cu `OnPush` și zoneless, fără `ChangeDetectorRef`.
- `resource()` și `httpResource()` oferă direct stările de încărcare și eroare.
- CDK include drag & drop-ul de care are nevoie kanbanul.

## Alternative
NgRx clasic: prea mult boilerplate pentru un singur dezvoltator.

## Legate
[[Kanban pe steroizi/_Architecture|_Architecture]]
