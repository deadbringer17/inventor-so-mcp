# S1 — Percorso immagini e build cumulativa, incremento 53

14 ottobre 2026. Avanzamento F03; **18 gate S1 aperti**.

La verifica SHA/CRC e la copia del PNG avvengono ora su un worker. L'await
mantiene il contesto del chiamante per consegnare il risultato al renderer;
annullamento e fallimento azzerano sia la risposta sia eventuali copie
private. Due test con PNG RGBA validi da oltre 8 MiB verificano che il
chiamante possa riprendere il lavoro e annullare prima della conclusione
della scansione. **1.080 test Core PASS**.

La suite Unity finale supera **1.057 test, zero FAIL/SKIP**, in una sola
run completa. Il test immagini passa attraverso lo shader effettivo e una
RenderTexture, controllando i due colori letti dalla GPU. La prima run
completa conserva tre timeout: 300 cicli dell'editor duravano circa
11–156 ms, quindi non erano un'attesa corretta per il worker. La fixture
usa ora una scadenza di 30 secondi, senza modificare il comportamento del
prodotto o le tolleranze geometriche. La run mirata successiva passa 7/7;
quella completa finale passa 1.057/1.057. Sorgenti e assembly del diagnostico
e della versione finale sono conservati separatamente.

La [prova HTTP nativa](s1-native-image-http53.md) aggiunge **due casi PASS**,
Parte e Lamiera: server MCP reale, add-in reale in processo dedicato, asset
PNG HTTPS autenticato e backend/client Core di produzione. Verifica sei
pixel RGBA, frame dopo rotazione di 37° e specchiatura, rifiuto del token di
un altro proprietario e stato/revisione CAD invariati. Il trasporto di questa
sonda è .NET con pin TLS; non è UnityWebRequest né un test sul Quest.

Sono compilate due APK cumulative, conservate in
`artifacts/s1-verification/increment53/apks`:

- `InventorXrSo-s1-ordinary.apk`: SHA256
  `F5BF9C02C0C432AC79F5C6D52D78C7357CC3FFF95626617FC4284DE80E5AD8FF`.
- `InventorXrSo-s1-acceptance.apk`: SHA256
  `C21E74F12F05558686CF14D3B1DA8871B847509D2A1D5525C9F7036C94D42DE7`.

Nessuna APK installata: ADB non rileva dispositivi collegati. Il runner QA
S1 conserva il suo sottoinsieme precedente; non certifica nuovi casi immagini
soltanto perché il nuovo renderer è incluso nella build. Il manifest/DLL
installati sul PC restano quelli originali, ripristinati dopo ogni sonda.
Il processo Inventor già aperto dall'utente viene preservato per PID e data
di avvio, senza leggere o modificare i suoi documenti.

Rimangono selezione/mutazione dei bordi strutturali, integrazione completa
dell'importazione nell'editor, linked, trasparenza, altri formati, cronologia
OLE, runner immagini sul Quest e prova fisica. Le evidenze 51–53 non
sostituiscono i requisiti dei 77 comandi o l'audit dei 18 gate complessivi.
