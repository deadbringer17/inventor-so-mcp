# Meta XR Simulator — verifica desktop

25 ottobre 2026, branch `nuova_ux`. Runtime Meta XR Simulator **207.1**, Unity
**6000.6.3f1**, OpenXR **1.18.0**, profilo Meta Quest 3. Nessun visore collegato.

## Esito osservato

`artifacts/meta-xr-simulator/run06/result.json` (confermato anche da run05):
**PASS_SYNTHETIC_META_XR_SIMULATOR**, 31 controlli, cinque screenshot della scena
Main effettivamente renderizzata in Play Mode, uscita con codice 0.
Il runtime OpenXR e il dispositivo testa sono attivi; una precedente interrogazione
CLI nella stessa sessione riporta Meta Quest 3, 72 Hz, testa e due controller attivi.
Il CAD è esclusivamente **HTTPS FakeAddIn**, documento `Fake.iam`.

| Percorso | Osservato nel simulatore |
|---|---|
| Home | connessione senza ingresso automatico in authoring; quattro schede |
| Presentazione | authoring e analisi tecniche bloccati; preview CAD diretta rifiutata prima di HTTP |
| Misura | due punti sintetici a 20 mm CAD, misura mantenuta e comando scala tavolo |
| Verifica | callback Presentazione obsoleta rifiutata; sezione e browser disponibili |
| Disegno | ingresso nel workspace Assieme e ritorno a Home |
| Capture | CAD nascosto; ritorno a Home prima di registrare |
| Sessione | connessione conservata e revisione della fixture invariata |

Gli ingressi sono callback UI/catalogo e punti di misura iniettati dal runner.
Non sono click, gesti, voce o raycast fisici dei controller. Gli screenshot sono
acquisiti dalla Game view; la vista polso può essere fuori dall'inquadratura con
la posa iniziale dei controller simulati.
Nello screenshot Disegno la fixture cubica occupa quasi tutta la vista: quella
immagine non dimostra la leggibilità dei pannelli di authoring. L'attivazione
del workspace Assieme è verificata dallo stato runtime.

Il ciclo ha rilevato e permesso di correggere eccezioni di cleanup su sezione e
cursore già distrutti. Il runner controlla anche le eccezioni dell'app durante
l'uscita da Play Mode e rimuove il marker PASS in caso di errore. Run05 non ne
riporta; run06 conferma l'esito con il wrapper definitivo. Il log Unity contiene ancora diagnostica nativa `JobTempAlloc` alla
chiusura dell'Editor: origine non isolata, nessuna prestazione certificata.
Le descrizioni Home sono state ingrandite dopo l'ispezione delle immagini.

Regressione Unity EditMode: **1.160 PASS, zero errori e zero skip**, target
Android, assembly `InventorXrSo.Tests.EditMode`; include il nuovo caso in cui il
renderer del cursore viene distrutto prima del cleanup dell'editor nativo.
XML e log: `artifacts/meta-xr-simulator/editmode-diagnostic.{xml,log}`.
È stato usato un logger temporaneo dei singoli casi, rimosso al termine.
I tentativi batch precedenti sono stati interrotti senza un esito di suite e
non costituiscono prove di regressione; l'esecuzione completa citata è l'unica
usata per il conteggio finale. I 1.355 test Core e le build APK rimangono le
evidenze della precedente tranche F0–F4, prima dell'aggiunta del simulatore.

## Riproduzione

Requisiti: Unity 6000.6.3f1, .NET 8 SDK, Meta CLI `metavr` nel PATH e runtime
[Meta XR Simulator Windows](https://developers.meta.com/vr/downloads/package/meta-xr-simulator-windows/).
Chiudere l'Editor di questo progetto prima di avviare il wrapper.

```powershell
& 'Inventor XR SO/Tools~/Invoke-Simulator.ps1' -Output 'artifacts/meta-xr-simulator/nuova-prova'
```

Il wrapper compila TestHost, avvia il frontend Meta, configura Standalone OpenXR,
apre Main in un Editor con Game view attiva ed esegue il percorso automatico.
Produce `result.json`, `pass.marker`, `unity.log`, log di build/avvio e cinque PNG;
restituisce codice 0 solo quando il marker finale è presente. `-SetupOnly` configura
il profilo senza eseguire il percorso. `-RuntimeManifest` accetta il percorso assoluto
di `meta_openxr_simulator.json` per un'installazione diversa.

La configurazione avviene tramite API Editor. Android mantiene Oculus; Standalone
usa OpenXR con profili Touch/Touch Plus. `XR_RUNTIME_JSON` ha ambito processo e viene
ripristinato; il runtime OpenXR di sistema non viene sostituito. Le credenziali della
fixture sono in memoria, senza leggere o sovrascrivere il pairing reale. Il frontend
Meta resta aperto al termine; il processo Unity della prova termina da solo.
Per i comandi batch `-runTests`, il bootstrap Editor disabilita in memoria
l'avvio automatico XR desktop a ogni domain reload, senza salvare l'override.
I test di lifecycle possono così entrare in Play Mode senza attendere frame XR
o un visore. La prova del simulatore usa invece l'Editor con rendering attivo.

Regressione del progetto (selezione esplicita dell'assembly, esclusi i test dei pacchetti):

```powershell
& 'Inventor XR SO/Tools~/Invoke-Unity.ps1' -Arguments '-buildTarget','Android','-runTests','-testPlatform','EditMode','-assemblyNames','InventorXrSo.Tests.EditMode','-testResults','C:/Temp/xrso-editmode.xml'
```

Su questo PC l'installer per utente ha fallito con MSI 1603/1406 scrivendo HKLM.
È stato usato il pacchetto ufficiale firmato Meta, estratto amministrativamente
senza registrazione globale, in
`%LOCALAPPDATA%/MetaXRSimulatorPortable/PFiles/MetaXRSimulator/v207.1`.
Il wrapper riconosce prima un'installazione registrata, poi questo percorso locale.
La CLI può quindi indicare il tool non installato anche se il frontend e il runtime
portatile funzionano. Il modo batch Unity è stato scartato perché non produceva
frame XR; il wrapper usa l'Editor con Game view.

## Limiti e gate aperti

Questa prova aggiunge copertura d'integrazione desktop ai test Core/EditMode di
[F0–F4](xr-quattro-esperienze-verification.md). Restano **NOT COVERED**: Inventor
COM reale, APK/Android/Keystore, fotocamere e acquisizione, microfono, tracking e
controller fisici, ergonomia/leggibilità nel visore e prestazioni GPU Quest.
Non chiude i gate Quest QX o quelli storici delle milestone. Meta XR Simulator
fornisce un runtime XR desktop, non esegue l'APK come un emulatore Android.

Riferimenti ufficiali: [guida Unity](https://developers.meta.com/vr/documentation/unity/unity-simulate-xrsim/),
[introduzione](https://developers.meta.com/vr/documentation/unity/xrsim-intro/),
[Meta CLI](https://developers.meta.com/vr/essentials/metavr-environment/).
