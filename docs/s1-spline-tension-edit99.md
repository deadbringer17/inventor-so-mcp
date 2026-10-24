# S1 — Modifica tensione nel plugin, incremento 99

24 ottobre 2026. Implementazione sperimentale PC; collaudo nativo del percorso
di prodotto ancora aperto. Nessuna chiusura dei gate S1.

`edit_sketch/spline_tension_update` accetta esattamente `references`, con
un riferimento tipizzato alla spline interpolata intera, e `tension`, intero
da 0 a 100. Il contratto conserva documento, revisione, schizzo e identità
opaca; rifiuta valori convertiti da stringhe/float, sottoparti, spline CV e
opzioni sconosciute.

L'handler controlla revisione prima della risoluzione e prima della scrittura,
identità COM dello schizzo proprietario e chiave persistente della curva.
Richiede la transazione atomica corrente e rifiuta riferimenti proiettati,
derivati o appartenenti a blocchi. Esegue un solo setter `SketchSpline.Tension`;
non sostituisce la curva, non imposta FitMethod e non scrive opzioni globali.
Solve/Update e rollback rimangono nel normale percorso batch.

La snapshot dello schizzo restituisce ora il valore nativo `tension` delle
spline interpolate. Il comando e il suo contratto sono esposti soltanto nel
tier sperimentale; il client Unity non è ancora collegato.

38 test della policy passati; suite bridge completa: **1.908 test passati**.
Build plugin 2027 sperimentale e ordinaria senza errori/warning; compatibilità
2024 passata con il warning nullable preesistente di SendCodeHandler.
Candidato sperimentale congelato in `increment99/producer-r1`, SHA256
`0D7801E7332B0743FCDB00C343CBDE888B9BBD8B1A70281DB14CD200CECC22E1`.

La [ricerca API 97](s1-spline-tension97.md) verifica cinque valori su tre
metodi, Parte/Lamiera e chiusura aperta/chiusa. Non sostituisce il collaudo di
questo handler. Restano preview/rollback, commit, prima snapshot, confronto
nativo, casi rifiutati, vincoli/dipendenze, modifica successiva, riapertura,
opzioni e oracle desktop, integrazione editor e Quest.

La sonda `S1SplineTensionEditOwnedHost99` è compilata, con guardie di processo
e documenti posseduti. Matrice prevista: 60 casi positivi, 16 rifiuti,
272 stati raw. Confronta la curva modificata con un'operazione API indipendente,
compresi punti, metodo, tensione, chiusura, campioni e tratti; cattura la prima
snapshot del plugin prima dell'ispezione nativa. Richiede preview/Abort esatto,
revisioni, conservazione delle identità e pulizia delle fixture. La prova
del riferimento esterno conserva il vero token di revisione, registrando
esplicitamente il codice atteso secondo l'ordine di validazione del prodotto.
Non è stata ancora eseguita: è in coda alla matrice dei punti 98 r2.
