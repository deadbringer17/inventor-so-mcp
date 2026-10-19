# S1 — Collaudo Quest owned75 e bootstrap77–78

15 ottobre 2026. Rapporto provvisorio fino a `device-r11`: l'incremento75
non è ancora congelato e potrà ricevere ulteriori tentativi.
**R11 termina PARTIAL con22 sottocasi PASS e cleanup verificato. I fallimenti
r6/r8/r9 restano conservati. Tutti i18 gate S1
complessivi restano aperti.**

## Percorso eseguito e build

Il wrapper75 usa un nuovo processo Inventor posseduto dall'host73, fixture
Parte/Lamiera dedicate e un host HTTPS fissato al target esatto. La QA usa
un bootstrap effimero e un tunnel USB `adb reverse`: non riconfigura il
pairing ordinario e non si collega per fallback all'istanza dell'utente.
Manifest, DLL caricata, identità PID/start-time e documenti sono registrati
nelle prove pubbliche dell'host; i contenuti delle credenziali non sono
archiviati. Per il disegno del percorso si vedano
[bootstrap73](s1-isolated-qa-bootstrap73.md) e
[runner74](s1-quest-text-parameters-runner74.md).

Le build QA77 e QA78 sono documentate dai log
[`bootstrap77-apk-build-r1.log`](../artifacts/s1-verification/bootstrap77-apk-build-r1.log)
e [`bootstrap78-apk-build-r1.log`](../artifacts/s1-verification/bootstrap78-apk-build-r1.log).
Gli hash delle APK effettivamente usate sono registrati nei `binaries.json`
dei tentativi, insieme agli hash add-in, host e manifest:

| APK | Dimensione | SHA256 |
|---|---:|---|
| QA77, r5 | 94.154.225 byte | `F704CA944F010CE5B756AE7FAB3CF1D8F5B118E3F540A34F6DC02954CF8786AD` |
| QA78, r6 | 94.167.973 byte | `82E1E8B66BAFAA89F73E920A422D9917D67F1A39BDD0E9A979D008A0710C6093` |
| Ordinaria38 ripristinata | baseline38 | `D8D6D341CCBDCF9BA06FF282908576CA3F5FCFCF5DFFAF6529AA68AC70B04F81` |

Queste sono identità delle APK, non hash equivalenti delle sorgenti.
Le copie delle sorgenti/binari e i relativi inventari restano nei singoli
tentativi. La DLL add-in staged usata da r6 ha SHA256
`29ACD9BCC640847650D4E361737B67C8F19882E814C76FB4F23855CB859A8A50`;
la DLL installata ordinaria è rimasta distinta e invariata. Il successivo
r8 conserva la stessa APK78 e prova invece l'add-in candidato80 con Rebuild,
SHA256 `711D8451C6740F5290A67F48E2F07DD0319534CB226CD4EB83FBD157A39F16C4`:
le due prove non vanno trattate come esecuzioni dello stesso binario add-in.

## Tentativi di dispositivo

Ogni riga rimanda al proprio verbale di ripristino; un arresto prima dei
casi CAD non costituisce un fallimento di un'asserzione CAD del runner.

| Tentativo | Esito osservato | Ambito e causa |
|---|---|---|
| [r1](../artifacts/s1-verification/increment75/device-r1/restoration.json) | `NOT_RUN_DEVICE_ASLEEP`, exit3 | Quest addormentato al preflight; nessuna fixture o caso runner preparato. |
| [r2](../artifacts/s1-verification/increment75/device-r2/restoration.json) | `SETUP_ERROR`, exit3 | Conversione PowerShell della scadenza JSON, nella fase `owned_host_launch`; runner non avviato. Ispezione, stop host e ripristino risultano completati. |
| [r3](../artifacts/s1-verification/increment75/device-r3/restoration.json) | `SETUP_ERROR`, exit3 | Trasferimento bootstrap verso storage app esterno negato da FUSE nel contesto `run-as`; runner non avviato. Il primo cleanup device è registrato false, senza occultarlo; gli altri ripristini registrati passano. |
| [r4](../artifacts/s1-verification/increment75/device-r4/restoration.json) | `NOT_RUN_DEVICE_ASLEEP`, exit3 | Nuovo preflight con Quest addormentato; nessuna fixture preparata. |
| [r5](../artifacts/s1-verification/increment75/device-r5/restoration.json) | Abort esplicito del coordinatore, exit3 | QA77 rifiuta il bootstrap prima del collegamento online: binding JNI `Intent.removeExtra` con tipo di ritorno errato. Bootstrap non consumato. Nessuna asserzione CAD del runner eseguita. |
| [r6](../artifacts/s1-verification/increment75/device-r6/restoration.json) | **FAIL runner, exit1; 12 PASS** | QA78 consuma il bootstrap, esegue linea/testo e parte dei parametri; fallisce il controllo immediato del consumatore testuale dopo il primo Apply a 20 mm. Tutti i cleanup registrati passano. |
| [r7](../artifacts/s1-verification/increment75/device-r7/restoration.json) | `SETUP_ERROR`, exit3 | Nome file QA errato passato dal coordinatore; arresto nella fase `arguments`, prima di installazione, fixture o azioni CAD. Nessun caso runner eseguito. |
| [r8](../artifacts/s1-verification/increment75/device-r8/restoration.json) | **FAIL runner, exit1; 12 PASS** | Stessa QA78 con candidato add-in80/Rebuild: il controllo stretto dopo Apply a 20 mm passa; il passo successivo viene rifiutato perché offline/fuori sessione/identità non attiva. Tutti i cleanup registrati passano. |

In r5 il `FAIL` nel log osservato dal wrapper è stato aggiunto
deliberatamente dal coordinatore per avviare il normale `finally` dopo aver
osservato il rifiuto QA. Il file
[`coordinator-abort.json`](../artifacts/s1-verification/increment75/device-r5/coordinator-abort.json)
lo identifica con `runner_assertion=false`; il testo è conservato in
[`coordinator-abort.txt`](../artifacts/s1-verification/increment75/device-r5/coordinator-abort.txt).
Non va conteggiato come test CAD eseguito. Anche mancato consumo e cattura
compositor fallita rimangono espliciti nel verbale r5.

## Bootstrap negativo e positivo reale

Prima dei casi nativi r6, un bootstrap malformato viene trasferito nella
directory Android privata `getFilesDir` e avviato sulla QA reale. Il file
viene consumato e l'app rifiuta l'avvio nella fase `read_bootstrap`, prima
di creare fixture native o eseguire casi CAD, senza fallback al pairing
ordinario. Esito separato:
[`PASS_INVALID_BOOTSTRAP_REJECTED`](../artifacts/s1-verification/increment75/device-r6/bootstrap-negative.json),
con [log negativo](../artifacts/s1-verification/increment75/device-r6/bootstrap-negative.txt).
Questo PASS di bootstrap non entra nel conteggio dei 12 sottocasi del runner.

Il bootstrap valido r6 viene trasferito solo tramite stdin, nella directory
privata Android con mode `600`; non compare negli argomenti, nei log o nelle
copie delle prove. Il
[`bootstrap-transfer.json`](../artifacts/s1-verification/increment75/device-r6/bootstrap-transfer.json)
registra soltanto metadata di trasferimento. Il verbale finale conferma
`bootstrap_consumed_by_app=true` prima della rimozione di sicurezza del wrapper.
Il percorso privato corregge il precedente tentativo su storage FUSE esterno.

## Dodici PASS e successivo FAIL di r6

Il [log reale r6](../artifacts/s1-verification/increment75/device-r6/s1-acceptance.txt)
contiene **8 PASS linea già esercitati nell'incremento38**, **2 PASS nuovi
di lettura/selezione testo** e **2 PASS nuovi di parametri**: **12 totali,
quattro aggiuntivi rispetto al subset38**.

| Gruppo | Sottocasi PASS |
|---|---|
| Parte, linea | `S1-04-Q-open-Part`, `S1-13-Q-cancel-Part`, `S1-05-Q-line-Part`, `S1-13-Q-finish-Part` |
| Lamiera, linea | `S1-04-Q-open-Sheet`, `S1-13-Q-cancel-Sheet`, `S1-05-Q-line-Sheet`, `S1-13-Q-finish-Sheet` |
| Testo nativo, sola lettura | `S1-TextRead-Q-Part`, `S1-TextRead-Q-Sheet` |
| Parametri in Progettazione | `S1-Parameters-Q-typed-readonly-Part`, `S1-Parameters-Q-cancel-Part` |

I casi testo usano apertura e selezione sintetiche, con backend Inventor
reale. Verificano vertici, indici, colori, bounds, highlight collegato al
riferimento e invarianza di contenuto/revisione; non eseguono creazione o
modifica del testo tramite comandi di authoring nascosti.
I due casi parametri verificano valori Boolean/Text nel catalogo e azioni
informative senza mutazione, poi tastiera testuale, Anteprima e Annulla con
parametri/consumatore/revisione invariati.

Il passo seguente applica l'espressione `20 mm` a `S1QuestLength73`. Il
runner termina con:

> Native parameter text reference/value/glyphs did not update after expression Apply.

È un'asserzione del runner reale sul readback immediato, non un abort del
coordinatore. Il run non arriva al secondo aggiornamento a 35 mm né ai casi
successivi create/rename/delete: non attribuire loro un PASS.

L'[ispezione host successiva](../artifacts/s1-verification/increment75/device-r6/host-public/inspect-001.json)
osserva nella Parte l'espressione `20 mm`, valore nativo `2.0` cm e testo
`L=20`, con 126 vertici/118 facce. La Lamiera resta a `10 mm`, `L=10`,
96 vertici/88 facce. Sono osservazioni temporalmente distinte: l'ispezione
finale non annulla il FAIL immediato e non determina da sola se la causa sia
sincronizzazione, rigenerazione, identità o confronto del runner.

## Aggiornamento r8: consumatore a 20 mm e arresto offline

Il [log r8](../artifacts/s1-verification/increment75/device-r8/s1-acceptance.txt)
mantiene gli stessi 12 PASS di gruppo. Con l'add-in candidato80, il controllo
immediato stretto dopo Apply a 20 mm viene superato e il record
`parameter-native-consumer 20` espone `L=20` e glifi aggiornati. È un progresso
rispetto al punto di arresto r6, ma non aggiunge un tredicesimo PASS di gruppo:
il workflow richiesto con due aggiornamenti 20→35 mm non è completato.

Il successivo errore è:

> Action refused: offline, outside session or active fixture identity changed.

La verifica logcat del coordinatore ha rilevato rate limiting del PC verso
il visore, con limite predefinito 240 richieste/minuto. Questo dato va
distinto dal messaggio generico dell'azione e non determina ancora una causa
certa nella UI: occorre completare l'audit delle richieste e della transizione
offline. Il run resta FAIL, senza PASS per 35 mm o per i successivi CRUD.
La nuova evidenza non prova da sola equivalenza desktop o chiusura del difetto
complessivo; il candidato80 e il percorso rimangono da verificare.

Il [verbale r8](../artifacts/s1-verification/increment75/device-r8/restoration.json)
registra tutti i cleanup true. L'host
[`stopped.json` r8](../artifacts/s1-verification/increment75/device-r8/host-public/stopped.json)
conferma uscita del processo owned PID55060 e conservazione dei processi
preesistenti, incluso PID91056. Manifest/DLL originali e APK ordinaria38
sono ripristinati e verificati; l'hash dell'ordinaria resta quello riportato
nella tabella iniziale. Non sono stati acquisiti documenti utente via COM.

## Cleanup verificato e limiti

Il [verbale r6](../artifacts/s1-verification/increment75/device-r6/restoration.json)
registra tutti i cleanup true e nessun errore del wrapper. L'host
[`stopped.json`](../artifacts/s1-verification/increment75/device-r6/host-public/stopped.json)
termina con exit0: fixture owned chiuse senza salvataggio, zero documenti
finali, processo owned PID77288 uscito e processi preesistenti conservati.
Il PID utente91056 mantiene lo stesso start-time prima/dopo; non è stato
acquisito via COM né usato per le fixture. Questo controllo dell'identità
del processo non equivale a una lettura del contenuto dei documenti utente.
Manifest originale e DLL sono verificati, bootstrap rimossi, reverse USB
rimosso e APK ordinaria38 reinstallata con hash remoto identico al baseline.

Il [pannello schizzo76](s1-sketch-parameter-editor76.md) ha evidenza separata
di **1.253 test Core e 1.119 Unity EditMode PASS**. La QA78 lo include, ma
questo runner esercita i parametri fuori dallo schizzo, in Progettazione:
non prova il pannello76 dentro lo schizzo Parte/Lamiera.

### Tentativo r9 con QA79

Il [verbale r9](../artifacts/s1-verification/increment75/device-r9/restoration.json)
registra **13 PASS**, inclusi entrambi gli aggiornamenti20→35mm con valore,
testo risolto e glifi aggiornati al primo readback richiesto e identità
conservata. La QA79,94167817byte, ha SHA256
`E9DDD2397B5E6A94B4D91723F95722092F151246B89A403D0FC9C1AD95C9167D`.
La DLL80 rimane `711D8451C6740F5290A67F48E2F07DD0319534CB226CD4EB83FBD157A39F16C4`.
Tre pause65s fra fasi rispettano il limite ordinario240 richieste/minuto;
nessuna pausa o retry interviene fra Applica e primo readback.

Il run termina **FAIL** al controllo della creazione del parametro
inutilizzato: l'asserzione richiedeva uguaglianza esatta a3. L'ispezione
nativa registra `S1QuestUnused74`, espressione `3 mm`, unità `mm` e valore
`0.30000000000000004`cm, cioè `3.0000000000000004`mm. Il runner è stato
corretto successivamente per usare tolleranza1e-6mm, ma r9 rimane FAIL e
rinomina/eliminazione non sono ancora verificate sul Quest.

Il bootstrap negativo è PASS separato; bootstrap positivo consumato e
tutti i cleanup r9 risultano true, senza errori del wrapper. Installazione,
istanza utente e APK ordinaria38 sono ripristinate e verificate. La QA81
aggiunge il pannello76 dentro lo schizzo Parte/Lamiera: la build r1 è
compilata ma non installata, perché conserva l'asserzione numerica errata;
la build r2 corretta viene poi eseguita nel run r11.

### Tentativi r10/r11 e pannello81

R10 si ferma al preflight ADB con dispositivo scollegato, senza installare
o creare fixture. Dopo il nuovo collegamento, [r11](s1-native-parameter-quest81.md)
termina PARTIAL con22 sottocasi PASS e cleanup interamente verificato:
CRUD in Progettazione e lettura/Cancel/Apply/CRUD nel pannello schizzo
Parte/Lamiera. Il rapporto81 conserva SHA, sorgenti, screenshot, ambito
esatto e limiti. I18 gate complessivi rimangono aperti.

Restano aperti opzioni e workflow parametrici non coperti81, altri comandi,
authoring testo, confronto desktop, wireless, prestazioni e
prova fisica MR/VR. Input e selezione sono sintetici; screenshot e compositor
non certificano leggibilità, tracking o ergonomia. Il collegamento reale è
HTTPS pinned su USB, non una misura Wi-Fi. Nessun gate complessivo S1 è chiuso.
