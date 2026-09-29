# M4 — Implementazione e collaudo

28 settembre 2026. Il codice M4 è implementato e ha superato i test automatici,
nativi, HTTPS e il runner nel Quest 3. **L'accettazione completa resta aperta**
per i gesti e la leggibilità verificabili solo con i controller fisici.

[Specifica M4](superpowers/specs/2026-09-27-inventor-xr-so-m4-design.md) ·
[Piano](superpowers/plans/2026-09-27-inventor-xr-so-m4.md) ·
[Probe riproducibili](../bridge/tests/M4LiveProbe/README.md).

## Funzioni implementate

- Contesto Assembly legato a documento e revisione: occurrence dirette, stato
  grounded/soppresso/adattivo/flessibile, assi e centro DOF reali, facce e
  spigoli proxy. Un DOF sconosciuto disabilita Move.
- Operazioni atomiche `assembly_move`, `assembly_constraint`, `assembly_joint`:
  solver autorevole, rifiuto di pose non raggiunte entro 0,01 mm/0,01°, sei
  vincoli (Mate, Flush, Mate Axis, Insert, Angle, Tangent) e sei joint (Rigid,
  Rotational, Slide, Cylindrical, Planar, Ball). Cylindrical usa LinearPosition;
  Ball richiede gap zero.
- Preview dell'intero assieme nel frame globale prima del rollback, incluse
  istanze annidate/ripetute. Nessun ID CAD transitorio nel GLB. Validator
  rebuild, salute relazioni, interferenza e clearance opzionale. Commit e
  Undo/Redo espliciti con cronologia XR del documento.
- Workspace XR Assembly: componenti, A/B ciano/giallo con evidenziazione facce,
  compatibilità contestuale, assi DOF, Grip solo vista, Grip+Trigger per Move,
  input mm/gradi, Preview/Applica/Annulla, errori leggibili, pin pannello e
  attivazione esplicita della definizione di sottoassieme. Cambio scena,
  revisione o rete revoca Applica; un commit incerto blocca anche Design.
- Attivazione documento senza transazione di prova: Inventor espone una
  unidentified transaction anche quando inattivo, e una transazione vuota
  eliminerebbe Redo. Una vera transazione utente aperta blocca l'attivazione.

## Risultati verificati

| Ambito | Risultato | Evidenza |
|---|---|---|
| Add-in Inventor 2027 sperimentale | build 0 errori, 0 warning | `bridge/src/plugin-so27/bin/M4/net48/` |
| Backend .NET | 924/924 | `bridge/tests/Bimwright.Ipt.Tests/TestResults/m4-backend.trx` |
| Core .NET | 167/167 | `Inventor XR SO/Tests~/XrSo.Core.Tests/TestResults/m4-core.trx` |
| Unity EditMode | 94/94 | `artifacts/m4-verification/unity-editmode.xml` |
| Inventor nativo | passati | `artifacts/m4-verification/native.log` |
| Sottoassiemi annidati | passati | `artifacts/m4-verification/nested.log` |
| Host HTTPS con add-in installato | passati | `artifacts/m4-verification/https.log` |
| Android Release | build, installazione e hash identici | `artifacts/m4-verification/manifest.json` |
| Runner nel processo Quest | PASS COMPLETE: preview, Cancel, Apply, centro nativo, Undo/Redo, stale e riapertura | `artifacts/m4-verification/quest-acceptance.txt` |
| Screenshot dalla preview Quest | generato dall'app, 224294 byte | `artifacts/m4-verification/quest-acceptance-preview.png` |

Il probe nativo copre DOF libero 3+3, slider 1+0, grounded 0+0,
rotational 0+1, rotazione residua di 30°, traslazione vietata, preview/rollback,
sei joint e sei vincoli con negativi same-occurrence, commit/Undo/Redo,
contesto soppresso e Redo conservato nell'attivazione documento. Il probe
annidato verifica due sottoassiemi ripetuti, uno ruotato, quattro mesh in
coordinate globali, child edit rifiutato e attivazione esplicita.

Il probe HTTPS attraversa TLS pinned, autenticazione MCP, dispatcher reale,
download GLB, clearance, Move 10 mm, Undo/Redo con posa nativa, rifiuto di
preview stale dopo edit desktop e joint rotazionale con DOF 0+1. I fixture
temporanei sono chiusi e il documento attivo precedente ripristinato.

I test Unity includono input controller simulato a scala 1:1 e 1:10,
Grip senza scrittura, tracking perso, UI senza click-through, DOF ignoto,
highlight A/B, cleanup, preview visibile obbligatoria, risposta tardiva,
commit incerto dopo Close/Rebind, selezione contestuale A/B e scena
ricostruita alla stessa revisione. Dopo il primo test fisico che ha mostrato
un bersaglio poco intuitivo, M4 consente anche Grip+Trigger sul componente
selezionato in modalità CAD Move e mostra solo l'asse attivo; il gesto resta
vincolato al solver e Applica separato. I test Unity per questo percorso passano
sia a scala 1:1 sia a 1:10. Non attestano ergonomia fisica.

## Quest e gate ancora aperti

L'APK ordinario aggiornato è `artifacts/m4-verification/InventorXrSo.apk`,
SHA-256 `F8A2B833BE64E47E483B86958231A98E02FE7B44B6EB4D0C2DFCDCF99095E7DC`.
È stato compilato dopo la prova fisica e installato sul Quest 3
`2G0YC1ZFB407P1` al termine del batch automatico, con hash uguale al file
archiviato. Add-in corretto installato in
`%LOCALAPPDATA%/InventorSO/packages/20260928-145027-e42332cc/addin/`,
Inventor PID 77812 e host HTTPS PID 75880 sulla porta 8443 al termine del
collaudo integrato del 28 settembre.

L'APK separato `artifacts/m4-verification/InventorXrSo-acceptance.apk` contiene
un runner opt-in, compilato solo con `XR_SO_ACCEPTANCE`, avviabile solo con
l'extra Android `xr_m4_acceptance=true`. Prima di avviare le mutazioni richiede il
fixture dedicato `XR_M4_Quest_Acceptance.iam`; verifica preview renderizzata,
Cancel, Apply, centro/DOF nativi, Undo/Redo, stale plan e riapertura workspace,
scrivendo log e screenshot nell'area persistente dell'app. Il percorso del
fixture è in `artifacts/m4-verification/quest-fixture.json`. Il runner non
è nell'APK ordinario.

La build QA usata nella prima prova ha SHA-256
`1CC04951D7F7365E1DBC54989AB5D75532450B43FB66EC3C93E54150F33DE8DE`
nella prima prova. Il pairing con Quest 3 sulla nuova rete è
riuscito. Il runner, avviato sul fixture dedicato, ha osservato il centro
iniziale a 166,209 mm, una preview di +10 mm senza cambio revisione, Cancel,
Apply con centro finale a 176,209 mm, Undo/Redo che ripristinano i centri attesi, rifiuto
di una preview obsoleta e riapertura del workspace con DOF. Lo screenshot è
stato scritto dall'app senza interventi ADB. Una prima esecuzione era fallita
per un'asserzione del runner sui due renderer di preview e una successiva aveva
richiesto una cattura ADB a causa di un percorso Android duplicato; entrambi i
difetti del solo harness QA sono stati corretti prima dell'esecuzione finale.

La prova fisica ha confermato che, nella nuova UI, Grip+Trigger sul cilindro
selezionato genera un'anteprima. Un primo tocco su Applica ha lasciato il
centro X a 176,209 mm e mostrato "Comando non completato"; il dettaglio di
quel primo errore non è stato acquisito. Una nuova anteprima visibile con
Δ 3,0 mm e Applica disponibile è stata poi applicata: la lettura diretta di
Inventor ha rilevato il centro X a 179,209 mm. Le letture prima e dopo sono
in `artifacts/m4-verification/quest-pose-before-physical-apply.json` e
`artifacts/m4-verification/quest-pose-after-physical-commit.json`. La
schermata Quest successiva mostra il pannello Assembly chiuso.
L'utente ha inoltre confermato che Grip semplice ha continuato a muovere
soltanto la vista e che il nuovo gesto Grip+Trigger sul cilindro è chiaro.
Una seconda prova fisica ha selezionato A: Faccia 3 e B: Faccia 2 su due
cilindri distinti, con evidenziazione delle facce. Lo screenshot è in
`artifacts/m4-verification/quest-reference-check3.png`. Un successivo
tentativo di Joint Planar ha mostrato "Comando non completato"; la posa
nativa non è cambiata e il dettaglio dell'errore resta da acquisire. La
selezione A/B è osservata, ma il workflow Joint fisico non è accettato.

Il runner non esercita input fisico dei controller. Per chiudere interamente i
gate A01, A02, A07 e A14 servono ancora le prove su scala ridotta,
traslazione e rotazione confrontate con input numerico, verifica del filtro
dei comandi compatibili dopo A/B e verifica completa della leggibilità e del
tracking nel visore. Gli altri gate hanno
evidenze native, HTTPS, Unity o Quest per i rispettivi sottocasi, ma non vanno
segnati globalmente accettati finché questi percorsi fisici restano aperti.
L'APK ordinario è stato reinstallato al termine del batch.

## Batch autonomo del 28 settembre

Su richiesta dell'utente è stato eseguito un batch senza indossare il visore:
backend 924/924, core 167/167, Unity EditMode 94/94, probe nativo, annidato e
HTTPS tutti passati. Log specifici `m4-batch-*` in
`artifacts/m4-verification/`. Il primo tentativo backend ha ereditato
`INVENTOR_SO_EXPERIMENTAL=1` dall'host e ha fallito soltanto un test di
configurazione; ripetuto con la variabile rimossa, 924/924 passati.

Il probe aggiuntivo ha ripetuto le facce fisiche A: Faccia 3 e B: Faccia 2.
Sono entrambe `kPlaneSurface`; la lista di vincoli compatibili è Mate, Flush,
Angle. Inventor 2027 rifiuta `CreateAssemblyJointDefinition(kPlanarJointType)`
con queste facce (`0x80004005 E_FAIL`) prima della verifica delle collisioni.
Tutte e quattro le combinazioni di facce piane del fixture sono state respinte
allo stesso stadio. Con due spigoli circolari la definizione Planar è creata,
ma la preview del fixture è respinta per interferenza tra i cilindri. Ogni
prova ha lasciato invariata la revisione. Evidenza:
`artifacts/m4-verification/m4-batch-planar.log`. È un difetto di scelta
dell'origine del Joint Planar, non un problema di rete o del gesto A/B.

La correzione nel sorgente costruisce ora l'origine di un Joint Planar su una
faccia piana dal centro di un bordo circolare, o dal punto medio di un bordo
lineare. Inventor 2027 ha accettato sia la definizione sia il gap nel probe
nativo con le stesse facce A/B, senza cambiare la revisione. L'add-in
sperimentale aggiornato compila con 0 errori/0 warning; backend 924/924 e
probe annidato passano dopo la modifica. Evidenza:
`artifacts/m4-verification/m4-planar-origin-fix.log`. Il primo runner Quest
aveva esercitato l'add-in precedente e riproduceva ancora `E_FAIL`. L'add-in
corretto è stato poi installato e caricato in una nuova istanza Inventor 2027.
Il probe integrato HTTPS ha creato l'anteprima Planar sulle stesse facce A/B
con gap 30 mm, senza cambiare la revisione; con gap zero il validator ha
respinto l'interferenza tra cilindri. Un secondo probe ha applicato il Joint,
verificato i tre DOF residui e annullato via XR, ripristinando posa e DOF
iniziali. Evidenze: `m4-planar-final-live.log` e
`m4-planar-integrated-commit-undo.log`. L'istanza Inventor è rimasta aperta
con il fixture fisico salvato e posa X 179,209 mm invariata.

La QA APK estesa, SHA-256
`0A84ED73651D35BD2833561CAFB48C3E27A9AF555B8A42FA50A99730A28C59F4`,
è stata installata sul Quest con hash identico. Horizon OS ha inizialmente
bloccato l'avvio con «Passa ai controller»; l'utente ha attivato i controller e
premuto «Continua» una sola volta. Da quel punto il runner è proseguito senza
interventi: A/B e lista compatibile corretti, errore Planar riprodotto, preview
di +10 mm renderizzata senza modificare la revisione, Cancel, Apply con centro
nativo da 50 a 60 mm, Undo/Redo, piano stale rifiutato e workspace riaperto.
Evidenze: `m4-batch-quest-acceptance.txt`, `m4-batch-quest-preview.png` e
`m4-batch-quest-native-pose.json` nella cartella artifacts M4.

Il fixture batch è stato chiuso senza salvare e il precedente fixture fisico
riattivato, con centro nativo X 179,209 mm invariato. I manifest dei due
fixture sono conservati separatamente. L'APK ordinario aggiornato è stato
reinstallato; l'hash del `base.apk` corrisponde a
`F8A2B833BE64E47E483B86958231A98E02FE7B44B6EB4D0C2DFCDCF99095E7DC`.
Il comando temporaneo di prossimità è stato ripristinato (`set_proximity_close=false`)
e le altre tre proprietà di test Meta sono `false`.

Nessun reset del Quest o del pairing. Il comando temporaneo di prossimità
documentato da Meta è stato ripristinato; `disable_guardian`,
`disable_dialogs`, `disable_autosleep` risultano `false`.
Riferimento: [Meta Scriptable Testing Services](https://developers.meta.com/horizon/documentation/native/android/ts-scriptable-testing/).

## Ripetizione del runner il 29 settembre 2026

`artifacts/m4-verification/quest-acceptance-run-20260929-103443.json` registra **PASS COMPLETE** con il nuovo APK di accettazione: A/B e vincoli compatibili, preview Move da 10 mm, Cancel, Apply con centro nativo da 50 a 60 mm, Undo/Redo, stale e riapertura Assembly. La diagnostica Planar ha riportato un rifiuto per interferenza tra i cilindri della fixture; non è conteggiata come successo Planar. L'assieme di prova è stato chiuso senza salvare. Il ripristino precedente lasciava aperta la parte `Cylinder.ipt`: è stata chiusa senza salvare e il metodo `--restore-quest` è stato corretto per chiudere anche la parte nei run futuri. Input controller fisico non esercitato dal runner.

## Prove fisiche guidate del 29 settembre 2026

Su una fixture temporanea pulita (due cilindri, Cylinder:2 a X = 50 mm),
Grip destro senza Trigger ha mosso solo la vista: nessuna anteprima e posa
CAD invariata. Il precedente tentativo A01 era contaminato da un tocco su
Applica dichiarato dall'utente; la ripetizione controllata costituisce la
prova A01.

A scala 1:1, il gesto Grip+Trigger ha prodotto Δ X = 7,55 mm visualizzati;
Inventor ha misurato X = 57,554054 mm dopo Applica. Dopo Undo, l'input
numerico 7,55 mm ha prodotto X = 57,55 mm dopo Apply/Redo: scarto
0,004054 mm. Per la rotazione attorno all'Asse 1, il gesto ha mostrato
−4,72°; la matrice nativa corrisponde a −4,717630°. Dopo Undo, l'input
numerico −4,72° è stato applicato: scarto angolare 0,002370° e scarto
posizionale 0,000414 mm. Le letture sono in
`artifacts/m4-verification/a02-rotation-controller-20260929.json` e
`artifacts/m4-verification/a02-rotation-numeric-20260929.json`. Questo chiude
il confronto A02 a 1:1; resta la prova fisica a scala ridotta.

L'utente ha selezionato due facce piane A/B e ha visto Mate, Flush, Angle,
Joint e Clearance 0 mm. Inventor non ha creato vincoli o joint e la posa è
rimasta invariata. Il log audit ha però mostrato una preview e un commit di
`assembly_move` dopo la selezione, senza variazione geometrica, benché
l'utente riferisca di aver usato solo i due Trigger. Il codice lasciava
attivo CAD Move dopo la scelta di A/B; ora l'ingresso nel workflow di
relazione cancella il comando Move precedente e non genera una preview.
Il nuovo test `SelectingReferencesAfterCadMoveDoesNotPreviewStaleMove`
passa; suite EditMode 178/178. APK ordinario aggiornato SHA-256
`529299E795437B33876001BAFA9F80A7877A1EB97A31F4B00B432A5135D82E29`.
La ripetizione fisica A07 sull'APK aggiornato è ancora necessaria per
escludere click-through/commit involontari. A14 resta aperto.

Per A02 a scala ridotta è stata preparata una seconda fixture temporanea
con cilindri di diametro 200 mm e Cylinder:2 iniziale a X = 500 mm.
La fixture precedente è stata chiusa senza salvare; l'assieme robot
originale resta aperto e non modificato. La nuova fixture è pulita,
senza vincoli né joint. La prova sul Quest è in attesa del visore attivo.

Il runner automatico M4 è stato esteso per riprodurre CAD Move → A/B senza
anteprima residua, traslazione e rotazione sintetiche a scala 0,25×, hit della
UI, perdita di tracking e riapertura pulita. La build di collaudo è riuscita
(SHA-256 `E248D889FF02351E21DB5E67C6EEA83D97BF928195F872241FA9F4D0663977BE`).
La prima esecuzione ha verificato l'installazione ma è scaduta prima
dell'avvio dell'app: Quest in standby, nessun log interno creato. Evidenza:
`artifacts/m4-verification/quest-acceptance-run-20260929-133351.json`.
La fixture grande è stata chiusa senza salvare, l'assieme robot originale
riattivato e l'APK ordinario reinstallato con hash verificato
`C2A935113F8F5C6A3A1D0FC78B6366D02D604FF9FBAD6A6A9AAD8230197FFB69`.
I nuovi sottocasi automatici richiedono ancora una run completata sul Quest;
il runner non sostituisce i gesti fisici né la valutazione di leggibilità.

Lo standard condiviso è stato inserito in `CLAUDE.md` e `bridge/CLAUDE.md`.
La build QA finale con dichiarazioni `NOT COVERED` per A01/A02/A07/A14
fisici è riuscita (SHA-256
`853C72E81A26D1B3EA0C99AB8191369A09BC6C384C608F1A2EA0EC0A2366286B`).
Il nuovo script registra fase del timeout, stato di avvio e check per gate;
il run `quest-acceptance-run-20260929-143512.json` ha confermato
`before_runner_start` con Quest `Asleep`. L'APK ordinario è stato
reinstallato con hash verificato. La fixture robot dell'utente è rimasta
invariata.
