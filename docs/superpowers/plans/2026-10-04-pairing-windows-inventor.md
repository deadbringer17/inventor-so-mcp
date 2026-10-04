# Piano: pairing Windows e pulsante Inventor

Spec approvata dall'utente il 4 ottobre 2026: `../specs/2026-10-04-pairing-windows-inventor-design.md`.

1. Aggiungere `bridge/src/pairing-control/`, libreria .NET 8 senza Inventor: DTO versionati e trasporto NDJSON limitato, Named Pipe con `CurrentUserOnly`, nomi derivati dall'utente/profilo. Test reali su pipe Windows.
2. Estendere PairingStore con id, stato, annullamento condizionato e riscatto serializzato con persistenza. Registrare il token dopo scrittura atomica; conservare protocollo `/pair`. Test TTL, concorrenza, rigenerazione, errore persistenza.
3. Aggiungere `--pair-control` al server HTTP; aprire il controllo solo dopo avvio HTTPS. Stato privo di segreti, apertura valida solo su un IPv4 LAN corrente, cancellazione con id; target descritti senza auth_token. Conservare CLI esistente e pulire PNG anche alla scadenza.
4. Aggiungere desktop WinForms .NET 8 Windows: singola istanza per utente, riuso host, avvio nascosto, scelta rete/target, QR in memoria, countdown, fingerprint, chiusura con annullamento confermato. Distribuzione desktop self-contained x64; host framework-dependent come pacchetto esistente.
5. Aggiungere hook di attivazione/disattivazione nella base add-in; implementare ribbon solo in SO27. Verificare firme contro interop installata e compilare default/experimental, senza installare sopra Inventor aperto.
6. Estendere build/install con desktop, server-http e collegamento Start. Conservare eventuali pacchetti precedenti. Documentare avvio, limiti e gate.
7. Eseguire test backend e core XR, build desktop/add-in, smoke Windows su finestra/pipe/HTTPS senza riscattare credenziali di produzione. Prove fisiche Quest e ribbon live restano aperte se non eseguite.

Contratto locale v1: `status`, `open` (host), `cancel` (windowId). Timeout e limite frame; errori strutturati. UI secondaria: `activate` (targetId). Un solo profilo gestito; target di un host esistente immutabile. Sessione MCP indicata come non disponibile nella v1, senza dedurla dall'associazione.
