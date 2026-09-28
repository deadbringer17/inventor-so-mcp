# Inventor XR SO — verifica M2

Data: 27 settembre 2026. Riferimento: specifica prodotto §53.

## Implementato

Inspect locale con menu sul controller sinistro, pannello riposizionabile e pinnabile, Browser paginato della gerarchia documenti/occurrence e breadcrumb cliccabile. La selezione rispetta il contesto: assieme → occurrence, parte → faccia. Doppia azione rapida oppure Apri entra nel contesto locale. Le proprietà del componente vengono lette dal CAD e mostrano esplicitamente valori non disponibili.

Nuovi tool sperimentali: `inventor_inspect_xr` verifica documento e revisione prima di leggere massa, materiale, area, volume, vincoli e DOF; `inventor_activate_open_document_xr` attiva soltanto documenti già aperti e rifiuta transazioni in corso. L'elenco documenti ora restituisce ID stabili. Nessun comando M2 salva, ricostruisce o modifica il modello CAD.

Misure tra due punti sulla mesh in millimetri del modello, con pin esplicito (20 massimo). Sezione con piano manipolabile, offset/angolo numerici e shader URP stereo; le superfici eliminate dal piano sono escluse dalla selezione. Scala 1:1, Fit to room su estensione impostata dall'utente, Table scale entro 60 cm; grab globale visuale; cambio MR/Studio durante la sessione.

Cambio scena/revisione: annullamento richieste, selezione e misure invalidate, sezione azzerata. Risposte ritardate non aggiornano un nuovo contesto. Offline: modello e strumenti geometrici locali disponibili; dati CAD invalidati e azioni remote disabilitate.

## Evidenza automatica

- Add-in Inventor 2027 sperimentale: compilato contro l'interop installato, zero errori e warning.
- Client .NET: **109/109** test passati, incluse chiamate M2 sul server HTTPS/MCP reale con FakeAddIn, rigetto di revisioni obsolete e documento diverso, navigazione e cambio documento, revisione delle proprietà senza ricaricare la mesh.
- Backend: **895/895** test passati. I contratti includono i due nuovi tool, con registrazione e mapping verificati.
- Unity EditMode: **41/41** test passati nella verifica finale: scala e unità CAD, piano di sezione, misure/pin, layout dei pannelli, posizione pinnata, invalidazione di risposte obsolete e aggiornamenti di sola revisione.
- Build Android ARM64/IL2CPP/Vulkan: **riuscita**. APK `Inventor XR SO/Builds/InventorXrSo.apk`, 50.806.366 byte; presenti `libil2cpp.so`, `libunity.so` e `libOVRPlugin.so`. Shader CAD e highlight compilati per Vulkan senza errori.

SHA-256 APK iniziale: `188CBD11BDCC91096EA00CAEEBCDE0EA1AABF8C6349C3BD71B5A0FF2D04218A1`.

APK attuale installato durante il collaudo: 50.806.254 byte, SHA-256 `1F2C689BA4CC466E02AF124D07B7C90FC9A89EA26DE51227DA6D4422FFEBD9CA`. Build riuscita, log `%TEMP%/xrso-document-debug-build.log`; aggiunge log delle transizioni di sessione/documento e rimuove la diagnostica ripetuta del controller. I risultati dei test sopra si riferiscono alla build funzionale iniziale.

Log e risultati locali: `artifacts/m2-verification/m2-backend.trx`, `m2-editmode.xml`, `m2-editmode.log`, `m2-build.log`. Le modifiche automatiche ai file GraphicsSettings, InputManager e QualitySettings prodotte dall'Editor sono state rimosse: M2 conserva le impostazioni preesistenti e cambia soltanto i materiali CAD necessari alla sezione.

I test con FakeAddIn verificano trasporto e client, non l'esecuzione COM di Inventor. Per il successivo collaudo live sono state installate e avviate le nuove versioni di server/add-in, come descritto sotto.

Comandi riproducibili dalla radice repository (Editor chiuso):

```powershell
dotnet build bridge/src/plugin-so27/Inventor.So.AddIn.csproj -p:SoExperimental=true --no-restore
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests" --artifacts-path "Inventor XR SO/Tests~/artifacts/m2"
$env:INVENTOR_SO_EXPERIMENTAL='0' # processo di test; i test verificano anche il default disabilitato
dotnet test bridge/tests/Bimwright.Ipt.Tests -p:OutputPath=bin/M2/net8.0/ --no-restore
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-m2-editmode.xml`"" -Log "$env:TEMP\xrso-m2-tests.log"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoBuild.BuildApkBatch" -Log "$env:TEMP\xrso-m2-build.log"
```

Gli output .NET isolati evitano di sovrascrivere DLL caricate dal server attuale. Per riavviare il server/add-in sperimentale, riabilitare `INVENTOR_SO_EXPERIMENTAL=1` nel loro processo.

## Collaudo Quest + Inventor

### Sessione guidata avviata il 27 settembre 2026

- APK M2 installato su Quest 3 via `adb install -r`: Success.
- Pacchetto sperimentale `artifacts/inventor-so-mcp-20260927-125431` compilato e add-in installato in una nuova cartella versionata.
- Utente ha confermato di avere salvato; Inventor chiuso normalmente e riavviato con add-in M2 (PID 22556).
- Host HTTPS M2 avviato sulla stessa porta 8443, conservando certificato e registro token del collaudo precedente. Client Quest collegato: richieste MCP HTTP 200 osservate.
- Aperto l'assieme di prova `XR M1 Assembly 20260927 0830.iam`: nessun riferimento mancante, `requires_update=false`; Inventor lo segnala dirty dopo l'apertura, non è stato salvato dal collaudo.
- App avviata sul Quest; nessuna delle eccezioni runtime cercate nel log iniziale. Esiti delle prove riportati sotto.
- Primo collaudo bloccato dal raggio destro assente. Screenshot Home conferma PC connesso e documento disponibile. Build diagnostica installata: alle 13:04:07 del log Quest, `OVRInput.GetConnectedControllers()` e `OVRPlugin.GetConnectedControllers()` restituiscono entrambi `LTouch`; tracking destro falso in entrambe le API, sinistro vero, riferimento LineRenderer presente, app in focus. La mancata disponibilità del destro è osservata anche dal runtime Meta; causa hardware/runtime ancora da isolare. Richiesta riattivazione del controller all'utente. M2 non ancora accettato su dispositivo.

### Ambiti della checklist estesa (non tutti verificati)

- Collegamento ai nuovi tool reali; confronto massa/materiale/DOF con Inventor, incluse occurrence annidate.
- Comfort/leggibilità di polso, badge, breadcrumb e pannelli; pin e grab con Touch Plus.
- Confronto di una distanza nota a 1:1 e Table scale; quote visibili e rimosse al cambio revisione.
- Sezione in entrambi gli occhi; manipolazione e valori numerici; nessuna selezione di triangoli rimossi dal piano.
- Cambio documento da PC e Browser, perdita rete durante richieste, riconnessione.
- MR e Studio su dispositivo, prestazioni di assiemi rappresentativi.

### Esiti del collaudo guidato

- Controller destro riattivato dall'utente; raggio ora funzionante. Diagnostica successiva: entrambi `Touch`, tracking destro e sinistro validi. Rimossi dal sorgente i log temporanei ripetuti del controller.
- Utente conferma ingresso, selezione, menu sinistro, pannello Ispeziona e proprietà.
- Browser raggiungibile e componenti visibili; chiarito che aprire il contesto non isola il componente. Navigazione del breadcrumb non confermata separatamente.
- Misura visibile; chiarita assenza di snap geometrico. Utente conferma misura fissata invariata nel passaggio Table → 1:1 su un modello grande.
- Sezione, ricentraggio, passaggio MR/Studio e spostamento del pannello confermati dall'utente.
- Table/Fit riducono soltanto: con modello sotto 60 cm nessuna variazione è prevista. Utente conferma riduzione Table e ritorno a 1:1 su un modello grande; Fit to room non confermato separatamente.
- Cambio documento: al primo tentativo il ritorno componente → assieme non seguiva Inventor. Backend riportava l'assieme di prova attivo; screenshot Quest mostrava Assieme3.iam con una coclea. Dopo installazione della build con log delle transizioni di sessione e riavvio dell'app, utente conferma il passaggio nei due sensi. Il difetto precedente non è stato isolato né corretto: resta un'anomalia intermittente da riprodurre.
- Utente conferma perdita Wi-Fi, permanenza del modello offline, riconnessione senza nuovo pairing e lettura delle proprietà dopo il ripristino.

Esito: flussi principali provati sul Quest con riscontro dell'utente. Non equivale a una certificazione completa: restano confronto quantitativo delle proprietà con Inventor, distanza nota, breadcrumb e attivazione documento dal Browser, Fit to room, valori numerici della sezione, esclusione delle facce tagliate dalla selezione e misure di prestazione. Il precedente blocco del cambio documento rimane aperto.

## Limiti deliberati

Misura approssimata sulla mesh, senza precisione B-Rep certificata; per ora distanza punto-punto. Sezione senza superfici di chiusura: non crea corpi tagliati. Fit to room usa un'estensione impostata, non dati del Guardian. Il Browser espone il scene graph, non feature e schizzi interni alla parte. Posizioni e misure sono di sessione, senza salvataggio tra riavvii. Il flusso resta Touch Plus destro per puntamento e sinistro per il menu; configurazione mancino, near interaction, modifica CAD e voce non vengono aggiunte da M2.
