# Quattro esperienze XR — verifica F0–F4

25 ottobre 2026, Europe/Rome. Branch: `nuova_ux`.
Perimetro: prima consegna del [piano](superpowers/plans/2026-10-25-quattro-esperienze-xr.md), F0–F4.

## Implementazione software

- **F0:** [matrice baseline](qx-f0-matrice.md), inventario degli ID e dei percorsi input, esiti Core/EditMode e disponibilità dispositivo. Nessun gate storico promosso.
- **F1:** Home con quattro schede e descrizioni, `ActivityController` Core separato da contesto documento/connessione/ambiente, scelta ricordata durante pairing. Il router aggiorna M9 in ogni attività e apre authoring solo in Disegno.
- **F2:** politica client applicata a palette, anello, Find/TryInvoke, voce, dispatcher e confine MCP. Le azioni catturate prima del cambio attività sono invalidate anche tornando nella stessa attività. Home mantiene sessione e documento; Connessione è distinta. Salva è etichettato «Salva dal desktop» e resta solo in Disegno.
- **F3:** provider Presentazione composto sui servizi Inspect condivisi: misure locali/pin, scala e posizionamento dell’intero modello, MR/VR, adatta e ricentra. Selezione locale anche offline. Nessuna sezione tecnica, analisi M7 o authoring; nessun movimento fittizio.
- **F4:** provider Verifica composto sugli stessi servizi: browser locale anche offline, contesti locali, proprietà online, sezione, visibilità e analisi M7. Restano invarianti revisione, ambito, scope, focus e invalidazione dei risultati.
- Uscita protetta per bozze/editor nativo, comandi armati e commit da verificare. Capture impedisce uscita durante acquisizione/pausa, upload e operazioni review/CAD in corso. Un riferimento con esito incerto conserva la guardia attraverso Home/review e richiede «Ho controllato il CAD», senza retry o salvataggio automatici.
- Runner M1/M2 migrati in Verifica; M3/M4/M5/M6/M9/S1 in Disegno (M8/M10 ereditano M6). M7 seleziona dal browser di Verifica. Nuovo runner QX sulla fixture dedicata M7 per Home/Presentazione/Verifica e controllo della revisione CAD invariata.

## Evidenze automatiche

| Livello | Baseline | Esito della tranche |
|---|---|---|
| .NET Core + integrazioni HTTPS/FakeAddIn | 1.337 PASS | 1.355 PASS; nessun errore o skip |
| Unity EditMode | 1.146 PASS | 1.159 PASS; nessun errore o skip |
| Android QA con `XR_SO_ACCEPTANCE` | non ricostruito per F0 | **PASS**, APK Development ARM64, 95.027.483 byte |
| Android ordinario, senza runner | non ricostruito per F0 | **PASS**, APK release ARM64, 57.141.704 byte |
| Quest sintetico su fixture Inventor | nessun dispositivo collegato | **NOT COVERED** |
| Meta XR Simulator desktop + HTTPS FakeAddIn | aggiunto dopo la tranche | **PASS sintetico**, 31 controlli e cinque screenshot; [verbale](xr-meta-simulator-verification.md) |
| Meta XR Simulator desktop + Inventor 2027 nativo | estensione del collaudo | **PASS sintetico**, 49 controlli, otto screenshot, interferenza/distanza/salute/BOM; fixture e revisione invariati, ripristino riuscito; [verbale](xr-meta-simulator-verification.md) |
| Prova fisica persona/controller | non eseguita | **NOT COVERED** |

Ispezione dei pacchetti: libreria `lib/arm64-v8a/libil2cpp.so` presente in entrambi; metadati `QxQuestAcceptance` presenti soltanto nel QA. Build conclusa con exit code 0. Hash SHA-256 completi in `manifest.json` e `package-inspection.json`.

Log, XML/TRX e APK sono conservati in `artifacts/qx-f0-f4/` (cartella ignorata da Git). Le prime iterazioni Unity hanno rilevato e corretto aspettative Esplora online/Salva, registrazione duplicata dei servizi composti e testo della guardia Capture; gli esiti finali qui sopra fanno riferimento soltanto all’ultima suite.

## Gate QX pertinenti

| Gate | Copertura software di questa tranche | Collaudo che rimane aperto |
|---|---|---|
| QX-01 | quattro ingressi Home non associata/associata; MR/VR indipendente | runner Quest e leggibilità |
| QX-02 | router con desktop change e Sync senza authoring fuori Disegno | cambio documento/riconnessione sulla fixture reale |
| QX-03 | transizioni protette, bozza, commit incerto, Capture e guardia reference | acquisizione/upload/COM/tracking reali |
| QX-04 | Find, palette, ring, voce e azioni obsolete; MCP spy; chiamate dirette preview/history | runner QX + audit/revisione nativa e comandi fisici |
| QX-05 | suite esistenti Core/EditMode; runner migrati e compilati nella build QA | riesecuzione dei runner storici per i sottocasi |
| QX-06 | misure/scala Presentazione e browser/sezione Verifica offline; reset al cambio attività | runner Quest e controllo fisico |
| QX-07 | regressioni M7 esistenti, provider dedicato; runner M7 migrato; analisi native M7 attraversate in Meta XR Simulator | M7 completo su Quest e prova fisica; scope/stale/focus non esercitati dal nuovo smoke desktop |
| QX-15/16 | nessuna prestazione o ergonomia certificata dai test PC | benchmark/leggibilità/comprensione con Quest e persona |

QX-08–14 non sono consegnati da F0–F4: esploso/smontaggio (F5), passaggio Capture guidato (F6), pose e movimenti (F7). F8 conserva l’accettazione complessiva. Nessun gate M1–M10, S1 o RC viene chiuso da questo verbale.

## Riproduzione

```powershell
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","C:/Users/salva/AppData/Local/Temp/qx-editmode.xml"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoBuild.BuildQxApksBatch","-qxOutput","Z:/SoftwareOcchipinti/Progetti Software/Trading bot test/AstraBot/artifacts/qx-f0-f4"
```

Il runner QX è compilato soltanto nell’APK QA e attivato dall’intent `xr_qx_acceptance`; accetta esclusivamente la fixture con prefisso `XR_M7_Quest_Acceptance`. Il wrapper M7 esistente resta il percorso per preparare/ripristinare la fixture e raccogliere le analisi. Non installare un APK QA su documenti dell’utente; ripristinare APK ordinario e documento precedente come richiesto dai protocolli esistenti.

Non sono necessari nuovi pacchetti, riferimenti Inspector o modifiche manuali della scena generata. La composizione avviene in AppController.
