# Reality Capture RC4 — riferimento Inventor e overlay

Data: 25 ottobre 2026. Richiesta: sviluppare RC4 prima del collaudo sul Quest.
Estende RC3 senza modificare i raw o gli artefatti immutabili del solve.

## Coordinate e allineamento

`ScanRegistration` condivide il sorgente matematico tra core XR e libreria PC.
Le matrici sono row-major, vettori colonna e traslazioni in mm. La scansione
è Unity LH, Inventor è RH: `H = diag(1,1,-1,1)` cambia esplicitamente base.
La matrice salvata `scan_metric_to_inventor_mm = R * H` ha determinante -1,
senza introdurre scala o deformazione. L’inversa rigida conserva i punti fuori
dal piano usato per la registrazione.

Tre punti: corrispondenze ordinate, almeno 1 mm di separazione, non collineari;
frame da origine e direzioni, senza adattare la scala. Residui massimi oltre
5 mm vengono rifiutati dalla UI; l’API consente una tolleranza esplicita fino
a 50 mm. Piano/asse: normale, direzione con componente tangente e punto
origine corrispondente. Asse quasi normale al piano viene rifiutato perché
non determina lo yaw; angoli piano/asse incompatibili vengono rifiutati. Le coordinate CAD vengono inserite manualmente; non
sono riferimenti topologici opachi del modello. Metodo `scan_basis` per una
parte nuova: solo cambio di base H, senza dichiarare un allineamento fisico.

Il server rilegge punti e primitive di proprietà del token e salva fino a
32 snapshot di allineamento per solve, con CAS, revisione annotazioni,
documento/revisione CAD dichiarati, riferimenti, coordinate di controllo,
matrice e residui. File pubblicati mediante rename. L’allineamento è un dato:
l’identità CAD viene verificata nuovamente prima del suo uso e nel batch nativo.

Tre tool sperimentali aggiunti: `get_alignment`, `align_to_cad`,
`create_reference` (preparazione delle operazioni, senza modifica CAD).
Il toolset capture ha 17 tool. Payload preparato soltanto da dati posseduti,
massimo 900 kB, 2000 punti, 32 primitive e 256 quote manuali abilitate.

## Parte di riferimento

La UI crea una nuova `.ipt` attraverso il ciclo workspace esistente. Una
registrazione corrente può mantenere il frame del CAD originale; altrimenti
la parte usa la base della scansione. Il frame di provenienza può appartenere
a un altro documento: la destinazione del piano rimane vincolata all’identità
e alla revisione della nuova parte. Il nome univoco non sovrascrive file.

`create_reality_reference` è un nuovo comando batch sperimentale, compilato
solo nell’add-in sperimentale e accettato soltanto dentro la sua transazione
atomica posseduta. Rifiuta destinazioni fuori dal workspace e parti con
corpi, feature o schizzi esistenti. Produce:

- struttura BOM Reference;
- point cloud in uno schizzo 3D nativo, punti non classificati come centri foro;
- work point, assi e piani fissi dalle primitive selezionate;
- parametri distinti per diametri/estensioni stimati e quote esterne note;
- attributi persistenti con punti, matrice, revisioni, hash, primitive,
  incertezze esterne, report e unità.

Conversione mm → cm soltanto alla frontiera Inventor. L’importazione non crea
B-Rep. La mesh sparsa GLB/PLY/OBJ rimane un artefatto separato: non viene
importato un `MeshFeatureSet` e non viene aggiunta geometria solida per simulare
una mesh. La parte nativa contiene punti e work geometry, utili come riferimento.

Anteprima tramite `inventor_plan_change`, eseguita e annullata da Inventor con
rebuild e feature health. Il client richiede prova di rollback, documento,
revisione, operazioni immutate e scadenza. Il modello di anteprima visualizzato
resta la scansione RC3; non si dichiara una nuova mesh B-Rep dal preview.
`Applica riferimento` consuma il piano una volta; un esito incerto non viene
ritentato automaticamente. Salvataggio separato, esplicito, nel workspace.
Annullare una preview lascia la parte vuota già creata: la creazione del file
non è una transazione di modellazione e non viene annullata cancellando file.

API verificate nella documentazione primaria Autodesk e tramite compilazione
contro l’interop installato: [WorkPlanes.AddFixed](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/WorkPlanes_AddFixed.htm),
[SketchPoints3D.Add](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/SketchPoints3D_Add.htm),
[UserParameters.AddByValue](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/UserParameters_AddByValue.htm).
La compilazione non costituisce collaudo COM dal vivo.

## Confronto e overlay

Una copia separata dello snapshot CAD viene renderizzata senza `CadBody` o
collider. Le occorrenze della stessa definizione condividono le mesh;
limite di un milione di vertici unici e 2000 occorrenze. Il confronto ridotto usa la stessa trasformazione di sola vista
della scansione. Il CAD operativo, input e voce rimangono sospesi.

Per il passthrough 1:1 occorrono tre nuovi punti fisici nel tracking corrente.
La UI acquisisce l’origine del controller dominante; **non è una punta
metrologica calibrata**. L’offset e la precisione devono essere collaudati.
Nessuna posa dei raw viene riutilizzata automaticamente dopo una riapertura.
La registrazione fisica è LH→LH, determinante +1 e vive solo nella sezione aperta.

Da CAD Unity/metri a mondo:
`trackingWorld * trackingFromScan_m * inverse(scanToCad)_m * H`.
Scala tracking esattamente 1:1, senza riflessioni; snapshot CAD completo richiesto.
Perdita tracking/recenter invalidano anche una registrazione non ancora
visualizzata. Documento/revisione/visual revision o PC cambiati rimuovono la
copia CAD. Home e pausa eliminano registrazione, piano e geometria temporanei.

## Gate

| Gate | Software | Collaudo distinto |
|---|---|---|
| RC4-01 | Tre punti, piano/asse, inverse, unità e assenza scala | Allineamento su oggetti reali |
| RC4-02 | Ownership, snapshot, hash, CAS e limiti | Tracciabilità del banco fisico |
| RC4-03 | Parte workspace, preview/commit, rollback e revisioni | Esecuzione nativa su fixture Inventor |
| RC4-04 | Punti, work geometry, parametri e provenienza | Native geometry, save/reopen e Undo dal vivo |
| RC4-05 | Copia CAD indipendente, senza picking CAD | Prestazioni e leggibilità sul Quest |
| RC4-06 | Registrazione fresca, 1:1 e invalidazione tracking | Controller, recenter e overlay fisici |
| RC4-07 | Cancellazione, risposta incerta e cleanup | Lifecycle dispositivo |
| RC4-08 | Runner sintetico preparato e compilato | Esecuzione runner Quest |

Il runner M1 aggiunge matematica e copia grafica della sua fixture dedicata,
senza importazione nativa. I relativi `NOT COVERED` rimangono espliciti.
Tutta RC4 resta sperimentale fino al collaudo nativo e fisico. Non promuove
misure a VALIDATED e non chiude i gate fisici RC1–RC3 o MVS denso RC2.
