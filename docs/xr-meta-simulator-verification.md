# Meta XR Simulator — verifica desktop

25 ottobre 2026, branch `nuova_ux`. Runtime Meta XR Simulator **207.1**, Unity
**6000.6.3f1**, OpenXR **1.18.0**, profilo Meta Quest 3. Nessun visore collegato.

## Esito osservato

`artifacts/meta-xr-simulator/run07/result.json` (confermato anche da run05/run06):
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
riporta; run06 e run07 confermano l'esito con i wrapper definitivi. Il log Unity contiene ancora diagnostica nativa `JobTempAlloc` alla
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

## Estensione con Inventor reale

Il run `artifacts/meta-xr-simulator/native04/` (confermato anche da native03) ha completato
**PASS_SYNTHETIC_META_XR_SIMULATOR_NATIVE_CAD**: 49 controlli, otto screenshot,
codice di uscita 0. Unity usa lo stesso runtime Meta; il CAD arriva dall'host HTTPS
di produzione e dall'add-in dentro Inventor 2027. La fixture dedicata è
`XR_M7_Quest_Acceptance.iam`, creata dal programma `QuestAcceptanceFixtures`.

| Controllo aggiuntivo | Risultato nativo |
|---|---|
| Identità | document ID della fixture e quattro occorrenze |
| Interferenza, azione UI Verifica | una coppia M7_A/M7_B, 2.000 mm³ |
| Distanza, due selezioni nel browser | M7_A/M7_C: 30 mm, con estremi restituiti da Inventor |
| Salute, azione UI Verifica | vincolo M7_Sick in errore; solo M7_D non vincolato |
| BOM | avviso DESCRIPTION_MISSING |
| Invarianti | revisione iniziale/finale identica; ispezioni native prima/dopo identiche, dirty=false |
| Ripristino | due documenti fixture chiusi senza salvare; nessun documento precedente da riattivare |

Le otto immagini includono Home, Presentazione, Verifica, Disegno, Capture e
i risultati di interferenza, distanza e salute. La misura locale Presentazione
rimane sintetica; la distanza di 30 mm è invece calcolata da Inventor.
L'ispezione prima/dopo è indipendente da Unity e controlla pose, volume, vincoli
e dirty flag. Il wrapper confronta i due log e considera fallita la corsa se
lo stato cambia o il ripristino non riesce.

Host solo su loopback, porta libera assegnata per la corsa, certificato ECDSA
P-256 e token temporanei; il client conserva il pinning SHA-256. Le credenziali
sono caricate in memoria dal client da un file temporaneo, senza modificare il pairing
persistente. Il `finally` arresta il solo host creato dalla prova ed elimina i
quattro file temporanei di configurazione/credenziali/certificato. I manifest di
fixture sono isolati nella cartella di ogni run. Inventor resta aperto.

I tentativi `native01` e `native02` non sono PASS: il primo si è fermato nella
creazione del certificato, il secondo nella connessione TLS prima di caricare
la scena CAD. Entrambi hanno ripristinato la fixture; i log sono conservati.
Il generatore finale usa curva ed estensioni del certificato compatibili con il
percorso del progetto, senza disabilitare la verifica TLS.

Riproduzione con una sola istanza Inventor 2027 aperta, add-in SO caricato e tier
sperimentale abilitato, ed Editor Unity chiuso:

```powershell
& 'Inventor XR SO/Tools~/Invoke-SimulatorLive.ps1' -Output 'artifacts/meta-xr-simulator/prova-nativa'
```

È un sottocaso nativo di F0–F4/M7 in un simulatore desktop: non è il runner
Android sul Quest né il collaudo completo di M7. Authoring/preview/applica CAD,
stale/scope/focus completi, riconnessione e cambio documento nativi restano
fuori da questa corsa. Non sono stati modificati codice bridge o scena Main.

## Riproduzione FakeAddIn

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

Queste prove aggiungono copertura d'integrazione desktop ai test Core/EditMode di
[F0–F4](xr-quattro-esperienze-verification.md). Il percorso FakeAddIn non copre
COM; quello nativo copre soltanto i sottocasi della tabella precedente.
Restano **NOT COVERED**: authoring/commit CAD nativi, APK/Android/Keystore,
fotocamere e acquisizione, microfono, tracking e
controller fisici, ergonomia/leggibilità nel visore e prestazioni GPU Quest.
Non chiude i gate Quest QX o quelli storici delle milestone. Meta XR Simulator
fornisce un runtime XR desktop, non esegue l'APK come un emulatore Android.

Riferimenti ufficiali: [guida Unity](https://developers.meta.com/vr/documentation/unity/unity-simulate-xrsim/),
[introduzione](https://developers.meta.com/vr/documentation/unity/xrsim-intro/),
[Meta CLI](https://developers.meta.com/vr/essentials/metavr-environment/).
