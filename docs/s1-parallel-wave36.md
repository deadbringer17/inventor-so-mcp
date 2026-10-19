# S1 — Filtri dei vincoli, inspector BMP e relazioni dei centri

12 ottobre 2026. Incremento 36 dei pacchetti D04/F03/C02 del
[piano parallelo](superpowers/plans/2026-10-11-s1-pacchetti-paralleli.md).
Il perimetro resta di 77 comandi e 18 gate aperti. Nessuna dichiarazione di
parità completa, installazione Quest o distribuzione DLL.

## D04 — Filtri nel prodotto Parte/Lamiera

La pagina Visualizza vincoli, raggiungibile dalle opzioni dei vincoli,
espone i tipi nativi presenti nello snapshot e il ripristino Tutti i tipi.
La prima esclusione parte da tutti i tipi correnti; un insieme vuoto nasconde
tutti i vincoli non selezionati. Il vincolo selezionato resta visibile quando
posizionabile. Il filtro è stato locale di vista, senza mutazioni CAD.

Stato visualizzazione distingue annotazioni mostrate, filtrate, oltre budget,
senza posizione, non supportate e associazioni indisponibili. Snapshot/layout
scaduti o assenti producono un motivo esplicito. Cambiare filtro revoca la
scelta fra candidati sovrapposti; refresh revoca anche callback dello snapshot
precedente. Sei nuovi casi EditMode coprono entrambi i workspace, insieme
vuoto, reset, selezionato escluso, sovrapposizione, revisione e budget.

Suite finale `unity-visibility36-final.xml`: **1.030 PASS**, zero falliti o
saltati. Il primo run aveva 1.028 PASS e due fallimenti del limite di 16
azioni: il nuovo accesso portava Vincola a 17. Spostato nelle opzioni già
esistenti, mantenendo il limite e conservando il diagnostico iniziale.
Simboli equivalenti al desktop, tutte le associazioni native, prestazioni,
runner Quest e prova fisica restano da completare.

## F03 — Inspector reale dei byte BMP

`BmpImportContentInspector` valida un sottoinsieme esplicito: header BM e
BITMAPINFOHEADER da 40 byte, BI_RGB a 24 bit BGR o 32 bit BGRX, righe top-down
o bottom-up, padding DWORD, pixel immediatamente dopo l'header e fine file
esatta. Rifiuta header/pixel troncati, overflow, palette, compressioni,
maschere, gap e trailer. Il byte X non è interpretato come alpha.

Dimensioni massime e numero di pixel sono forniti dal chiamante; nessun
budget di produzione viene dedotto dai test. Restituisce metadati immagine,
senza decodifica/rendering o conversione delle dimensioni DPI in geometria.
Non abilita automaticamente importazioni nella CLI. Test del parser e
dell'host loopback coprono byte reali, metadati, upload/fetch, rifiuti e scope.
Suite `bridge-bmp36.trx`: **1.337 PASS**, zero falliti o saltati, 52 casi in
più rispetto all'incremento 35. Il log conserva il warning preesistente
sull'ID duplicato di un caso di revisione; non è una nuova copertura.

## F03 — Ricerca nativa immagini e OLE

La sonda `arc-ui-bmp-images` usa solo la fixture temporanea posseduta e
inizialmente priva di geometria, immagini e descrittori OLE. Quattro BMP
3×2 pixel, 24/32 bit e due orientamenti, vengono provati con collegamento
attivo/disattivo: **otto casi Parte e otto Lamiera**. Tutti completano
inserimento, proprietario/link, posizione, dimensioni uniformi, rotazione
di un quarto di giro e inversa, identità e Delete. Non è un confronto con
il comando desktop o con il percorso di importazione del prodotto.

**Il solo Abort non ripristina i descrittori OLE in nessuno dei 16 casi.**
Delete rimuove l'immagine, ma resta un descrittore senza FullFileName.
La prima pulizia basata sul percorso si ferma correttamente: diagnostici
`bmp36-sheet.json`, `bmp36-part-final.json` e `bmp36-part-ole-diagnostic.json`
conservati come fallimenti, distinti dai precedenti rifiuti prima di Add.

La versione finale richiede baseline OLE vuota e registra le chiavi native
immediatamente dopo Add. Dopo Abort rimuove esclusivamente descrittori con
quelle chiavi; se un percorso è ancora presente deve appartenere ai file
creati dalla chiamata. Identità sconosciute sono rifiutate. Il report mantiene
`exact_rollback=false` e registra separatamente la riparazione:
**16/16 ripristini esatti dopo pulizia esplicita**, zero divergenze nei
controlli API e cleanup dei file temporanei riuscito.

Report finali: `bmp36-part-identity.json` e `bmp36-sheet-identity.json`.
La riparazione è una strategia verificata nella fixture isolata: gestione
di descrittori preesistenti, preview/commit del prodotto, persistenza dei
file collegati, aggiornamenti esterni, selettore Quest e altri formati
rimangono aperti. Rotazione numerica arbitraria non dimostrata.

## C02 — Grafo nativo dei centri e due aggiornamenti

Dodici casi per contesto confrontano sweep +90°/-270°, legami automatici e
due ordini di Merge, con/senza linea collegata al centro originale. La sonda
legge EntityOne/EntityTwo dei vincoli Coincident/Concentric, chiavi e ruoli,
prima/dopo ricostruzione e cancellazione. Due spostamenti dell'ancora devono
propagarsi a entrambi i centri; nessun legame viene dedotto dalla vicinanza.

Parte e Lamiera hanno lo stesso esito: **quattro casi completati e otto
divergenze ciascuno**, tutti i 24 Abort ripristinano esattamente la baseline.
Riescono entrambi gli ordini di Merge quando la linea conserva l'ancora;
i legami automatici e il Merge senza linea non garantiscono la propagazione
dall'identità originale. Le chiavi dei sopravvissuti devono essere lette.
Report `relations36-part-final.json` e `relations36-sheet.json`; assembly
eseguiti congelati separatamente da quelli della successiva sonda BMP.
Trasferimento completo dei vincoli/dipendenze, oracle desktop e comandi
Taglia/Estendi/Dividi nel prodotto rimangono aperti.

## Consegna e limiti

Core invariato ai **941 PASS dell'incremento 34**, non rieseguito in questo
incremento. Sono 58 nuovi casi automatici fra Unity e bridge. La sonda di
stato ora riconosce anche le cinque fixture Oracle, oltre a S1_Probe_: il
precedente messaggio «nessuna fixture» non dimostrava l'assenza delle Oracle.
`native36-final-state.json` conferma indipendentemente **zero documenti aperti**.

APK cumulativa `InventorXrSo-s1-visibility36.apk`, build con marker
`Build Finished, Result: Success.` e processo Unity terminato. Il vecchio
handle di esecuzione non è più disponibile: il codice finale del processo
non è recuperabile, quindi non viene dichiarato. APK da 53.946.130 byte,
SHA-256 `ABB5FBDF18DCF0A041E6CF5E73C0DD20650873A8F1A06BE97F2C59FDF366C784`.
Non installata sul Quest. Sorgenti, assembly, risultati, log e diagnostici
sono congelati e indicizzati nel manifest 36; nessun gate complessivo chiuso.
