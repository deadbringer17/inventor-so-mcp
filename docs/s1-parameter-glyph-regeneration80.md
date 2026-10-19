# S1 — Rigenerazione dei glifi dipendenti dai parametri80

15 ottobre 2026. Rapporto incrementale, non congelato. Tutti i 18 gate
complessivi S1 restano aperti.

## Difetto riprodotto

Il runner Quest75/r6 leggeva dopo Applica il valore `20 mm` e il testo
risolto `L=20`, ma la tassellazione nativa conservava i glifi precedenti.
L'ispezione COM successiva dell'host osservava invece glifi aggiornati:
quel risultato tardivo non annulla il FAIL della prima lettura.

La sonda `S1ParameterFirstRead80` usa HTTPS pinned e il vero Core, senza
acquisire COM, ricostruire il documento o eseguire un oracolo prima delle
prime due letture dopo ciascun commit. La conferma della preview è
**sintetica sul PC**: non è evidenza di rendering Unity o Quest.

| Tentativo PC | Risultato |
|---|---|
| first-read-r1 | Errore di setup del client prima delle mutazioni; mancava ConnectAsync sul backend. |
| first-read-r2 | Parte20/35: identità conservata e testo aggiornato, glifi invariati anche alla seconda lettura. Poi STALE_REVISION nell'inizializzazione del contesto Lamiera. |
| first-read-r3 | Leggere Width/Height prima di GetFacetsInfo non corregge i glifi. La modifica diagnostica al reader è stata rimossa. |
| first-read-r4 | Rebuild nella mutazione: Parte20/35 con glifi aggiornati e rollback della rappresentazione esatto; inizializzazione Lamiera ancora STALE_REVISION. |
| first-read-r5 | Quattro aggiornamenti20/35 Parte/Lamiera: identità conservata, testo e glifi aggiornati al primo snapshot, seconda lettura identica e revisione invariata dalle letture. |

R5 effettua un solo refresh iniziale del DesignContext Lamiera a fronte
di STALE_REVISION durante l'attivazione. Il refresh precede baseline e
mutazioni. Non esistono retry delle letture dopo Applica, né retry dei commit.
Le prove sono in `artifacts/s1-verification/increment80/first-read-r1…r5`.

## Correzione e ambito

Nel percorso Inventor2027 sperimentale, `SetParameterHandler` esegue
`doc.Rebuild2(false)` dopo l'assegnazione e Update, soltanto quando il
contesto contiene una transazione batch posseduta. Il backend atomico
verifica la proprietà della transazione prima dell'handler e nella
validazione successiva sulla stessa STA. Il reader resta privo di Update,
Rebuild e modifiche nascoste.

Rebuild2 ricalcola tutto il documento e si ferma al primo errore quando
AcceptErrors è false: aumenta il costo e può rilevare errori in dipendenze
che Update non ricalcolava. Questo limite va misurato su documenti più
complessi; non costituisce chiusura del gate prestazioni.
Riferimento: [API Autodesk2027, PartDocument.Rebuild2](https://help.autodesk.com/cloudhelp/2027/ENU/Inventor-API/files/PartDocument_Rebuild2.htm).

La DLL esatta provata ha SHA256
`711D8451C6740F5290A67F48E2F07DD0319534CB226CD4EB83FBD157A39F16C4`.
Il build non sperimentale e gli altri anni non ricevono la nuova chiamata.
La DLL ordinaria installata rimane invariata.

## Verifiche e rollback

- Suite bridge: **1660 PASS, 1 SKIP**, nessun fallimento; TRX in
  `increment80/bridge-tests-r1`.
- Sonda first-read-r5: quattro aggiornamenti reali con rollback esatto
  del testo rappresentato e della revisione eventi durante preview. Da
  sola questa sonda non osserva Dirty, stili o tutto lo stato nativo.
- Regressione69/r1 con la stessa DLL: dieci preview/commit, quattro
  aggiornamenti sorgente e otto rifiuti atomici Parte/Lamiera; snapshot
  nativi comprendenti parametri, identità, geometria, stili e glifi.
- Regressione69/r2 aggiunge due batch con una modifica valida a40mm,
  rigenerazione e successiva espressione non valida: ROLLED_BACK/API_ERROR
  e stato nativo/revisione esatti in Parte e Lamiera. Dieci rifiuti totali.

Gli snapshot della regressione r1 usano il producer69 congelato; r2
archivia il producer esteso e il relativo binario. Entrambi i processi
owned sono usciti e manifest/DLL ordinari e PID/start-time dell'istanza
utente sono stati preservati. Nessuna prova accede ai documenti utente.

## Quest e lavoro aperto

Quest75/r8 con APK78 e questa DLL supera il controllo rigoroso del primo
Apply20 e registra `L=20` con glifi aggiornati. Il gruppo20→35 non termina:
il run fallisce successivamente offline e il log sessione segnala rate
limiting. Rimangono **12 PASS** di sottocasi, senza attribuire un PASS
aggiuntivo al gruppo incompleto. Cleanup r8 interamente verificato.

Il runner79 aggiunge diagnostica prima delle assertion e pause65s fra
tre fasi QA, mantenendo il limite ordinario240 richieste/minuto. Nessuna
pausa o riparazione viene inserita fra Applica e primo snapshot. La nuova
build è verificata. Quest75/r9 supera entrambi gli aggiornamenti20→35,
con13 PASS di sottocasi, poi fallisce al confronto esatto del parametro
creato3mm contro3.0000000000000004mm. Il FAIL resta conservato; il runner81
corregge il confronto con tolleranza1e-6mm ed estende i casi al pannello76.

La [QA81/r11](s1-native-parameter-quest81.md) successiva termina con22 PASS,
inclusi aggiornamenti e CRUD dentro lo schizzo Parte/Lamiera, con cleanup
interamente verificato. Altre opzioni parametri, authoring testo, altri
comandi, confronto desktop, Wi-Fi, prestazioni e prove fisiche restano aperti.
