# S1 — Chiusura spline nell'editor XR, incremento 100

28 ottobre 2026. Estensione del percorso sperimentale di creazione spline
dell'[incremento 96](s1-spline-closure96.md), nel perimetro S1-04/05/13/14/16.
Non completa la parità dei comandi spline né i 18 gate S1.

## Comportamento

L'editor condiviso Parte/Lamiera offre **Spline chiusa** durante la raccolta
dei punti, per spline interpolate e di controllo. Ogni nuova raccolta parte
aperta; cambiare chiusura conserva i punti e non invia comandi CAD.
Durante l'input numerico il toggle è disabilitato. Concludi congela `closed`
insieme a punti e metodo nella bozza; Anteprima e Applica restano espliciti.
La scelta sopravvive al refresh di una bozza e richiede la conferma prevista
dal normale percorso di revisione. Annullamento e cambio revisione invalidano
l'input in corso.

Una spline di controllo esplicitamente aperta con estremi coincidenti non
diventa una bozza: l'editor conserva i punti e invita a scegliere chiusa o a
rimuovere l'ultimo punto. La ripetizione del primo punto per la chiusura CV
resta responsabilità del backend, senza duplicazione client.

## Verifiche implementate

- EditMode Parte/Lamiera: aperta/chiusa, interpolata/CV, conservazione punti,
  blocco durante tastierino, congelamento/refresh, annullamento, revisione e
  recupero della raccolta CV con estremi coincidenti.
- Runner S1 in Meta XR Simulator con Inventor 2027: otto creazioni tramite
  catalogo e tastierino del prodotto, preview renderizzata e rollback dello
  schizzo, commit esplicito, lettura nativa di tipo/chiusura/numero punti e
  conservazione delle identità preesistenti. Le fixture vengono chiuse senza
  salvarle dal wrapper; gli input sono sintetici.
- Build Android QA/ordinaria e ispezione dei metadati: ARM64 IL2CPP, runner
  soltanto nel QA. Nessuna installazione sul Quest richiesta per il simulatore.

## Esiti

Evidenze in `artifacts/s1-verification/increment100/20261028-closure-xr/`.

| Livello | Esito |
|---|---|
| Core XR completo | **1.355 PASS** |
| Backend completo | **1.926 PASS**, diagnostico xUnit duplicato preesistente |
| Capture.Worker | **16 PASS** |
| Unity EditMode completo, r2 e r3 | **1.179 PASS**, zero fail/skip; 14 casi nuovi |
| EditMode mirati dopo gli ultimi adeguamenti del runner, r4 e r5 | **19 PASS**, zero fail/skip; sottoinsieme ripetuto, non da sommare al totale |
| Meta XR Simulator + Inventor 2027, `simulator-r4` | **8 casi spline PASS, 692 asserzioni, exit 0**, input sintetici |
| Ripristino fixture e installazione ordinaria | **PASS**, `native-cleanup.json` e `qa-cleanup.json`; processo QA assente |
| APK Android QA/ordinario e metadati | **PASS**, ARM64 IL2CPP; 16 runner nel QA, zero nell'ordinario |

Le suite complete totalizzano **4.476 PASS**. La suite Unity completa precede
gli ultimi adeguamenti delle asserzioni del runner, poi coperti dai 19 casi
mirati e dall'esecuzione XR finale. Il codice prodotto dell'editor è lo
stesso della suite completa; gli adeguamenti successivi sono nel solo runner.

La matrice XR usa tre punti per curva, il metodo interpolante corrente della
fixture e otto combinazioni Parte/Lamiera × interpolata/CV × aperta/chiusa.
Il renderer attivo viene confrontato punto per punto con il nuovo stroke del
piano nativo (tolleranza `1e-7 m`). La preview lascia revisione, entità,
quote e vincoli invariati; Applica avanza la revisione e la rilettura nativa
conferma tipo, flag di chiusura e tre/quattro punti nativi. Le identità delle
entità preesistenti sono conservate. I PNG sono evidenze del frame sintetico,
non una verifica di leggibilità, occlusione o ergonomia.

Il verdetto globale rimane **PARTIAL** per scelta del runner S1: gli otto
sottocasi sopra passano, non l'intera parità S1. Il `NOT COVERED` del runner
sul ripristino è chiuso soltanto dall'evidenza separata del wrapper. Non
sono stati installati APK sul Quest né modificati pairing persistente o
runtime OpenXR globale.

Pacchetti in `apks/`, ispezione in `package-inspection.json`:

| APK | Byte | SHA256 |
|---|---:|---|
| QA Development | 95.750.589 | `A47F411902E4ABBF99BFE5F79BC1555E61018BC869DCA45A8D9912AEB4EB38A9` |
| Ordinario | 58.010.404 | `590C58FCF7B3867833508E34A8C0D62847307317CDE56052B5509CAF604CAAC8` |

Le due cache dinamiche Satoshi modificate automaticamente sono conservate
nelle evidenze e ripristinate dopo i test/build. L'installazione Inventor
ordinaria è riavviata dal manifest originale, verificato mediante SHA256.

### Corse precedenti conservate

- `simulator-r1`: i casi preesistenti linee/testo/parametri Parte/Lamiera e la
  prima spline aperta passano; poi FAIL perché il runner invoca il comando
  successivo prima che il workspace abbia terminato il refresh. Ora attende
  l'abilitazione effettiva nel catalogo.
- `simulator-r2`: FAIL dell'asserzione che limitava l'intera curva chiusa
  all'intervallo X dei punti inseriti. L'oracle renderer deve seguire gli
  stroke nativi. Nella corsa finale, punti X 80–100 mm producono correttamente
  campioni tra circa 78,86 e 101,30 mm.
- `simulator-r3`: FAIL nell'associazione dello stroke nuovo tramite confronto
  della tessellazione con la snapshot precedente. Il runner finale identifica
  la curva nell'area dedicata della fixture, registra inventario e coordinate
  degli stroke e confronta esattamente la geometria del piano con il renderer.

Tutte le fixture delle quattro corse sono ripristinate. Le corse r2–r4 usano
la modalità mirata `XR_SO_S1_SPLINE_CLOSURE_ONLY=1`, abilitata soltanto in
Editor. Il log dichiara che linee/testo/parametri non sono rieseguiti in quelle
corse: restano evidenze separate della r1, che non diventa un PASS globale.
Gli errori di avvio degli script (argomenti batch Unity, lettura BOM XML e
percorso dell'eseguibile) sono conservati e non contati come errori CAD.

## Limiti

Modifica delle spline esistenti, punti/maniglie/tensione nell'editor, tutte
le opzioni/default desktop, dipendenze e lifecycle completi restano separati.
Il simulatore non certifica ergonomia, tracking/controller reali o GPU Quest.
