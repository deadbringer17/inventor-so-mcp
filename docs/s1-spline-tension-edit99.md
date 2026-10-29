# S1 — Modifica tensione nel plugin, incremento 99

24 ottobre 2026; collaudo nativo aggiornato il 28 ottobre. Implementazione
sperimentale PC; il sottoinsieme nativo descritto sotto è verificato.
Nessuna chiusura dei gate S1.

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
Non è stata ancora eseguita. Al checkpoint richiesto dall'utente il run 98 r2
è terminato incompleto e non sono state avviate altre suite native.

## Collaudo nativo del 28 ottobre 2026

Run `2e1d6c89b608468a8eb2d2123abf90d8`, candidato congelato 99 invariato:
**60 casi positivi, 16 rifiuti e 272 stati raw**, con verdetto
`PASS_PLUGIN_NATIVE_SPLINE_TENSION_EDIT_SUBSET`. Preview/Abort, commit,
prima snapshot, identità native e confronto con l'oracle API indipendente
sono passati nel perimetro della matrice. I rifiuti includono rollback dopo
una modifica valida seguita da un secondo comando errato.

Wrapper e worker terminano con exit code 0. Il processo Inventor posseduto
151968 è assente dopo il cleanup; manifest ordinario ripristinato byte per
byte (SHA256 `807DAC44FCB8D333647D171482C016078885881E6C4AA4B6050A4C63CE7F98C9`)
e DLL ordinaria invariata. Nessun processo Inventor preesisteva alla prova.
Evidenze: `artifacts/s1-verification/increment99/native-r1/`, report e sidecar
in `worker/`. Il wrapper usato è conservato nella stessa cartella.

Il verificatore offline `scripts/verify-s1-spline-tension99.ps1` passa sui
60/16 casi e sui 272 hash: confronta stati prima/dopo preview e rifiuti,
revisioni, identità e geometria campionata rispetto all'oracle. Non è una
nuova esecuzione CAD e non verifica ogni proprietà della snapshot o ogni
combinazione di vincoli/dipendenze. La matrice 98 dei punti resta incompleta.
Quattro copie alterate sono rifiutate per il motivo atteso: geometria
dell'oracle, tensione richiesta, stato del caso e hash del sidecar. Originali
conservati; esiti in `increment100/20261028-closure-xr/native99-offline-adversarial.json`.

Tensione nell'editor Unity, aggiornamenti successivi, persistenza/riapertura,
oracle desktop e prova fisica restano aperti. Il successivo incremento
[100](s1-spline-closure-xr100.md) collega la **creazione aperta/chiusa**
all'editor; non introduce editing della tensione.

Aggiornamento successivo: l'[incremento 101](s1-spline-tension-xr101.md)
collega la tensione all'editor XR Parte/Lamiera e verifica dodici modifiche
nel simulatore con Inventor reale. È evidenza separata dalla matrice nativa 99;
persistenza, oracle desktop e prova fisica restano aperti.
