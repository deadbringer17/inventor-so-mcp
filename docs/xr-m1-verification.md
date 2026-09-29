# Inventor XR SO — verifica M1, 2026-09-27

Ripresa dallo stato C2 già committato (`ec8cce2`). Il perimetro è il design M1 e i task C1–C9, non le milestone successive della spec prodotto.

## Implementazione

- C2: trasporto pinnato preesistente, verificato e ampliato con test pairing QR/codice su host HTTPS reale.
- C3: mesh condivise per asset, handedness e mapping triangolo/faccia, collider e posizionamento 1:1.
- C4: tint occurrence e overlay dei soli triangoli della faccia; pulizia al cambio scena.
- C5: indirizzi IPv4/DNS/IPv6, storage protetto Android Keystore, recupero da file corrotto.
- C6: controller destro, disabilitazione con tracking perso, MR e Studio VR.
- C7: Home italiana, tastiera indirizzo/codice, conferma fingerprint, stato offline e collegamento sessione/scena/selezione.
- C8: ZXing, scansione fotocamera, permessi e timeout/fallback codice.
- C9: scena riproducibile, script APK, documentazione e checklist fisica.

Correzioni rispetto al codice esemplificativo del piano: stop definitivo al cambio certificato; riconnessione anche alla chiusura SSE; annullamento selezioni su cambio scena; nessun highlight locale lasciato da errori remoti; buffer raycast espandibile; rilascio mesh/overlay; decoder QR con rotazione; cancel e cleanup dell'app; tastiera DNS/IPv6; permessi WebCamTexture completi. Meta SDK 207 crea internamente il passthrough Underlay: non espone più `OVRPassthroughLayer.overlayType`.

## Risultati automatici

| Verifica | Risultato |
|---|---|
| Core .NET / netstandard2.1 | **103/103 passati**, nessuno saltato |
| Unity EditMode | **27/27 passati**, nessuno saltato, inclusi loader XR Android, modulo UI destro, clic tastiera e messaggio Inventor non disponibile |
| Backend .NET | **893/893 passati**, nessuno saltato, ambiente test come descritto sotto |
| Prima build Android IL2CPP | **Riuscita**, APK generato |
| APK finale dopo l'ultima regressione | **Riuscita**, exit code 0, firma v2 verificata con `apksigner` |

EditMode include: tool MCP e SSE sul vero host HTTPS con FakeAddIn; certificato
errato rifiutato; pairing QR e codice con confronto fingerprint; winding e
triangle→face via raycast; mesh condivise e riutilizzate; overlay faccia e suo
rilascio al rebuild; raycast con oltre 32 collider; tastiera, limiti del layout
e testi; decodifica QR; scena Main con riferimenti presenti e nessuno script
mancante. Core copre anche stop senza retry al cambio certificato, riconnessione
SSE, reset delle selezioni durante pick e salvataggio protetto delle credenziali.

L'APK finale è stato ispezionato con `aapt`: package
`com.occhipinti.inventorxrso`, ABI `arm64-v8a`, min SDK 32, target SDK 36,
permessi INTERNET/CAMERA/HEADSET_CAMERA. La classe JNI `CredentialVault` è
presente nel DEX. La compilazione e l'inclusione nel pacchetto non verificano
l'esecuzione del Keystore su hardware.

Artefatto: `Inventor XR SO/Builds/InventorXrSo.apk`, **50.843.686 byte**,
generato il 2026-09-27 alle 08:47:05 Europe/Rome, installato sul Quest.
SHA-256: `3944b1409563bc771fe6bb32e4b2393e751ac7a0dc29e63a225b114013c46408`.

File locali dei risultati: `%TEMP%/xrso-keyboard-tests.xml`,
`%TEMP%/xrso-keyboard-tests.log`, `%TEMP%/xrso-keyboard-build.log`.

Unity ha serializzato il profilo volume URP, i filtri shader della pipeline,
le impostazioni globali URP, il progetto Oculus e le impostazioni di build.
`ProjectSettings/EditorBuildSettings.asset` include Main;
`ProjectSettings/ProjectSettings.asset` include le impostazioni XR precaricate;
`ProjectSettings/SceneTemplateSettings.json` è stato generato dall'Editor.

Baseline prima delle modifiche: Core 76/76, Unity EditMode 5/5.
Backend: 893/893 con `INVENTOR_SO_EXPERIMENTAL=0` nel solo processo di test. L'ambiente di sviluppo ha il flag a 1, incompatibile con l'asserzione di default di `TierPolicyTests.ExperimentalFlagLoadsFromCliAndEnvironment`. I due nuovi casi verificano TLS reale con PFX esplicito: su Windows il caricamento deve usare UserKeySet come il percorso self-signed, altrimenti SChannel chiude la connessione. Corretto anche il rinnovo heartbeat di FakeAddIn per collaudi oltre i 120 secondi.

## Collaudo fisico — completato

Quest 3 collegato e prima installazione riuscita. Il primo avvio ha rilevato
`DllNotFoundException: OVRPlugin`: C1 conteneva il loader Oculus ma nessuna
assegnazione Android in XR Management. Corretto il setup, persistita
l'assegnazione e aggiunto un controllo preventivo alla build. Il nuovo APK
contiene sia `libOVRPlugin.so` sia `libOculusXRPlugin.so`. Reinstallazione
riuscita: i log confermano HMD acquisito, tracking attivo e layer passthrough
creato. Una cattura ADB mostra la Home italiana renderizzata in stereo sopra
il passthrough; l'utente ha confermato l'interazione con i pulsanti tramite
controller destro. La prima scansione QR è andata in timeout: `dumpsys
media.camera` ha mostrato che il device iniziale era la camera avatar (source 1).
Aggiunto un selettore Camera2 basato sui vendor tag Meta: source 0, preferenza
posizione 0 (sinistra), senza hardcodificare gli ID 50/51 del dispositivo.
Verificati sul visore apertura device 50, frame 1280×960 e decodifica QR.
L'utente ha confermato pairing e caricamento del modello. Successivamente
il server è stato collegato a Inventor 2027 reale: assieme gestito di due
blocchi 100×60×20 mm, distanza libera 50 mm. La parte ha schizzo completamente
vincolato e volume verificato 120000 mm³. File di collaudo:
`%LOCALAPPDATA%/InventorSO/workspace/XR M1 Test 20260927 0830.ipt` e
`XR M1 Assembly 20260927 0830.iam`. Nessun documento utente preesistente era aperto.
Le catture Quest mostrano Home connessa con nome documento, versione e anno
Inventor, Studio VR e overlay faccia. I log del server reale confermano richieste
Quest `inventor_pick_entity` e `inventor_highlight_entity` riuscite. Dopo
aggiornamento APK e force-stop/start, la Home è tornata connessa senza pairing.
I punti seguenti non sono dichiarati superati dai test automatici o dalla sola compilazione APK.

| Verifica sul Quest 3 con Inventor 2027 | Esito |
|---|---|
| Pairing QR tramite fotocamera | Superato, conferma utente e log camera/decoder |
| Pairing manuale con confronto fingerprint | Superato dopo correzione del grilletto destro: tastiera e stato Connesso confermati dall'utente; redemption confermata dal server |
| Home: PC, versioni, documento e tipo | Superato, cattura ADB su Inventor 2027 reale |
| Scala 1:1 confrontata con una misura reale | Superato: l'utente conferma che il lato lungo del blocco coincide con 10 cm sul righello reale |
| Occurrence evidenziata su Quest e Inventor | Superato: richieste live e corrispondenza visiva confermata dall'utente |
| Secondo trigger: stessa faccia su entrambi | Superato: pick/highlight live, overlay Quest e conferma utente |
| Part: selezione diretta faccia | Superato: overlay Quest e conferma utente con singola pressione |
| Cambio documento attivo seguito dal Quest | Superato: assieme a due istanze → parte singola, senza riavvio |
| Modifica parametro: aggiornamento incrementale | Superato: d0 da 100 a 200 mm, geometria Quest aggiornata senza riavvio |
| Wi-Fi off/on: modello conservato e riconnessione | Superato: Wi-Fi Quest disattivato per 12 s, modello presente con badge Offline, poi Connesso automaticamente |
| Riavvio app: credenziali Keystore riutilizzate | Superato dopo aggiornamento APK e force-stop/start |
| Revoca token / cambio certificato: nuovo pairing | Superato: cambio certificato rilevato; revoca restituisce 401 Invalid token e l'utente conferma il ritorno alla richiesta di associazione. Nuovo pairing QR riuscito, stato Connesso confermato da utente e server |
| MR e Studio VR, menu e tracking controller | Superato: MR e Studio verificati da catture; pulsanti confermati dall'utente. Rimozione/reinserimento della batteria destra: raggio scompare e ritorna senza selezioni involontarie, conferma utente |

Il collaudo di accettazione M1 è completato il 2026-09-27 su Quest 3 e Inventor 2027, con le conferme fisiche dell'utente sopra riportate. La prova del controller verifica disconnessione e riconnessione tramite batteria; non costituisce una misura dei tempi di recupero da occlusione ottica. Il completamento riguarda il perimetro M1/C1–C9, non le milestone successive della spec prodotto. Build, installazione e preparazione host sono descritte nel [README XR](../Inventor%20XR%20SO/README.md).

### Correzione input menu durante il pairing manuale

`OVRInputModule` leggeva `PrimaryIndexTrigger` senza specificare il controller:
il gruppo `Touch` lo mappa sul sinistro, mentre il raggio è quello destro.
`ControllerUiInputModule` vincola pressione e rilascio a `RTouch`. La scena usa
questo modulo senza navigazione legacy; il raggio termina sul punto UI rilevato
e il tasto puntato assume un colore chiaramente distinguibile. Aggiunta una
regressione per gli eventi di clic sui tasti (inclusi cancella e conferma) e
verifica del modulo nella scena generata. Il tentativo di provare il raycast
con un render manuale della camera in EditMode ha causato un crash dell'Editor;
quel test è stato sostituito dal test degli eventi, senza modifiche al renderer
di produzione. L'utente ha confermato sul visore che la tastiera aggiornata funziona.

## Runner automatico sul Quest

Dal 29 settembre 2026 la milestone ha un runner in-app e una fixture dedicata secondo lo standard M4: vedi [test automatici sul Quest](xr-quest-acceptance.md). Runner e fixture non sono ancora stati compilati né eseguiti; nessun gate di questa milestone cambia stato finché non viene registrato qui un run reale (log `quest-acceptance-*` in `artifacts/`).
