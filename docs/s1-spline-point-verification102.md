# S1 — Ripresa della matrice punti spline, incremento 102

28 ottobre 2026. Verifica PC del candidato congelato dell'[incremento 98](s1-spline-point-edit98.md).
Chiusura del verbale: 29 ottobre 2026.
Nessuna nuova funzione del client XR e nessuna parità completa S1 dichiarata.

## Obiettivo e candidato

Completare 128 casi di `spline_point_update`, 14 rifiuti atomici e 540 stati raw.
Parte/Lamiera, tre metodi interpolanti e CV, aperta/chiusa, 2/5/64 input,
ruoli Start/Interior/End. I punti vengono risolti dalle raccolte native;
coordinate e ordinali non vengono usati come identità del prodotto.

DLL sperimentale 98 congelata:
`D70976D527A541CE0940B0DF13CD318C9775C9D55F548961758D1BFAEC1B2877`.
Il nuovo host è ricompilato dai sorgenti di sonda esistenti; non si ricompila
né si sostituisce il candidato. Nel checkout attuale l'helper di mutazione
ha lo stesso hash; la policy differisce per l'accessibilità di `ParseReference`
(`private` → `internal`, riuso della tensione 99). Questa matrice rimane
attribuita al candidato 98, non alla totalità della build backend più recente.

## Verifica

Il wrapper versionato `scripts/run-s1-spline-point98.ps1` avvia un Inventor
posseduto separato e usa il suo target esatto. Il processo ordinario 41096,
inizialmente senza documento attivo, resta escluso dalle mutazioni e dal Quit.
PID/ora di avvio, DLL caricata e identità dei documenti sono attestate dall'host.
Nessun HTTP, bootstrap, pairing o prova Quest viene avviato.

Preview/rollback e rifiuti richiedono stato nativo e revisione invariati.
Il commit deve conservare la curva e i punti nativi; la prima snapshot viene
registrata prima dell'oracle. Il confronto con una seconda spline nativa
costruita in transazione annullata usa `1e-5 mm`, dominio `1e-10`.
Gli scarti del punto risolto rispetto alla richiesta sono registrati per
prodotto e oracle: non si pretende che un fit point approssimato coincida
esattamente con l'input. Il README della sonda è allineato a questo criterio.

`scripts/verify-s1-spline-point98.ps1` ricontrolla offline matrice, hash,
stati di rollback, identità e ordine dei membri, geometria oracle, residui,
binding e contenuto della prima snapshot. Non esegue CAD e non verifica
proprietà non rappresentate o tutti i coefficienti NURBS.

## Esiti

Matrice nativa e controllo offline **PASS del sottoinsieme**. Evidenze nuove:
`artifacts/s1-verification/increment102/20261028-points-native/`.
Il precedente r2 conserva il fallimento dopo 98 casi e nessun rifiuto;
l'accesso negato non aveva una stack trace sufficiente ad attribuirne la causa.
Le nuove letture di avanzamento permettono la sostituzione del file pubblicato;
non viene ritentata alcuna operazione CAD e i vecchi esiti non vengono riscritti.

Il nuovo `native-r1` si arresta ancora con accesso negato dopo **61 casi PASS**,
prima dei rifiuti. Report e 244 sidecar conservati. Cleanup effettivo confermato:
zero documenti posseduti, PID QA 149372 assente, PID originale 41096 preservato,
manifest e DLL ripristinati. Il vecchio controllo del wrapper richiedeva anche
exit 0 del test per marcare il ripristino: `native-cleanup-audit.json` chiarisce
l'esito indipendente senza sovrascrivere il risultato originale del wrapper.

Per `native-r2` la sola sonda registra fase e nomi dei metodi in caso di errore,
senza argomenti, messaggi generici o percorsi. La pubblicazione atomica dei
metadati ha attesa massima di cinque secondi, con contatori osservabili;
nessuna operazione CAD viene ritentata. Il wrapper separa ora esito del test
e ripristino effettivo. Non si attribuisce ancora una causa precisa all'accesso
negato. La validazione diagnostica del verificatore su otto casi già completati
del vecchio r2 passa; i primi controlli hanno individuato e corretto conteggio
delle proprietà JObject e confronto Base64 sensibile alle maiuscole.
Questi otto controlli non sono un PASS della matrice nativa incompleta.

`native-r2`, run `6429b93dfa564ce98b3032c256048fe8`, completa **128 casi,
14 rifiuti e 540 stati raw** con exit 0. `offline-r1/verification.log`
conferma `VERIFIED_NATIVE_SPLINE_POINT98_SUBSET` su tutte le combinazioni,
prima snapshot, stati di rollback, identità, residui e confronto con l'oracle.
Il primo chiamante aveva trattato `LASTEXITCODE` non impostato di uno script
PowerShell puro come errore di processo: risultato JSON del verificatore
corretto, nota di orchestrazione conservata e prove negative avviate separatamente.

`negative-r1/results.json`: **cinque alterazioni rifiutate per il motivo atteso**:
geometria oracle, destinazione richiesta, prima snapshot, hash del sidecar e
caso mancante. Le copie negative contengono i quattro sidecar del primo caso;
il motivo specifico deve emergere prima di cercare file successivi volutamente
assenti. Hash degli originali invariati. Non sono nuove esecuzioni CAD.

L'host registra **5.283 attese di pubblicazione**, massimo **89 attese nominali
da 20 ms** nella stessa sostituzione di file: oltre il vecchio limite di 50.
Questo documenta contese sui metadati nel run riuscito e l'utilità del limite
maggiore; il processo esterno che trattiene i file non è stato identificato.
Non si attribuisce retroattivamente una stack trace ai vecchi errori.

Cleanup `native-r2/wrapper-result.json`: documenti posseduti chiusi senza
salvare, PID QA **120336** assente, PID originale **41096** preservato,
manifest ripristinato byte per byte e DLL ordinaria immutata. Il controllo MCP
successivo vede lo stesso processo originale, con un assieme aperto dall'utente.
Nessun comando della sonda ha operato sui suoi documenti.

Client e runner Unity conservano i sorgenti dell'[incremento 101](s1-spline-tension-xr101.md).
Questo incremento modifica soltanto tooling e documentazione delle prove native.
Regressioni complete in `regression/`: Core **1.393**, backend **1.926**,
Capture.Worker **16**, Unity EditMode **1.193 PASS**: **4.528** test,
zero falliti o saltati. Le otto verifiche diagnostiche e i cinque test negativi
del verificatore sono evidenze separate, non sommate a questa suite.

APK QA/ordinario ricompilati e ispezionati: **ARM64 IL2CPP**, 16 nomi runner
esatti nel QA e zero nell'ordinario; nessuna esecuzione hardware.

| APK | Byte | SHA256 |
|---|---:|---|
| QA | 95.770.547 | `AB9EF0A243D4C05BED1FAFAEC1B121AEBF38EE3692C33950E9E60785C7AD54E7` |
| Ordinario | 58.007.416 | `1B505C963602CE55B70B4B7E790C4383473651AF5D9B0D6F407D24997B4317E9` |

Font Satoshi generati archiviati in `generated-fonts/`, poi ripristinati
selettivamente; `git diff --check` passa. Manifest e DLL originali verificati
ancora nel controllo finale, con il processo 41096 e il suo assieme aperto.
Modifiche utente preesistenti conservate. Sorgenti, hash ed esiti nella consegna
locale `source-manifest.json` e `delivery.json`.

## Limiti

Punti di spline libere: vincoli, maniglie, dipendenze, aggiornamenti successivi,
persistenza/riapertura, editor XR dei punti e prova fisica restano aperti.
Un PASS di questa matrice non completa S1 né promuove automaticamente il tier.
