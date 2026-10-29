# S1 — Modifica dei punti spline nel plugin, incremento 98

Implementazione del 24 ottobre 2026. Aggiornamento 28 ottobre: matrice nativa
del candidato congelato completata nell'[incremento 102](s1-spline-point-verification102.md):
**128 casi, 14 rifiuti, 540 stati raw**, verifica offline PASS del sottoinsieme
e cinque alterazioni rifiutate. I precedenti tentativi incompleti restano sotto.
Nessun gate S1 chiuso e nessuna parità completa dichiarata.
Il successivo [incremento XR 103](s1-spline-point-xr103.md) collega il comando
all’editor condiviso Parte/Lamiera; gli esiti nativi sotto restano distinti.

`edit_sketch/spline_point_update` modifica il punto nativo esistente tramite
`SketchPoint.MoveTo`. Non ricrea la spline, non elimina vincoli e non crea
maniglie. Solve e Update rimangono nel normale percorso atomico del padre.

Le opzioni sono esattamente `references` e `position_mm`. I riferimenti sono
due, ordinati: spline interpolata/CV intera (`Entity`), poi punto reale
(`Point` o `Entity`). Ognuno conserva i sei campi tipizzati documento,
revisione, schizzo, entità, tipo e sottoparte. La posizione è una coppia XY
finita in millimetri. Le revisioni vengono controllate prima della risoluzione
e nuovamente prima della mutazione, con rifiuto strutturato dei riferimenti
scaduti.

L'appartenenza viene verificata enumerando i veri FitPoint/ControlPoint:
servono sia la stessa identità COM sia la stessa chiave persistente del
punto selezionato. Coordinate vicine e indici di collezione non diventano
identità o prove di ownership. Gli alias nativi dell'estremo di chiusura
sono ammessi quando identità e chiave coincidono. Proiezioni, geometrie
derivate, blocchi ed estremi strutturali di maniglie/immagini sono rifiutati
prima della scrittura. Il contratto è pubblicato nel solo tier sperimentale;
alla consegna 98 l'editor Unity non era ancora collegato a questa operazione.

## Evidenza della consegna iniziale

- 47 test della policy passati: input, congelamento, identità opache e
  riferimenti/documenti/revisioni incoerenti. Non sono prove Inventor.
- Suite bridge completa: 1.870 test passati, con diagnostico preesistente
  dell'ID xUnit duplicato conservato nei log.
- Build add-in 2027 sperimentale e ordinaria: zero errori/warning.
- Build compatibilità 2024 passata, con il warning nullable preesistente
  di `SendCodeHandler.cs`.
- Candidato congelato `increment98/producer-r1/Inventor.So.AddIn.dll`, SHA256
  `D70976D527A541CE0940B0DF13CD318C9775C9D55F548961758D1BFAEC1B2877`.
  Sette sorgenti/test associati conservati separatamente.

La sonda nativa r1, run `16b54e9045df46e9bb54a955224c7206`, si è fermata
al settimo caso: Parte, metodo 26369, aperta, 64 punti, punto interno.
Il confronto della posizione richiesta con il punto risolto rileva circa
0,0000135 mm di scarto. Il messaggio generico parlava di oracle, ma l'arresto
precede la creazione dell'oracle indipendente: r1 non ne prova l'equivalenza.
Stati raw e fallimento conservati, cleanup e assenza PID verificati.

La sonda r2 registra posizione richiesta, posizione effettiva e residui sia
per plugin sia per API nativa. Il risultato è confrontato con l'operazione
nativa indipendente, mantenendo le tolleranze geometriche esistenti: non viene
più equiparato un fit point risolto al dato di input senza prova nativa.
Non si dichiara posizionamento esatto rispetto alla richiesta.

R2, run `1219bb6a274847f99ccec7c6b0b5f9cb`, termina con FAIL dopo 98 casi
passati e 99 avviati su 128. Il caso successivo (Lamiera, metodo 26371,
aperta, cinque punti, Start) registra `System.UnauthorizedAccessException`,
HRESULT `0x80070005`; causa non diagnosticata. I rifiuti non sono stati
eseguiti. Conservati 393 stati raw; nessun PASS dell'intera matrice né del
verificatore offline. Worker uscito, assenza PID 129656 verificata separatamente,
manifest e DLL ordinari ripristinati e controllati tramite SHA256.
Al checkpoint richiesto dall'utente non viene avviato un ulteriore tentativo.
La matrice deve verificare preview/rollback, prima
snapshot dopo commit, appartenenza dei punti, confronto con API indipendente,
estremi/alias di chiusura, tutti i tre metodi fit e budget fino a 64 input.
La parte restante della matrice e il controllo offline rimangono aperti.

Restano da verificare solver, vincoli e dipendenze, maniglie, aggiornamenti
successivi, persistenza/riapertura, UI/Quest e comportamento fisico.
Questa modifica non completa i due comandi spline né il perimetro S1.
