# Collaudo completo automatizzabile — F0–F4

25–26 ottobre 2026, branch `nuova_ux`. **Collaudo della tranche F0–F4 concluso
nel perimetro software sotto: nessun fallimento nelle ultime corse dei casi
eseguiti. L'accettazione completa delle milestone storiche resta parziale.**
La prova fisica su Quest è esclusa su richiesta dell'utente. Si verificano le
funzioni software implementate; F5–F8 non vengono dichiarate implementate.
Runtime: Meta XR Simulator 207.1 / Unity 6000.6.3f1, CAD nativo Inventor 2027
su fixture dedicate, input sintetico esplicito. Nessun documento dell'utente
viene salvato o modificato dalla suite.

## Evidenze concluse

| Livello | Esito corrente | Artefatto |
|---|---|---|
| Core XR e HTTPS/FakeAddIn | 1.355 PASS, zero errori/skip | `artifacts/simulator-full/core/core.trx` |
| Unity EditMode finale | 1.161 PASS, zero errori/skip | `artifacts/simulator-full/editmode-final.xml` |
| Backend | 1.926 PASS, zero errori/skip; warning preesistenti e caso duplicato xUnit segnalato | `artifacts/simulator-full/bridge/bridge.trx` |
| Backend dopo fix RC4 | 1.926 PASS, zero errori/skip | `artifacts/simulator-full/bridge-final/bridge-final.trx` |
| Pipeline Capture PC | 16 PASS, zero errori/skip | `artifacts/simulator-full/capture-worker/capture-worker.trx` |
| M7 completo nel simulatore | 97 asserzioni, sette gruppi PASS; fixture invariata e ripristinata | `artifacts/simulator-full/sweep02/m7-20261025-204246/` |
| M1–M5, seconda serie | 961 asserzioni PASS; ogni fixture ripristinata | `artifacts/simulator-full/sweep02/results.json` |
| Regressione individuata durante il collaudo | riconoscimento Lamiera riparte dopo chiusura del workspace inattivo; test EditMode mirato PASS | `artifacts/simulator-full/lamiera-regression.xml` |
| Riferimento RC4 nativo | 37 PASS: plan/preview/rollback, commit, primitive, provenienza, quote, Undo/Redo, salvataggio e riapertura; zero documenti rimasti aperti | `artifacts/simulator-full/rc4-native-06.log` |
| `face_feature` nativo | PASS estrusione/raccordo/smusso/foro + parametro guidato da espressione | `artifacts/simulator-full/face-feature.log` |
| Android QA e ordinario | build PASS, ARM64 IL2CPP; 16 nomi runner nei metadati QA, zero nell'ordinario; nessuna esecuzione hardware | `artifacts/simulator-full/package-inspection.json` |

## Corse eseguite nel simulatore

| Runner | Perimetro | Stato |
|---|---|---|
| M1 | Home/connessione, resa CAD, selezione, Capture locale e review sintetiche | PASS 79, seconda serie |
| M2 | browser, proprietà, sezione, misure, scala, ambiente, documenti | PASS 173, seconda serie |
| M3 | feature parte, preview/cancel/apply, volume, undo/redo, stale | PASS 259, seconda serie |
| M4 | assieme, spostamenti/vincoli e input sintetico | PASS 142, seconda serie |
| M5 | lamiera, preview/apply/stale e voce sintetica | PASS 308, seconda serie |
| M6 | quattro workspace, input, keypad, preview/commit, manipolazione, voce | PASS 625, seconda serie |
| M7 | visibilità, focus, interferenze, distanza, salute/BOM, stale e concorrenza | PASS, limiti dettagliati nel log |
| M8/M10 | tema, font, icone, rebuild e profilo PC; regressione M6 | M8 PASS 678; M10 PASS 1.704, con campioni CPU/GC, GPU non disponibile |
| M9/n/f/h | contesti, doppio Trigger, Torna, input/voce, annidati, flessibili e finestre nascoste | M9 549 asserzioni senza errori ma PARTIAL per maniglia/highlight storici; M9n PASS 121; f/h PASS 200 ciascuno |
| QX esteso | attività, blocchi authoring, cambio documento nativo, interruzione/ripristino host HTTPS, guardie Capture | PASS 51, quarta serie; stato nativo invariato e fixture ripristinata |
| S1 | editor nativo schizzi, testi e parametri, preview/cancel/apply su parte e lamiera | 4.213 asserzioni senza errori nei casi eseguiti; PARTIAL incrementale; fixture ripristinata, `s1-03` |

Ogni `NOT COVERED` del runner storico resta visibile nel JSON e nel log. A fine
collaudo va classificato come coperto da altra evidenza software, escluso fisico
o mancante: una riga finale `PASS COMPLETE` del runner non basta a chiuderlo.
Le prestazioni misurate sul PC non certificano il budget GPU del Quest.

I quattordici runner principali totalizzano **5.186 asserzioni** nelle ultime
corse selezionate, con ripetizioni della regressione M6 dentro M8/M10. Non sono
5.186 funzioni distinte e il totale non promuove i sottocasi `NOT COVERED`.
M1–M8 e M9n: `sweep02`; M9/f/h: `sweep03`; QX: `sweep04`; M10: `sweep05`.
Le serie precedenti conservano errori e corse fermate prima della preparazione:
non vanno sommate agli esiti finali né nascoste.
Con S1, i **15 runner** totalizzano **9.399 asserzioni**. Il conteggio dei test
Core/backend/Capture.Worker/EditMode è separato: **4.458 PASS**.

Due difetti di prodotto sono emersi: la scoperta Lamiera cancellata dalla
chiusura del workspace inattivo e RC4 che richiedeva `CopyWithOwner=true` su
attributi del documento. Il secondo falliva nativamente con `E_FAIL`; la prova
con attributi persistenti e `CopyWithOwner=false` verifica anche la riapertura.
I restanti adeguamenti riguardano runner precedenti alla UX F0–F4/S1: Text/TMP,
provider Verifica, gruppi Inspect filtrati, chiusura esplicita della scheda
feature, profili/pagine palette e contatore revisioni dopo attivazione documenti.

## Copertura e limiti che non vanno confusi con la prova fisica

| Gate F0–F4 | Evidenza software conclusa |
|---|---|
| QX-01 | Home e quattro attività: Core/EditMode, M1 e QX nativo |
| QX-02 | cambio documento assieme/parte/assieme e arresto/riavvio dell'host HTTPS; attività conservata |
| QX-03 | protezioni bozze/editor/preview e Capture Recording/Paused/upload/review/commit incerto; raw sintetico conservato |
| QX-04 | catalogo, azioni obsolete, voce e confine MCP; preview diretta fuori Disegno rifiutata prima del trasporto |
| QX-05 | regressioni dei sottocasi esercitati dai runner migrati M1–M10 e S1; limiti storici sotto conservati |
| QX-06 | misura/scala Presentazione, sezione/browser Verifica e strumenti locali offline |
| QX-07 | M7 nativo completo: visibilità/focus/scope, interferenze/distanza/salute/BOM, stale e concorrenza |

QX-08–14 appartengono a F5–F7 e non sono implementati da questa consegna.
QX-15/16 non ricevono un PASS hardware da un profilo PC o da input sintetico.

| Voce non coperta dal singolo runner | Esito complessivo |
|---|---|
| RC2 transfer e RC3 review PC | test HTTPS/Core e Capture.Worker; M1 resta una fixture locale sintetica |
| RC4 riferimento CAD nativo | chiuso software dalla sonda RC4, separata dal rendering M1 |
| M6/M8 stale live e regressioni storiche | M3/M5 verificano rifiuti nativi; M1–M8 sono rieseguiti separatamente |
| M7 offline/parte | Core/EditMode e QX esteso; prova nativa di perdita/ripristino HTTPS |
| M9 maniglia feature e highlight di tutte le facce | lacune software storiche, ancora aperte; estranee alla consegna F0–F4; M9 resta PARTIAL |
| M6 anello Inspect, sottoassieme flessibile vincolato M9f/h | capacità/caso storico non chiuso da queste fixture; non attribuito a un limite fisico |
| S1 altri comandi/opzioni, cambio unità, commit Boolean/Text dentro l'editor | fuori dal sottoinsieme del runner incrementale, ancora aperti come collaudo nativo; S1 resta PARTIAL |
| S1 stale/offline e parità desktop | contratti unitari separati; questa corsa non certifica il percorso nativo completo o il desktop |
| S1 ripristino fixture | PASS nel wrapper `native-cleanup.json`; il runner interno non può certificarlo da solo |
| Profilo M10 A/B/A | campioni CPU/GC e intervalli frame PC disponibili; GPU non fornita; nessun budget Quest certificato |
| Camera, tracking, microfono, controller fisici, metrologia, ergonomia e GPU Quest | prova reale esclusa su richiesta; nessun PASS attribuito |

L'accettazione di tutte le milestone storiche **non è completa**: M9 e S1
conservano esplicitamente i limiti software sopra. Non vengono classificati
come «resta soltanto la prova fisica». Il collaudo della tranche F0–F4 va letto
insieme a questa matrice e alle evidenze unitario/simulatore/nativo distinte.

## Ripristino dell'ambiente

Ogni fixture preparata nelle corse finali risulta ripristinata. L'add-in QA è
stato rimosso dal manifest attivo e l'installazione precedente riavviata.
Il manifest originale coincide byte per byte con il backup: SHA-256
`807DAC44FCB8D333647D171482C016078885881E6C4AA4B6050A4C63CE7F98C9`.
Stato e hash della DLL QA: `artifacts/simulator-full/addin-qa-state.json`.
Inventor ripristinato risponde a health senza documento attivo; anche il target
MCP è stato riallineato dopo il cambio PID. I file nativi RC4 salvati sono
parti sintetiche dedicate, conservate nel workspace come evidenza.
Ripristinati anche font SDF, impostazioni generate dal SDK e preloaded assets
modificati durante le build. Le copie di recupero Unity sono conservate in
`artifacts/simulator-full/unity-scene-recovery/`; la scena Main generata non è
stata modificata manualmente.

## Pacchetti e ripetizioni

| APK | Byte | SHA-256 |
|---|---|---|
| QA Development ARM64 | 95.732.383 | `E78B7BEECF813D0575C005F0C0B9AA28006A9F105D35182F89E04C57C796B5EA` |
| Ordinario ARM64 | 58.008.028 | `1C6FCD0441045C9D19E0A26567C2F286B480738F37367ED4768BDD5AC952AC24` |

La suite EditMode iniziale ha trovato cinque contratti di runner da aggiornare;
quella finale passa tutti i 1.161 casi. Un tentativo backend ha avuto timeout del
processo di pairing durante importazioni concorrenti Unity: la ripetizione
seriale completa passa 1.926 casi. Le sonde RC4 precedenti al run06 conservano
errori di fixture/serializzazione, il rollback COM che ha individuato il bug e
un timeout durante creazione documento sotto carico. Quel documento è stato
ispezionato come parte sintetica vuota, identificato e chiuso prima di ripetere
la prova; nessun retry automatico di una scrittura con esito incerto.

S1-01 si è fermato all'avvio per il formato della scadenza bootstrap; S1-02 ha
individuato la mancanza della fixture testo/parametri. Entrambi sono ripristinati.
S1-03 è la corsa incrementale valida. Nessun tentativo incompleto vale come PASS.

## Riproduzione

Con una sola istanza Inventor 2027 e add-in SO sperimentale attivo, Editor chiuso:

```powershell
& 'Inventor XR SO/Tools~/Invoke-SimulatorSuite.ps1' -Output 'artifacts/simulator-full/nuova-corsa'
& 'Inventor XR SO/Tools~/Invoke-SimulatorLive.ps1' -AcceptanceRunner m7 -Output 'artifacts/simulator-full/solo-m7'
```

I runner sono disponibili anche nell'Editor, ma si avviano soltanto con richiesta
esplicita, file credenziali temporaneo e cartella evidenze. Nei player restano
compilati soltanto con `XR_SO_ACCEPTANCE`. Manifest, log e screenshot sono
isolati per corsa; lo script conserva lo stato nativo prima/dopo e ripristina la
fixture nel `finally`. Una mancata chiusura della fixture arresta la suite.
