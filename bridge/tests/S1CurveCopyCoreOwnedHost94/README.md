# Host94 — copia curve tramite Core/HTTPS

Progetto nuovo, nessuna modifica di produzione. Clone delle guardie73/host90,
con adattatori nativi indipendenti92; nessuna esecuzione FSM/lifecycle90.

Il coordinatore compila in serie, poi conserva sorgenti e assembly effettivi
prima del run. Questa consegna non è stata compilata o eseguita dall'agente.

```powershell
dotnet build bridge/tests/S1CurveCopyCoreOwnedHost94 -c Debug
pwsh -NoProfile -File artifacts/s1-verification/run-copy-core94.ps1 -Run r1 -AddinAssembly <DLL-candidata93-esplicita>
```

Il wrapper richiede anche il client già compilato
`artifacts/s1-verification/increment94/client/bin/Debug/net8.0/S1CopyCoreHttpProbe94.dll`.
CLI host identica73: run-id, host127.0.0.1, output-dir privato nuovo,
expected-addin-path/sha256, manifest-path/sha256, installed-dll-path/sha256.
Solo comandi stdin `bootstrap`, `inspect`, `copy-checkpoint94`,
`verify-copy94`, `stop`. Nessun comando accetta contenuti o percorsi arbitrari.

Il catalogo pubblico contiene chiavi degli8creator esatti per documento,
senza geometria/revisioni/token. La verifica finale richiede18workflow,
28coppie,10rifiuti e74checkpoint. Controlla digest e riferimenti di ogni
checkpoint, Abort/rifiuti full-native esatti e oggetti precedenti a ogni commit,
poi traduzioni/identità/glifi/proprietà e grafo intrinseco da membri nativi.
Nessun oracle o getter nativo è chiamato dal wrapper tra Apply e la prima
snapshot Core; il client richiede il checkpoint solo dopo quell'assert.

Gli originali su disco vengono chiusi skip-save, senza SaveCopy/reopen94.
Sorgenti/binari, catalogo, report e74rawcheckpoint vengono archiviati;
bootstrap/token non vengono archiviati né stampati. Il wrapper ripristina
byte-per-byte il manifest e verifica DLL originale e cleanup normale.
Non termina processi con kill, non usa ROT o documenti utente.

PASS significa soltanto sottoprofilo dichiarato: curve libere cerchio/4archi,
linea/testo letterale, Parte/Lamiera, conferma PC sintetica, preview/apply e
rifiuti. Campi parametro copiati, variazioni sorgente/reopen, altri profili,
UI/Quest/fisico e18gate restano aperti. Proprietà opzionali native UNKNOWN
sono confrontate come osservazioni incomplete, senza dichiararle coperte.
