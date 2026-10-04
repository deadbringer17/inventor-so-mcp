# M8 grapics — design system HIVE A.P.E. per Inventor SO

Data: 4 ottobre 2026, Europe/Rome. Stato: **implementazione software in verifica**;
risorse di riferimento e font ufficiali acquisiti. Collaudo Quest e fisico aperto.
Nome della milestone richiesto dall'utente: **M8 grapics**.

Decisioni esecutive: font statici ufficiali Satoshi Medium/Bold, estratti integri
dall'archivio Fontshare (FFL inclusa), nessuna conversione del font variabile.
Palette invariata a 160×110 mm; interlinea TMP −52 per conservare due righe a
7 mm. Anello con raggio 172 mm e celle 144×50 mm, testo 14 mm; barra 560×68 mm.
Posa, ancoraggio e input restano quelli M6. Le quote schizzo mantengono 1 mm
per unità UI indipendentemente dalla scala CAD, evitando cap height dimezzata.
Il maggiore ingombro di anello/barra resta soggetto al collaudo M8-08.
Home mantiene 820×620 mm come minimo e si estende verticalmente quando
tastiera/istruzioni richiedono spazio, senza ridurre i caratteri. Backspace
mantiene id/callback e mostra `←`. Fallback CAD locale: Satoshi dinamico per
latino esteso, Liberation già incluso in TMP per greco/cirillico, con OFL;
nessuna modifica all'asset fallback TMP globale preesistente. CJK non incluso.

## Obiettivo e superfici

Adottare il linguaggio grafico EnerBot usato da HIVE A.P.E. nel plugin Inventor
SO, con priorità al client Quest: colori industriali, tipografia coerente,
pannelli con gerarchia chiara, pulsanti, chip e stati riconoscibili. Copertura:
guscio M6, quattro workspace, Home/pairing Quest, Browser/Proprietà/Risultati,
finestra Windows di associazione. Il viewer WebXR di supporto è fuori dal primo
rilascio M8. Nessuna nuova capacità CAD, modifica del protocollo o riprogettazione
dei gesti; valgono disposizione spaziale e regole di conferma di M6.

Riferimenti locali:

- [Snapshot e provenienza](../../../assets/design-system/hive-ape/README.md).
- [M6](2026-09-29-inventor-xr-so-m6-ux-spaziale-design.md),
  [M7](2026-10-03-inventor-xr-so-m7-inspect-verifica-design.md).
- [Pairing Windows](2026-10-04-pairing-windows-inventor-design.md).
- [Spec prodotto](../../../Inventor%20XR%20SO/inventor_meta_product.md),
  [accettazione Quest](../../xr-quest-acceptance.md).

Fonte GitHub: [APE_Hive al commit fissato](https://github.com/deadbringer17/APE_Hive/tree/436a52a1450be2f3330c055670515df20696f031).
`templates/base.html` carica `colors_and_type.css` e `app-overrides.css`.
Questi hanno precedenza sul README storico e sul kit marketing. In particolare
Satoshi ha sostituito Inter. Il manifest originale non è una prova della presenza
dei componenti JSX né di una libreria di icone CAD: tali file non sono nell'albero
GitHub esaminato. Esistono motivi grafici, pin e badge, non un set CAD riutilizzabile.

## Stato reale del client

- uGUI costruito in C#, scena generata; `UiFactory` centralizza canvas e controlli.
- `UiStyle` codifica i colori della barra M6; `ChipView` ha colori locali.
- `PaletteView`, `RingView`, `CommitBarView` e `HudView` usano TextMeshPro.
- `UiFactory` mantiene anche `Label` e `Button` basati su `UnityEngine.UI.Text`;
  **HomePanel usa ancora questi controlli legacy**, anche per il mirror vocale.
- La tavolozza misura 160×110 mm, due colonne di celle 75,5×20 mm; tastierino
  con celle 49×15 mm. Non aggiungere decorazioni consumando lo spazio delle label.
- `XrAction.Icon` esiste, ma il guscio attuale presenta etichette testuali.
- `TMP_Settings.defaultFontAsset` e `CapHeightRatio = 0.7` sono assunzioni attuali,
  da sostituire con riferimenti espliciti e metriche del font scelto.
- `bridge/src/pairing-desktop/PairingForm.cs` è WinForms, con layout e test propri;
  non condivide direttamente materiali o asset TextMeshPro con Unity.

## Fondazioni e adattamento

Valori originali sRGB da mantenere come token sorgente:

| Ruolo HIVE | Valore | Uso M8 |
|---|---|---|
| Fondo chiaro | `#f4f8fb` / `#eaf2f7` | card Home, liste e finestra Windows |
| Superficie | `#ffffff` | contenuto e input su fondo chiaro |
| Testo / secondario | `#102235` / `#4f6477` | testo leggibile su chiaro |
| Terziario | `#7a8a99` | metadati non essenziali; verificarne contrasto |
| Navy | `#0d2030` | fondo guscio XR e header Windows |
| Navy-teal | `#1a3a4a` | superficie secondaria XR |
| Teal / hover | `#326975` / `#285660` | pulsanti e identità, testo chiaro |
| Giallo segnale | `#fdd11b` | focus e azione primaria; testo navy |
| Accento su chiaro | `#c07a00` | decorazione; evitare testo essenziale senza verifica |
| OK web | `#2ee08e` / `#1a8a5a` | indicatore su scuro / testo su chiaro |
| Avviso web | `#fdd11b` / `#9a7700` | indicatore su scuro / testo su chiaro da validare |
| Errore web | `#f0637c` / `#b03050` | indicatore su scuro / testo su chiaro |

Profilo XR proposto: superfici navy quasi opache, testo `#f4f8fb`, comandi teal,
giallo solo per focus e CTA abilitata. Le liste dense possono usare card chiare
contenute, senza trasformare tutta la scena in una superficie bianca luminosa.
Windows: fondo chiaro con header navy. È un adattamento spaziale documentato,
non una conversione letterale dei layout web.

Token separati per `Brand`, `Surface`, `Text`, `Interaction`, `Status` e `CadOverlay`.
Il giallo di brand non significa automaticamente Stale; il verde OK non abilita
Applica. Ogni stato combina colore, etichetta e forma/marker.

Gli overlay CAD conservano il significato esistente: anteprima blu, interferenza
rossa, fantasmi trasparenti, highlight distinguibile. Un tema teal non deve
rendere identici preview, selezione, X-Ray e risultato di verifica.

## Componenti e tipografia

Satoshi è il sans di comandi, label, messaggi e titoli operativi; pesi 500/700
proposti. L'implementazione usa gli statici ufficiali su Editor/Android IL2CPP
e Windows: la FFL acquisita non autorizza modifiche ai binari, quindi nessuna
derivazione del variabile. Provenienza e hash sono registrati; nessun download
di font a runtime.

JetBrains Mono è opzionale per numeri, unità e impronta del certificato;
Playfair Display è opzionale per il titolo Home/Windows. Acquisire binari e
licenze prima dell'uso. Non usare serif per azioni, quote o risultati CAD.
Il primo tema operativo non dipende dall'acquisizione dei due font opzionali.

Creare asset TMP SDF dedicati e fallback deterministici: lettere accentate IT,
`°`, `²`, `³`, `≈`, `±`, `×`, `−`, `←`, `‹`, `›`, `…`, virgola decimale.
Le altezze delle maiuscole derivano dalle metriche reali, non dal rapporto 0,7.
Sulla tavolozza minimo 7 mm; piano/barra minimo 14 mm, secondo M6. Nessun
auto-shrink sotto soglia, ellissi o taglio delle etichette operative.

| Componente | Aspetto e comportamento richiesti |
|---|---|
| Pannello | navy/chiaro secondo profilo, bordo visibile, angoli sobri, titolo e contenuto distinti |
| Pulsante | normal, hover ray, pressed, selected/toggle e disabled distinti; stessa hit area |
| Tavolozza | header di scheda, stato selezionato, griglia invariata; label complete su due righe |
| Barra conferma | marker di stato e messaggio; Applica evidenziata solo quando il catalogo la abilita |
| Chip/tastierino | valore, unità, passo e campo armato leggibili; focus coerente col raggio |
| HUD/badge | stato + testo breve; nessun dato essenziale comunicato solo dal colore |
| Home | gerarchia PC/documento/connessione e input; TMP con mirror vocale ancora funzionante |
| Browser/Risultati | righe, focus e breadcrumb; distinguere distanza esatta e misura `≈` |
| Pairing Windows | header navy, corpo chiaro, azioni coerenti, codice e impronta leggibili |

Raggi web 6/12/20 px e scala 4/8/12/16/24/32 sono **proporzioni di riferimento**.
Definire i token XR in mm: proposta raggi 2/4/6 mm e spazi 2/3/6/8/12 mm,
da verificare dentro i layout esistenti. Nessuna equivalenza universale px→mm.
Hit target M6: almeno 15 mm sulla tavolozza e 25 mm sul piano.
Decorazioni non interattive con `raycastTarget=false`.

Nessuna libreria di icone obbligatoria nel primo passaggio. Motivi coerenti:
pin di stato, bordo attivo, separatore, chevron; eventuali icone CAD vengono
prodotte come task distinto e restano accompagnate dalle label. Evitare
glifi Unicode usati come surrogato di una libreria di icone.

## Stati e rendering

La macchina `CommitBarState` resta l'autorità. Vuoto nascosto; Bozza neutra;
Previewing blu; Ready marker verde; Stale ambra; Uncertain ed Error rosso;
Offline neutro/grigio; Applied verde. `UiStyle.For` diventa una risoluzione
semantica del tema, non una seconda macchina a stati. Non uniformare Error
e Offline solo perché il CSS HIVE associa entrambi alla famiglia rossa.

Contrasto obiettivo per testi essenziali: almeno 4,5:1, e marker di interazione
3:1, sulla coppia finale **composita**. È un controllo di progetto, non una
certificazione di leggibilità nel visore. Misurare anche disabled e metadati;
aumentare l'opacità dei fondi se il passthrough riduce il contrasto.

Unity è Linear: verificare i token sRGB attraverso import/materiale/render
target; evitare conversioni manuali doppie. Rounded panel con sprite 9-slice
e materiali UI esistenti, senza blur dello schermo, bloom o shader pesanti.
Target: UI della singola milestone senza nuove dipendenze web o framework.
Microtransizioni 150–240 ms, curva ispirata al DS; pose del guscio e transizioni
spaziali M6 restano quelle esistenti. Evitare movimenti dei target durante hover.

## Gate M8

| Gate | Criterio di accettazione | Evidenza |
|---|---|---|
| M8-01 | Risorse fissate, hash verificati, avvisi raccolti e font runtime derivati riproducibili | manifest + font report |
| M8-02 | Token unici, profili XR/Windows, contrasto misurato e resa sRGB/Linear verificata | report + gallery Unity |
| M8-03 | Font TMP, accenti/simboli, minima altezza e label complete; Home migrata senza perdere voce/input | EditMode + screenshot |
| M8-04 | Tavolozza/anello/barra/chip/HUD e quattro workspace coerenti, target/pose M6 preservati | EditMode + runner Quest |
| M8-05 | Tutti gli stati rappresentati; stale/incerto/offline/disabilitato non mutano CAD; Applica vocale bloccata | test e runner, fixture reale dove necessario |
| M8-06 | Pairing Windows con tema, DPI 100/150/200%, QR leggibile e quiet zone bianca, confronto impronta | test Windows + prova fisica QR/DPI |
| M8-07 | Regressioni pertinenti verdi, APK, screenshot e profilo UI confrontati alla baseline | suite + manifest, tempi/frame/memoria |
| M8-08 | Lettura e puntamento reali da seduto per 30 minuti, MR e Studio VR, quattro workspace | prova fisica Quest |

M8 non chiude retroattivamente i gate fisici M6/M7 o pairing ancora aperti.
Runner con input sintetico e prova umana sono evidenze diverse; ogni sottocaso
non esercitato va dichiarato `NOT COVERED`. Verbale: `docs/xr-m8-verification.md`.
