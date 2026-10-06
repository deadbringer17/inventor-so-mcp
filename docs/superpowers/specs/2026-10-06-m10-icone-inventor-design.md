# Inventor XR SO — M10: icone Inventor

Data: 6 ottobre 2026, Europe/Rome. Stato: **migrazione software implementata; collaudo distinto nel verbale**.
Esiti: [verbale M10](../../xr-m10-verification.md).

## Obiettivo

Sostituire le scritte dei comandi CAD con le corrispondenti icone di Inventor
2027 nella tavolozza e nell'anello. Conservare nome italiano, sinonimi vocali,
id, callback, stato e motivo di disabilitazione. La UI rimane uGUI/TMP con tema
M8, geometria M6 e navigazione M9. Nessuna nuova capacità o chiamata CAD.

Riferimenti: [M8](2026-10-04-m8-grapics-design.md),
[M9](2026-10-04-m9-navigazione-contesto-design.md),
[prodotto](../../../Inventor%20XR%20SO/inventor_meta_product.md).

## Risorse e provenienza

Le DLL installate `InvAIRLookColorImages.dll`, `InvAIRLookImages.dll` e
`InvAIRLookImagesDark.dll` contengono risorse .NET `*.g.resources`, con nomi
semantici e ICO 16/32 pixel. La variante a colori esaminata contiene 2.682
risorse. Le DLL Browser sono invece risorse bitmap Win32: non usarle come
sostituto automatico delle icone dei comandi.

L'estrattore legge le DLL senza avviare Inventor o modificare documenti. Produce
ICO originali, PNG senza ingrandimenti, manifest con nome risorsa, dimensioni,
SHA-256 della DLL, dell'ICO e del PNG, catalogo HTML locale e ZIP. Cartella
predefinita: `artifacts/m10-icons/`. Il pack completo resta un artefatto locale;
Unity riceve solo le immagini selezionate, evitando migliaia di texture nell'APK.

Le risorse restano Autodesk, non ereditano MIT/Apache dal repository. Registrare
origine e assenza di una licenza di redistribuzione verificata; l'estrazione non
è una certificazione di diritti per una pubblicazione del pack o dell'APK.

Il mapping è esplicito: chiave `XrAction.Icon` → risorsa verificata. Nessun
matching della label tradotta, nessun glifo Unicode sostitutivo, nessun download
a runtime. Le varianti colore/chiaro/scuro sono inventariate separatamente;
la scelta definitiva sul navy M8 dipende dal confronto visivo e dal visore.

## Contratto della UI

- Solo azioni con icona disponibile mostrano il simbolo al posto del testo.
  Icona assente o chiave sconosciuta: testo completo, mai un pulsante vuoto.
- Puntando l'icona compare subito un tooltip con nome italiano completo e,
  se disabilitata, spiegazione. Il tooltip non intercetta il raggio; si chiude
  su uscita, disattivazione, cambio scheda e ricostruzione del controllo.
- Testo tooltip: cap height almeno 7 mm sulla tavolozza e 14 mm nell'anello;
  nessun auto-shrink, ellissi o taglio. Tooltip dimensionato al contenuto.
- Simboli senza tinta di brand, proporzioni e trasparenza conservate. Focus,
  pressed, selected/toggle e disabled rimangono riconoscibili sul pulsante.
- Hit area invariata: celle tavolozza 75,5×20 mm, anello 144×50 mm;
  icona indicativa 16 mm / 32 mm. I PNG 32 pixel non sono SVG e non acquistano
  dettaglio se ingranditi: nitidezza reale resta un gate fisico.
- Valori numerici, unità, opzioni con valore corrente, nomi CAD, picker,
  breadcrumb, messaggi ed errori restano testuali. La prima consegna mantiene
  anche le label della barra Anteprima/Applica/Annulla e del tastierino.
- La voce continua a leggere `XrAction.Label` e `Synonyms`; Applica conserva
  `VoiceInvokes=false`. Un'icona non cambia `Enabled`, `IsOn` o `TryInvoke`.
- Risorse caricate una volta e condivise; nessun PNG decodificato, Sprite o
  materiale creato durante i rebuild. Nessuna modifica manuale a Main.unity.

## Fasi

1. **Pack e inventario:** estrazione riproducibile, catalogo, manifest, mapping
   iniziale e risorse locali selezionate.
2. **Primitivo comune:** resolver Sprite, pulsante azione, tooltip, fallback
   e integrazione in tavolozza/anello.
3. **Pilota Progettazione:** Crea schizzo, Linea, Rettangolo, Cerchio,
   Estrusione, Foro, Raccordo, Smusso, Parametri, Undo/Redo.
4. **Estensione:** Lamiera, Assieme e Ispeziona, poi comandi Documento/Vista
   con corrispondenza semantica documentata; non forzare icone ambigue per XR.
5. **Consegna:** runner Quest, regressioni native pertinenti, build QA/ordinaria,
   catalogo definitivo, performance e prova fisica MR/Studio VR.

## Gate di accettazione

| Gate | Criterio |
|---|---|
| M10-01 | Pack rigenerabile da installazione, hash e dimensioni verificabili; catalogo e provenienza |
| M10-02 | Sprite locali validi, trasparenza e mapping esplicito; nessun asset sconosciuto |
| M10-03 | Icone tavolozza/anello con hit area invariata e tooltip completo non raycastabile |
| M10-04 | Fallback testo, disabled e toggle; nessuna invocazione extra su hover |
| M10-05 | Pilota Progettazione e voce preservano catalogo/callback/guardie CAD |
| M10-06 | Copertura Lamiera/Assieme/Ispeziona con mapping verificato e valori leggibili |
| M10-07 | Runner Quest sintetico, core/EditMode, regressioni e APK; sottocasi non coperti espliciti |
| M10-08 | 100 rebuild senza crescita di Sprite/texture/materiali; budget rispetto alla baseline M8 |
| M10-09 | Prova fisica: riconoscimento, tooltip, contrasto e nitidezza con controller reali in MR/VR |

Un PASS EditMode non chiude runner o prova fisica. Il runner usa fixture dedicate
e registra input **sintetico**, PASS/NOT COVERED, manifest/log/screenshot,
ripristino APK ordinario e documento. Il gate fisico resta aperto finché eseguito.

## Protocollo del confronto performance

L'APK M10 QA usa `Development` per rendere disponibili i contatori CPU; quello
ordinario rimane una build normale e non include i runner. Confronto controllato
A/B/A nello stesso APK, sulla stessa fixture, posa della tavolozza e tema: otto
etichette testuali con geometria M8, le stesse otto azioni con icone M10, ritorno
al testo. Non equivale al confronto tra due versioni complete del prodotto.
90 frame di riscaldamento e 240 campioni per fase; senza hover o rebuild durante
il campionamento. Report JSON e log includono conteggi dei campioni disponibili.

Il contatore `CPU Main Thread Frame Time` misura la metrica dichiarata da Unity;
il tempo GPU proviene dal plugin XR, tramite
[TryGetAppGPUTimeLastFrame](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/xr/xrdisplaysubsystem/trygetappgputimelastframe),
in secondi e solo se disponibile. L'intervallo tra frame include pacing XR e
non sostituisce il tempo di esecuzione CPU. GC: `GC Allocated In Frame`.

Budget del confronto: controlli A/A entro `max(0,5 ms, 10%)` tra loro per CPU/GPU;
M10 entro lo stesso incremento rispetto al maggiore dei due controlli; incremento
GC p95 massimo 128 byte/frame. Contatori assenti o controlli instabili lasciano
il sottogate aperto, con i dati effettivamente raccolti conservati. Il test delle
100 ricostruzioni verifica separatamente assenza di crescita di Sprite/texture/
materiali. La prova fisica M10-09 rimane distinta.
