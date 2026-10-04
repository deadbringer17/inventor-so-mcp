# Pairing Windows e pulsante Inventor — specifica

Data: 4 ottobre 2026. Stato: approvata dall'utente e implementata nel software; collaudi live aperti nel verbale `docs/pairing-windows-verification.md`.

## 1. Obiettivo e soluzione proposta

Associare Inventor XR SO al PC senza aprire un terminale: l'utente apre una finestra, scansiona un QR oppure inserisce indirizzo e codice sul Quest, quindi vede l'esito dell'associazione.

Proposta: una sola applicazione Windows, **Inventor SO — Connessione visore**, accessibile dal menu Start e dal pulsante **Associa visore** dell'add-in Inventor SO 2027. Il pulsante avvia o porta in primo piano la stessa finestra. La logica di pairing resta nel server HTTP; l'add-in non genera credenziali e non ospita il server dentro Inventor.

La specifica distingue associazione del dispositivo al server, collegamento MCP e disponibilità di Inventor: sono tre stati diversi. Un dispositivo associato non è necessariamente connesso, e un server raggiungibile non implica un documento CAD disponibile.

## 2. Base esistente verificata nel repository

- `bridge/src/server-http/Program.cs`: `--pair <nome>` apre il pairing durante l'avvio del processo; stampa i dettagli in console e genera un PNG. Non espone un controllo locale per rigenerarlo durante l'esecuzione.
- `Pairing/PairingStore.cs`: QR con segreto casuale e codice manuale di sei cifre; validità di due minuti; un solo utilizzo condiviso tra i due percorsi; cinque segreti errati chiudono la finestra. `Open` sostituisce i segreti precedenti.
- `Pairing/PairingSetup.cs`: payload QR versione 1 con `host`, `port`, `ott`, `cert_sha256`; enumerazione IPv4 delle interfacce attive.
- `Pairing/PairingEndpoint.cs`: `POST /pair` scambia il segreto con un bearer token permanente, aggiornando il registro e il file dei token; evento `Paired` a esito positivo.
- Il client Quest supporta QR con pinning TLS e inserimento manuale con confronto dell'impronta prima di inviare il codice.

Riferimenti: spec M1 `2026-09-26-inventor-xr-so-m1-design.md`, §3.1 e §4.1; `Inventor XR SO/README.md`, primo collegamento; `bridge/CLAUDE.md`, selezione del target e architettura HTTP.

## 3. Flusso utente

1. Aprire **Associa visore** da Inventor oppure **Connessione visore** dal menu Start.
2. La finestra cerca il server gestito per l'utente Windows; se assente, lo avvia in background e attende che HTTPS e controllo locale siano pronti.
3. Mostrare il PC, l'istanza Inventor e l'indirizzo di rete. Con più indirizzi LAN plausibili, richiedere una scelta senza pubblicare un QR basato arbitrariamente sulla prima interfaccia.
4. L'utente preme **Genera codice**. La finestra presenta QR, codice, indirizzo con porta, impronta TLS e conto alla rovescia.
5. Sul Quest: **Associa PC → Scansiona QR**, oppure **Inserisci codice**, indirizzo e porta, confronto dell'impronta e sei cifre.
6. Dopo la registrazione persistente della credenziale, mostrare **Visore associato** e rimuovere QR e codice. Mostrare separatamente se il Quest ha aperto una sessione e se Inventor è disponibile.
7. Alla scadenza mostrare **Codice scaduto** con **Genera nuovo codice**. Non rigenerare automaticamente: l'utente potrebbe avere già iniziato a inserirlo.

La generazione richiede un'azione esplicita, anche quando la finestra viene aperta dal pulsante Inventor.

## 4. Contenuto della finestra

Finestra ridimensionabile, testi italiani, utilizzo con mouse e tastiera, supporto DPI Windows. QR ad alto contrasto, con margine libero e senza decorazioni sovrapposte; possibilità di ingrandirlo per la scansione.

```text
Inventor SO — Connessione visore

PC: UFFICIO-CAD           Server: pronto
Inventor 2027: disponibile
Indirizzo: [192.168.1.20:8443    v]

          [ QR grande ]

Codice manuale: 123 456
Valido ancora: 01:42

Sul visore scegli “Associa PC”. Per il codice manuale,
inserisci anche l'indirizzo e confronta l'impronta.

Impronta certificato: [SHA-256 completo, raggruppato]

[Copia indirizzo] [Genera nuovo codice] [Annulla pairing]
```

Le cifre mostrate sono un esempio. Il raggruppamento visivo non cambia il codice trasmesso. L'impronta completa deve essere consultabile senza troncamenti e corrispondere al formato mostrato sul Quest.

Cambiare indirizzo durante un pairing invalida la finestra precedente e richiede una nuova generazione. **Genera nuovo codice** invalida immediatamente entrambi i segreti precedenti. Chiudere la finestra annulla il pairing aperto ma lascia il server disponibile alle sessioni esistenti. Se l'annullamento non è confermato dal server, l'interfaccia non dichiara il codice invalidato: resta il limite del TTL.

## 5. Architettura proposta

### Applicazione Windows

Nuovo progetto C# Windows separato, indicativamente `bridge/src/pairing-desktop/`, con WinForms come proposta iniziale. Framework e packaging definitivi vanno confermati nel piano di implementazione in base ai runtime distribuiti. Nessun riferimento all'API Inventor nell'applicazione.

Responsabilità: finestra, rappresentazione del QR, scelta rete, avvio del server e invocazione del controllo locale. Una sola finestra per utente; successive aperture la portano in primo piano. Il QR viene renderizzato dai dati della finestra corrente, senza leggere un PNG condiviso che potrebbe essere scaduto.

### Server HTTP

Introdurre un servizio di controllo locale, proposto su Named Pipe Windows con ACL limitata all'utente proprietario, identificata per utente e profilo server. Gli endpoint amministrativi non vengono esposti sulla LAN e non usano il codice breve come autorizzazione.

Operazioni proposte, con protocollo versionato e risposte strutturate:

| Operazione | Risultato |
|---|---|
| Stato | Identità del server, HTTPS pronto, target, pairing corrente ed esito; nessun bearer token |
| Apri pairing | Identificatore della finestra, payload QR v1, codice, indirizzo, scadenza e impronta |
| Annulla pairing | Invalidazione della finestra indicata |
| Segui stato | Aggiornamenti o polling limitato per scadenza, annullamento ed esito |

Il server resta l'unica autorità per segreti, TTL, consumo e persistenza. Identificatore di finestra obbligatorio per annullamento e aggiornamenti, così un messaggio tardivo non cancella o sovrascrive un pairing nuovo. Una sola finestra di pairing attiva per profilo server.

`POST /pair` e il payload QR v1 mantengono la compatibilità con il Quest attuale. Il server gestito deve poter aprire pairing a runtime, anche con registro token inizialmente vuoto, senza riavviarsi. La CLI esistente resta utilizzabile; un server senza controllo locale compatibile viene segnalato senza avviare un duplicato sulla stessa porta.

La registrazione deve essere completata in modo coerente: nessun esito positivo prima della persistenza; su errore di scrittura niente token attivo orfano e messaggio recuperabile. Il codice attuale registra il token in memoria prima di scriverlo nel file: questa sequenza va gestita nel refactoring. Nessuna riemissione implicita di un token già consegnato dopo una risposta di rete persa; un nuovo tentativo richiede una nuova finestra.

### Pulsante Inventor

L'add-in SO 2027 registra **Associa visore** in un pannello dedicato, disponibile anche senza documento aperto. Il click apre il programma e comunica l'identità dell'istanza chiamante senza trasferire token o codici nella riga di comando. Nessuna attesa di rete o avvio processo bloccante sul thread STA. Registrazione idempotente e rimozione dei callback alla disattivazione dell'add-in.

## 6. Avvio, rete e scelta Inventor

- Riutilizzare certificato e registro credenziali persistenti del profilo; non cambiare impronta TLS a ogni avvio.
- Verificare la prontezza effettiva del listener prima di mostrare un codice. Una porta occupata produce un errore esplicito, non un QR per un server sconosciuto.
- Nessun QR con `0.0.0.0`, loopback o indirizzo non più assegnato. Mostrare scheda e IPv4, consentendo scelta manuale tra gli indirizzi validi; selezione iniziale preferita sull'ultima scelta ancora disponibile.
- Il controllo locale verifica il server sul PC, non la raggiungibilità dal Quest. Le indicazioni su stessa rete, isolamento Wi-Fi e firewall sono diagnostica; non dichiarare la LAN collaudata senza prova dal dispositivo.
- Nessuna regola firewall ampia creata implicitamente. Eventuali configurazioni di installazione vanno definite separatamente.
- Con una sola istanza 2027, il profilo ordinario può usare `--target 2027`, seguendo la politica di riavvio già presente. Con più istanze, chiedere una scelta e usare un profilo esplicitamente vincolato al target: nessun cambio silenzioso del target di un host con sessioni attive.
- Se il pulsante viene premuto da un'istanza diversa da quella servita, mostrare la discrepanza. Nel primo rilascio consentire un solo profilo attivo gestito dalla finestra; non avviare più host o spostare sessioni automaticamente.
- Senza Inventor, consentire l'associazione al PC e mostrare **Apri Inventor per visualizzare un modello**. Senza documento, mostrare **Apri un documento**.

## 7. Stati, errori e credenziali

Stati UI: **Avvio server**, **Pronto**, **In attesa del visore**, **Scaduto**, **Annullato**, **Visore associato**, **Errore**. Disponibilità Inventor e sessione MCP restano indicatori separati. **Visore connesso** richiede evidenza di una sessione recente, non il solo evento `Paired`; se tale evidenza non è implementata, mostrare **Stato connessione non disponibile**.

Errori comprensibili per: server mancante, runtime mancante, porta occupata, rete assente, target ambiguo, certificato illeggibile, registro non scrivibile, controllo locale perso e cinque tentativi errati. Esporre codici strutturati al software; niente parsing delle stringhe della console.

Ogni nuovo dispositivo usa un nome tecnico univoco conforme al registro, senza chiedere all'utente di inventarlo. Il nome inviato dal Quest è un'etichetta non verificata. Per la prima versione, non sostituire automaticamente credenziali già registrate; gestione, rinomina e revoca dispositivi sono una fase successiva.

QR e codice sono visibili solo durante la finestra valida; niente bearer token permanente nell'interfaccia, negli argomenti dei processi o nei log. Nuovi dati di pairing restano in memoria; eventuali artefatti prodotti dal percorso CLI mantengono una politica esplicita di pulizia anche su scadenza e annullamento. File persistenti e controllo locale sono accessibili all'utente proprietario. TLS e pinning restano obbligatori per il pairing LAN.

## 8. Criteri di accettazione

| Gate | Criterio |
|---|---|
| P01 | Menu Start e pulsante Inventor aprono la stessa finestra, senza duplicare server e pulsanti |
| P02 | QR v1 riscattato dal client esistente; associazione persistente riutilizzabile dopo riavvio |
| P03 | Percorso manuale completo con IP, porta, impronta e sei cifre, inclusi zeri iniziali |
| P04 | Scadenza a 120 secondi, monouso condiviso QR/codice, blocco al quinto errore |
| P05 | Rigenerazione e annullamento invalidano i vecchi segreti; eventi tardivi non alterano il nuovo pairing |
| P06 | Associazione successiva senza riavviare host né interrompere sessioni esistenti |
| P07 | Più schede di rete e perdita dell'indirizzo producono scelta o errore coerenti |
| P08 | Nessun cambio implicito di target con più istanze; stato corretto senza Inventor o documento |
| P09 | Errori di persistenza e richieste concorrenti non producono successi falsi o token orfani |
| P10 | Controllo amministrativo inaccessibile a un altro utente Windows e dalla LAN; log privi dei nuovi segreti |
| P11 | Finestra e ribbon utilizzabili a DPI 100%, 150% e 200%, con focus da tastiera e QR ingrandibile |
| P12 | Prova fisica sul Quest: QR leggibile sul monitor e percorso manuale completabile |

Verifiche separate: unitari e host con FakeAddIn per protocollo, stati e concorrenza; controllo su Windows reale per avvio, ACL, DPI e ribbon; runner Quest per i sottocasi automatizzabili; persona con Quest per scansione reale, confronto impronta ed ergonomia. Le prove non eseguite restano aperte; la presente bozza non certifica alcun gate.

## 9. Sequenza proposta

1. Servizio di pairing a runtime e controllo locale: stato, apertura, annullamento, persistenza coerente e test di concorrenza.
2. Finestra Windows: riuso/avvio host, selezione rete, QR, codice, scadenza ed errori.
3. Pulsante Inventor e packaging: stessa applicazione, registrazione ribbon, collegamento Start e diagnostica installazione.
4. Collaudo Windows/Inventor/Quest e verbale con distinzione tra evidenza automatica e prova fisica.

Scelte approvate: una finestra condivisa fra Windows e Inventor; WinForms .NET 8 Windows con distribuzione self-contained x64; un profilo gestito attivo; gestione e revoca dispositivi rimandate. Piano: `../plans/2026-10-04-pairing-windows-inventor.md`. I gate hardware restano aperti fino alla verifica effettiva.

## 10. Estensione richiesta: scollegare server indesiderati

In seguito al conflitto sulla porta 8443, il 4 ottobre l'utente ha richiesto
un comando per scollegare copie indesiderate. La UI aggiunge **Scollega altri
server**, con selezione esplicita dei backend HTTP Inventor SO del medesimo
utente. L'arresto riguarda solo i backend, interrompendone le connessioni;
Inventor e i documenti rimangono aperti. Prima dell'arresto l'identità viene
ricontrollata; processi estranei e altri utenti sono esclusi. Dopo l'arresto
la UI riprova l'avvio del profilo di pairing. Il server attualmente gestito può
essere selezionato per riavviare con un'altra istanza Inventor.
