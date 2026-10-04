# Inventor XR SO — M1 + M2

Client Quest 3 per visualizzare il documento attivo di Inventor a scala 1:1, selezionare componenti e facce e sincronizzare l'highlight. M1 è in sola lettura CAD: nessuna modifica a geometria o parametri.

M2 aggiunge l'ambiente di ispezione: Browser gerarchico, breadcrumb, proprietà CAD, misure, sezione, scale e manipolazione visuale. Anche M2 non modifica geometria o parametri. Stato delle verifiche e limiti: [collaudo M2](../docs/xr-m2-verification.md).

Riferimenti: [spec prodotto](inventor_meta_product.md), [design M1](../docs/superpowers/specs/2026-09-26-inventor-xr-so-m1-design.md), [piano C1–C9](../docs/superpowers/plans/2026-09-26-inventor-xr-so-m1.md), [verifica e collaudo](../docs/xr-m1-verification.md).

## Prerequisiti

- Unity **6000.6.3f1** con Android Build Support, SDK, NDK e OpenJDK; pacchetti fissati in `Packages/manifest.json`.
- Quest 3 in modalità sviluppatore, debug USB autorizzato e PC sulla stessa LAN.
- Horizon OS v74+ e permessi fotocamera per la scansione QR. Il percorso manuale resta disponibile senza fotocamera.
- Inventor 2027 con add-in sperimentale abilitato (`INVENTOR_SO_EXPERIMENTAL=1`) e server HTTP con `--enable-experimental`.
- Porta TCP 8443 raggiungibile dal visore sulla rete privata. L'app usa HTTPS con certificato pinnato.

## Struttura

- `Packages/com.occhipinti.inventorxrso.core`: MCP, pairing, GLB, cache, sessione e selezione senza Unity.
- `Assets/XrSo/Runtime`: UnityWebRequest, mesh, highlight, credenziali e UI italiana.
- `Assets/XrSo/Xr`: rig Meta, ray controller, MR/Studio VR, fotocamera e bootstrap applicativo.
- `Assets/XrSo/Scenes/Main.unity`: scena generata e cablata.
- `Assets/Plugins`: vault Android Keystore AES-GCM e ZXing.Net 0.16.9 (Apache-2.0).
- `Tests~`: test .NET e server HTTPS con FakeAddIn.
- `Tools~/Invoke-Unity.ps1`: esecuzione Unity batch con attesa del processo.

## Test e build

Comandi dalla radice del repository. Chiudere l'Editor di questo progetto prima dei comandi batch.

```powershell
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
dotnet build "Inventor XR SO/Tests~/XrSo.TestHost"
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-runTests","-testPlatform","EditMode","-testResults","`"$env:TEMP\xrso-editmode.xml`""
& "Inventor XR SO/Tools~/Invoke-Unity.ps1" -Arguments "-executeMethod","InventorXrSo.Editor.XrSoBuild.BuildApkBatch" -Log "$env:TEMP\xrso-build.log"
```

APK: `Builds/InventorXrSo.apk`. Target Android ARM64, IL2CPP, Vulkan, Linear, API minima 32.

Il menu **Configure Project** (batch: `InventorXrSo.Editor.XrSoProjectSetup.ConfigureBatch`)
assegna e abilita il loader Oculus per Android. La build si interrompe se il
loader manca: senza questa assegnazione Unity esclude le librerie native XR.

La scena è riproducibile con il menu **Inventor XR SO → Build Main Scene** o `InventorXrSo.Editor.XrSoSceneBuilder.BuildBatch`. Questo comando ricrea `Main.unity`: salvare altrove eventuali personalizzazioni prima di rigenerarla. Configura passthrough, permessi fotocamera, materiali e build scene tramite le API del Meta SDK installato.

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.6.3f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
& $adb devices
& $adb install -r "Inventor XR SO/Builds/InventorXrSo.apk"
```

## Primo collegamento

Con il nuovo pacchetto Windows, aprire **Inventor SO → Connessione visore** dal
menu Start oppure **Inventor SO → Associa visore** nella scheda dell'add-in.
Selezionare l'indirizzo della rete condivisa con il Quest e premere **Genera codice**:
la finestra mostra QR, codice manuale, IP con porta, impronta e scadenza. È possibile
rigenerare il codice senza riavviare il server. Alla chiusura della finestra il pairing
viene annullato, mentre il server continua a servire i dispositivi già associati.
Il server richiede ASP.NET Core Runtime .NET 8 x64; la finestra è distribuita con
il proprio runtime. Questa nuova UI ha verifiche automatiche Windows; collaudo
fisico Quest e ribbon live sono registrati come aperti nel
[verbale pairing Windows](../docs/pairing-windows-verification.md).

Il percorso da console rimane disponibile per sviluppo e collaudo:

Senza Inventor, per il collaudo del client:

```powershell
dotnet run --project "Inventor XR SO/Tests~/XrSo.TestHost" -- --lan --churn 20
```

Con più schede di rete, aggiungere `--pair-host <IP-LAN-del-PC>` per includere
nel QR l'indirizzo raggiungibile dal Quest.

Con Inventor e un documento aperto:

```powershell
dotnet bridge/src/server-http/bin/Debug/net8.0/Inventor.So.Mcp.Http.dll --http-urls https://0.0.0.0:8443 --http-self-signed --http-token-file "$env:LOCALAPPDATA\InventorSO\inventor-so-mcp\http\tokens.txt" --pair quest3 --enable-experimental
```

Sul visore scegliere **Scansiona QR**, oppure **Inserisci codice**: inserire l'indirizzo del PC, confrontare l'impronta del certificato con il PC, confermare e digitare le sei cifre. Il codice dura due minuti ed è monouso; generarne uno nuovo se scaduto.

Home mostra PC, versioni, documento e stato connessione. Scegliere MR o Studio VR. Trigger destro su un componente: selezione del figlio diretto del contesto attuale. Doppia azione rapida, oppure **Apri** nel Browser, entra nel contesto; nel contesto di una parte si selezionano le facce. Nei documenti part si selezionano subito le facce. Trigger nel vuoto: cancella selezione. Pulsante menu: Home. **Dimentica PC** cancella le credenziali locali.

Anche menu e tastiera usano il grilletto destro: il raggio termina sul pannello
e il tasto puntato diventa azzurro.

Il modello resta visibile offline con selezione remota disabilitata. La sessione si riconnette con backoff, riusa gli asset invariati e segue il documento attivo. Un token revocato o un certificato cambiato richiede un nuovo pairing.

## Uso M2

- **Polso sinistro → Ispeziona / Browser**: pannelli nascosti fino al richiamo. **Pin pannello** mantiene la posizione anche dopo chiusura e riapertura. Grip destro puntando il pannello lo riposiziona.
- **Browser**: nomi = selezione; **Apri** = contesto di ispezione locale; **Indietro** e breadcrumb = livelli superiori. **Documenti aperti** attiva esplicitamente una parte o un assieme già aperto sul PC. Non apre file, non salva e non entra in modifica CAD.
- **Proprietà / Dettagli**: massa in kg, materiale, area, volume, vincoli e DOF. Il trattino indica un dato non disponibile. Le proprietà riguardano il componente selezionato o il contesto corrente, non la singola faccia. Revisione e identità documento sono controllate dal backend.
- **Misura**: due trigger su punti della mesh; distanza in mm CAD anche a scala ridotta. Il simbolo **≈** identifica la misura sulla tessellazione. **Pin misura** mantiene fino a 20 quote sul modello. Aggiornamenti della scena rimuovono tutte le quote per evitare riferimenti obsoleti.
- **Sezione**: attivare il piano; con lo strumento Sezione selezionato, puntarlo e tenere Grip per traslare e ruotare. Offset in mm e angolo Y possono essere digitati. La sezione è visuale e non chiude con nuove superfici il taglio.
- **Scala**: 1:1, Fit to room con spazio disponibile impostato dall'utente (default 2 m), Table scale entro 60 cm. Nessuna riduzione automatica. **Porta davanti a me** riposiziona l'intero modello. Grip sul modello muove soltanto la rappresentazione XR.
- **MR / Studio VR** si cambia dal pannello Ispeziona, mantenendo lo stesso modello e workflow.
- Offline rimangono disponibili scala, misure, sezione e navigazione locale; proprietà aggiornate, selezione remota e cambio documento richiedono la connessione.

Per i nuovi dati CAD occorrono server e add-in ricompilati con il codice M2 e tier sperimentale abilitato. I nuovi tool sono `inventor_inspect_xr` e `inventor_activate_open_document_xr`. Non basta aggiornare l'APK. Il menu **Upgrade M2 Materials** aggiorna i due materiali CAD al nuovo shader di sezione senza rigenerare la scena; la build distribuita include già i materiali aggiornati.

## Uso M3 — collaudo completato

- **Polso → Design** richiede una parte attiva in Inventor. Dal Browser si può
  attivare una parte già aperta. Grip sposta la rappresentazione senza modificare
  il CAD.
- **Crea schizzo** propone piani di lavoro e faccia piana selezionata. Linea,
  rettangolo e cerchio usano due pressioni del Trigger oppure coordinate numeriche
  in millimetri. A blocca lo snap candidato.
- **Quota geometria**: selezionare la geometria della bozza, posizionare il testo
  sul piano e inserire il valore. La quota diventa persistente con Applica.
- **Anteprima** calcola il risultato in Inventor e annulla la transazione di prova.
  Risultato solido, originale trasparente. **Applica** richiede un'anteprima valida
  visualizzata; **Annulla comando** scarta la bozza. Dettagli mostra gli errori.
- **Vincoli della geometria / Mostra tutti i vincoli** leggono l'anteprima valida.
  Dopo una modifica alla bozza occorre una nuova anteprima.
- **Aggiungi vincolo** propone tipo, linee/cerchi/lati di rettangolo e
  **Conferma nella bozza**; seguono Anteprima e Applica. Simmetria richiede una
  terza linea come asse. È disponibile la rimozione dell'ultimo vincolo.
  Il pacchetto aggiornato è installato; verifiche native e collaudo guidato nel
  visore sono registrati nel [collaudo M3](../docs/xr-m3-quest-collaudo.md).
- **Estrusione**: schizzo, distanza, direzione e operazione. **Foro**: faccia,
  centro, diametro e profondità/passante. **Raccordo / Smusso**: spigoli e dimensione.
  Grip + Trigger sul manipolatore cambia la dimensione; al rilascio viene richiesta
  l'anteprima esatta.
- **Parametri** modifica anche quote esistenti tramite Anteprima/Applica.
  **Annulla/Ripeti modifica XR** opera sulla cronologia verificata del client;
  modifiche desktop intermedie la invalidano. Una nuova anteprima cancella il
  Ripeti nativo di Inventor.
- Cambi documento/revisione e perdita di connessione invalidano Applica. Se l'esito
  è incerto, controllare Inventor prima di **Ho controllato il CAD**. Il client non
  ripete automaticamente la modifica.

Occorrono APK, server e add-in M3 aggiornati insieme, con modalità sperimentale
abilitata. La [verifica M3](../docs/xr-m3-verification.md) distingue test automatici,
prove native Inventor e accettazione Quest completata il 2026-09-27.

## Assembly M4

- **Polso → Assembly** apre gli strumenti dell'assieme attivo. Scegli un
  componente dalla scena o dall'elenco; i manipolatori mostrano gli assi DOF
  restituiti da Inventor. Grounded, DOF sconosciuti, componenti soppressi,
  adattivi, virtuali e sottoassiemi flessibili non abilitano CAD Move.
- Grip muove soltanto la vista. **CAD Move → Grip + Trigger sul manipolatore**
  modifica una bozza lungo un asse consentito; il rilascio calcola l'anteprima.
  Spostamento/angolo precisi usano mm/gradi indipendenti dalla scala XR.
- Seleziona facce/spigoli **A e B** su componenti distinti. I colori ciano e giallo
  identificano i riferimenti. Il menu contestuale propone i vincoli compatibili:
  Mate, Flush, Mate Axis, Insert, Angle e Tangent. I joint disponibili sono Rigid,
  Rotational, Slider, Cylindrical, Planar e Ball. Ball richiede gap zero.
- **Anteprima → Applica** verifica rebuild, salute delle relazioni, interferenza
  e l'eventuale clearance. La preview comprende l'assieme e viene catturata prima
  del rollback. Annulla scarta la bozza; Undo/Redo usa la cronologia del client.
- Per modificare i figli di un sottoassieme, scegli **Attiva questo assieme**.
  La modifica della definizione riguarda tutte le sue istanze. I figli annidati
  non vengono modificati implicitamente dal contesto del parent.
- Un esito incerto blocca anche l'ingresso in Design finché il CAD non viene
  controllato. Cambio revisione, disconnessione, perdita tracking e ricostruzione
  della scena richiedono una nuova preview prima di Applica.

Richiede APK, server e add-in M4 sperimentali aggiornati insieme. Stato delle
prove e limiti di accettazione nella [verifica M4](../docs/xr-m4-verification.md),
con collegamento alla specifica e ai probe riproducibili.

## Limiti attuali

Massimo 200 definizioni, con avviso sui componenti omessi; mesh oltre il limite backend sono omesse. Nessuna voce; modifica CAD disponibile in M3, con collaudo completato. Le misure M2 coprono la distanza tra due punti sulla mesh: angoli, raggi e distanze minime esatte B-Rep restano estensioni successive. Fit to room usa lo spazio impostato, senza scansione della stanza. Il Browser segue il scene graph (documenti/occurrence), non espone ancora l'albero completo delle feature. Le credenziali sono cifrate con Android Keystore sul visore; nell'Editor sono un file di sviluppo in chiaro sotto `Application.persistentDataPath`.

La scansione mantiene il percorso WebCamTexture del piano M1, con entrambi i permessi CAMERA e HEADSET_CAMERA; non aggiunge MRUK. La [guida Meta alla migrazione](https://developers.meta.com/horizon/documentation/unity/unity-pca-migration-from-webcamtexture/) descrive anche il percorso MRUK, che richiede il solo HEADSET_CAMERA. Fotocamera, pairing e riutilizzo delle credenziali Keystore sono stati verificati sul Quest 3; gli esiti e le verifiche residue sono nel [verbale di collaudo](../docs/xr-m1-verification.md).
## M8 grapics

Tema HIVE/EnerBot applicato ai primitivi uGUI/TMP, Home, voce e workspace:
Satoshi Medium/Bold locale, superfici navy/teal, focus e CTA gialli, card chiare
per il contesto. Font ufficiali e FFL in `Assets/XrSo/Ui/Fonts`, asset locali
in `Assets/XrSo/Ui/Resources`; nessun caricamento remoto di CSS o font.
Il tema Windows di pairing usa gli stessi riferimenti. [Spec M8](../docs/superpowers/specs/2026-10-04-m8-grapics-design.md)
e [verifica](../docs/xr-m8-verification.md): software in verifica, collaudo Quest
e fisico aperto. Il runner M8 usa la fixture dedicata M6 attraverso
`scripts/run-m8-acceptance.ps1` dalla radice del repository.
