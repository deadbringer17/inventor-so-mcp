# S1 — Integrazione residua delle immagini native

12 ottobre 2026. Audit del codice, successivo alle ondate 37/38. Non è una
prova nativa o Quest. Il perimetro S1-12 comprende anche gli altri formati,
punti/curve e ACAD: questa tranche BMP non li sostituisce.

Aggiornamento incremento 49: quattro angoli identificati da chiavi native
persistenti, dati nel reader e DTO Core; 32 casi nativi e due copie riaperte
PASS, regressione 96/96 PASS. Stato partial conservato finché i pixel/texture
non sono verificati. [Rapporto](s1-image-corner-bindings49.md).

Aggiornamento incremento 48: download interno del payload persistito,
con binding completo e chunk limitati, verificato in 32 casi nativi.
Adattatore server con hash e target fissato; 27 test dedicati PASS.
Reader/UV/rendering e superficie di prodotto ancora aperti.
[Rapporto](s1-image-download48.md).

Aggiornamento incremento 46: i byte originali vengono conservati come
attributo binario persistente del modello; salvataggio/riapertura verificati
su Parte e Lamiera. Regressione 96/96 PASS; cronologia PARTIAL per OLE
conservato da Undo, con modello/Redo/payload verificati. Reader e rendering
ancora da collegare. [Rapporto](s1-image-persistence-and-history46.md).

Incremento 45: Add embedded e journal OLE integrati nel
batch, 96/96 casi nativi Parte/Lamiera PASS dopo tre diagnostici conservati.
La rotazione usa RotateSketchObjects: RotateLeft non ruota il bordo come
richiesto. Capability XR ancora non dichiarata; linked, reader/rendering,
preview visiva e Undo/Redo rimangono aperti. [Rapporto](s1-native-image-batch45.md).

Incremento 44: sender chunk collegato ai due ingressi,
registrazione interna add-in e loader di lease nel batch implementati.
Target fissato anche fra preview e commit; cleanup indipendente dalla
cancellazione. 1.506 bridge PASS, un SKIP; build 2027/legacy PASS,
senza installazione. [Rapporto](s1-import-transfer44.md).
Inserimento CAD non abilitato; restano journal OLE, Add, rendering e Quest.

Incremento 43: resolver owner-bound collegato a PlanChange e
AtomicBatch; lease acquisita prima del trasporto e mantenuta nel piano fino
al termine. Staging a chunk sigillato implementato e testato, ancora privo
di adapter wire. 1.451 bridge PASS, un SKIP; build add-in PASS, non installata.
[Rapporto](s1-import-ingress-and-staging43.md). Inserimento CAD non abilitato.

Incremento 42: ownership delle lease nei piani e controllo
dei messaggi/target implementati; inserimento CAD ancora da collegare.
La prova con OLE preesistente completa 56 osservazioni, ma rifiuta due casi
linked con sorgente condivisa: eliminare il nuovo descrittore può rompere
il riferimento precedente. [Risultati e residui](s1-import-lifetime-and-ole42.md).

Il prossimo percorso prodotto usa `edit_sketch`, variante `insert_image`,
con `asset_id`, `position_mm`, `width_mm`, `quarter_turns` e `link`.
Altezza derivata dal rapporto pixel; nessun percorso, URL o byte sorgenti
negli argomenti pubblici. Angoli arbitrari, specchiatura e scala non uniforme
richiedono prove ulteriori. I quarti di giro sono il sottoinsieme studiato.

## Lavoro coordinato necessario

1. `PlanningTools.cs` e l'ingresso `AtomicBatch` in `ParameterTools.cs`
   risolvono asset per owner autenticato, congelano hash/metadata e acquisiscono
   una lease. Entrambi gli ingressi devono applicare la stessa autorizzazione.
2. `ChangePlanStore.cs` possiede gli allegati e li rilascia su commit, discard,
   scadenza, eviction e fallimenti. Implementato negli incrementi 42/43.
3. `PluginClient.cs`, trasporto e `InventorCommandContext.cs` trasferiscono
   chunk verso il target fissato, con staging add-in hashato e sigillato.
   Collegamento software implementato nell'incremento 44, senza deploy live.
   NDJSON ammette 1 MiB per riga: validare l'intero messaggio serializzato,
   evitando una singola sorgente base64 fino a 32 MiB.
4. Un helper sperimentale `NativeSketchImages.cs` risolve solo allegati
   verificati del batch posseduto e applica `SketchImages.Add` sulla STA,
   con documento/revisione/sketch e posizione/dimensioni controllati.
   Embedded implementato e verificato nell'incremento 45; linked ancora aperto.
5. `AtomicBatchHandler.cs` acquisisce immediatamente un journal degli effetti
   OLE/file e ripara dopo Abort prima del ripristino della revisione.
   Riparazione incompleta significa `ROLLBACK_FAILED`. Journal embedded
   implementato nell'incremento 45; il caso linked condiviso resta da risolvere.
6. `SketchEditorXrHandler.cs`, snapshot Core e renderer Unity rappresentano
   l'immagine nativa e la sua preview CAD: oggi è unsupported/partial e la
   preview batch viene rifiutata. Il PNG nel pannello Risorse è un'altra vista.
7. `NativeSketchImportResources.cs` prepara una bozza con `SetOperation`,
   controllo di contesto/scadenza e capability CAD esplicita. Il client attuale
   richiede `cad_import_available=false`: capability e client cambiano insieme.

## Evidenza precedente e limiti

Il renderer/inspector verificato accetta BMP BI_RGB, DIB40, 24 BGR/32 BGRX,
top/down, con dimensioni/stride/lunghezza esatti e budget espliciti. Non sono
supportati palette, compressione, estensioni, gap o trailer.

Le sedici osservazioni Parte/Lamiera delle ondate precedenti coprono Add,
posizione, scala uniforme, quarti di giro e Delete. Abort lascia descrittori
OLE anche con `link=false`; la pulizia delle identità catturate dopo Add
ripristina quelle fixture, prive di OLE preesistenti. Servono ancora riferimenti
preesistenti, più immagini, errore dopo Add e Undo/Redo. Il getter pixel esterno
fallisce con E_UNEXPECTED; il PNG HTTP viene dai byte sorgenti.

Il deposito in memoria con TTL/lease non garantisce la vita documentale dei
file collegati. `link=true` richiede un deposito duraturo con ownership
documentale prima di essere abilitato. Il solo Add o il picker non costituisce
un percorso verificato Anteprima/Applica. Tutti i gate S1 restano aperti.
