# S1 — Parametri nel pannello nativo Parte/Lamiera81

15 ottobre2026. Il run `increment75/device-r11` conclude il sottoinsieme
con **22 PASS**, nessuna asserzione fallita e cleanup interamente verificato.
Esito intenzionale **PARTIAL**, exit4: tutti i18 gate complessivi S1
restano aperti. I22 sottocasi non sono22 comandi della matrice77.

## Artefatti ed esecuzione

- [Log Quest](../artifacts/s1-verification/increment75/device-r11/s1-acceptance.txt)
  e [verbale di ripristino](../artifacts/s1-verification/increment75/device-r11/restoration.json).
- QA81/r2:94195329byte, SHA256
  `5E74D74B821B1530821D12B8CFD29B1D19FB552389F258D61E0238D9CE56D0B1`;
  build `runner81-apk-build-r2.log`, sorgenti e managed producer in
  `increment81/build-r2-source` e `increment81/qa-managed-r2`.
- Add-in80 esatto già verificato sul PC:
  `711D8451C6740F5290A67F48E2F07DD0319534CB226CD4EB83FBD157A39F16C4`.
-42 screenshot conservati dal wrapper, senza attribuire loro una prova
  di tracking, leggibilità o gesto fisico.

Il bootstrap malformato viene rifiutato prima della fixture; il bootstrap
valido monouso viene consumato dalla directory Android privata mode600.
HTTPS pinned usa `adb reverse` USB. Il limite ordinario240 richieste/minuto
resta attivo: nove pause65s fra gruppi QA,585s complessivi, evitano il burst.
Nessuna pausa, retry o ricostruzione nel reader viene inserita fra Applica e
il readback rigoroso richiesto. Il Quest si è addormentato durante una pausa
Parte ed è stato riattivato dall'utente: lo stesso run è proseguito, senza
ripetere commit.

Il tentativo precedente r10 si ferma al preflight ADB perché il dispositivo
non è collegato: nessuna installazione, fixture o asserzione CAD. R11 parte
solo dopo il nuovo collegamento confermato. La QA81/r1 compila, ma non viene
installata perché conserva il confronto esatto3mm difettoso scoperto in r9.
R2 usa la tolleranza1e-6mm per la creazione/rinomina del parametro3mm.

## Copertura effettiva

| Gruppo | PASS | Percorso verificato |
|---|---:|---|
| Linea Parte/Lamiera |8| Apertura, preview/cancel, commit e chiusura, già esercitati38 |
| Testo nativo readonly Parte/Lamiera |2| Glifi, bounds, colore, selezione/highlight e revisione invariata |
| Parametri in Progettazione |4| Valori tipizzati, Cancel, Apply20→35 e CRUD inutilizzato |
| Parametri dentro lo schizzo Parte |4| Lettura tipizzata, Cancel, Apply20→35, CRUD inutilizzato |
| Parametri dentro lo schizzo Lamiera |4| Stesso flusso nel pannello condiviso76 |

Il runner usa catalogo reale, tastierino testuale, `DesignSession`, preview
del prodotto e relativo consenso dopo rendering. Non prepara bozze né
conferma il rendering tramite scorciatoie del runner. Input e selezioni
restano **sintetici**.

Dentro ciascuno schizzo, Boolean/Text/lunghezza vengono modificati in
Anteprima e annullati: valori, testo e revisione restano invariati. Per la
lunghezza, due Apply verificano valore nativo, `L=20`/`L=35`, glifi cambiati,
stesso entity_id e snapshot corrente dell'editor. Il renderer effettivo
deve contenere gli stessi vertici/indici e bounds, con colore nativo e
nessun TMP sostitutivo. Creazione, rinomina ed eliminazione del parametro
inutilizzato richiedono ogni volta preview/Apply e nuovo readback;
identità, contenuto, bounds e glifi dei testi restano invariati.

## Ispezione e ripristino

L'[ispezione nativa finale](../artifacts/s1-verification/increment75/device-r11/host-public/inspect-001.json)
osserva Parte e Lamiera a35mm, `L=35`,153 vertici/143 facce per quel testo,
nessun parametro inutilizzato residuo e file baseline immutati. La lettura
preserva DatabaseRevisionId/Dirty: non salva le modifiche temporanee.

L'host chiude senza salvare solo le due fixture e termina normalmente:
PID owned107068 uscito, exit0. PID utente91056 con lo stesso start-time
preservato, senza acquisizione COM o lettura dei documenti personali.
Manifest e DLL ordinari verificati, bootstrap e reverse rimossi; APK38
reinstallata con hash remoto
`D8D6D341CCBDCF9BA06FF282908576CA3F5FCFCF5DFFAF6529AA68AC70B04F81`.
Tutti i14 cleanup registrati risultano true e `failures` è vuoto.

## Limiti

I1253 test Core e1119 Unity76 rimangono evidenza software distinta; questo
run aggiunge backend e renderer reali sul Quest al sottoinsieme descritto.
Non prova Apply Boolean/Text, modifica della proprietà Units esistente,
callback stale/offline indotti, tutte le opzioni/default desktop, Wi-Fi,
prestazioni o input fisico. Autore testo e testo su geometria, altri comandi,
Scheletro/3D e la matrice completa77 restano da completare e collaudare.
Il risultato non autorizza la promozione dell'intera milestone S1.
