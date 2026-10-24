# S1 — Chiusura delle spline native, incremento 96

23 ottobre 2026. Incremento del plugin sperimentale PC; nessuna promozione di
capacità e nessuna chiusura dei 18 gate complessivi S1.

## Comportamento implementato

`edit_sketch/spline_fit` e `spline_control` accettano l'opzione booleana
`closed`. Per una spline interpolata viene impostata la proprietà nativa
`Closed`; per una spline di controllo viene ripetuto il primo punto, secondo
il comportamento nativo di `IsClosed`. L'omissione conserva il comportamento
del creatore precedente. Una spline di controllo esplicitamente aperta con
primo e ultimo punto uguali viene rifiutata prima della creazione.

Il preflight congela da 2 a 64 punti di input in millimetri, richiede coppie
numeriche finite e rifiuta opzioni sconosciute. Le spline interpolate richiedono
un `fit_method` intero appartenente ai tre valori nativi 26369–26371. Il reader
restituisce chiusura, metodo e numero dei punti interpolati, oppure chiusura e
numero dei punti di controllo. La conversione dell'API rimane in centimetri.

## Verifiche disponibili

- 26 test della policy passati; suite bridge completa: 1.802 test passati.
- Build add-in 2027 sperimentale e ordinaria senza errori o warning.
- Build di compatibilità 2024 passata, con il warning nullable preesistente
  in `SendCodeHandler.cs`.
- Catalogo MCP aggiornato con opzioni spline, enum e limiti effettivi;
  113 test mirati di policy, batch e sorgenti sperimentali passati dopo
  l'aggiornamento della descrizione. Questa modifica descrittiva è successiva
  al congelamento del candidato nativo r1.
- Candidato nativo congelato in `increment96/producer-r1`, SHA256
  `2AF7E77A8A34EA82052AD84445398CDFA6858A2E9BC2AF13A6B577B6A13D15E0`.

La prova nativa r1, run `2dc65016847045d697a448a458941ee1`, è terminata con
`PASS_PLUGIN_NATIVE_SPLINE_CLOSURE_SUBSET`: 64 casi Parte/Lamiera,
interpolata/controllo, 2/3/5/64 punti, aperta/chiusa e tutti i tre metodi
interpolati, più 14 rifiuti e 284 stati raw. Wrapper e worker hanno exit code
zero. Il PID posseduto 121328 è assente dopo l'uscita; manifest e DLL ordinari
sono ripristinati e invariati. Il controllo offline indipendente
`verification-spline-closure96-r8.json` è passato: 64 casi, 14 rifiuti,
284 sidecar e otto alterazioni semantiche rifiutate, incluso il flag
costruzione della prima snapshot. Non sostituisce l'oracle nativo e non
estende il perimetro delle prove.

Le prime esecuzioni del verificatore hanno rilevato assunzioni errate nel
verificatore stesso, conservate nei log: aritmetica PowerShell, punto di
chiusura aggiunto anche alle spline interpolate, chiavi da confrontare entro
lo stesso documento, approssimazione dei fit point e poligono di controllo CV.
Le spline interpolate a 64 punti dei metodi 26369/26370 mostrano scarti dagli
input fino a circa 0,000808 mm, uguali al creatore nativo indipendente.
Il confronto prodotto/oracle conserva 0,00001 mm di tolleranza; non si
attribuisce interpolazione esatta dell'input né ownership dei segmenti
ausiliari sulla base delle sole coordinate.

La sonda usa il vero plugin tramite Named Pipe autenticata, preview atomica,
commit e una prima lettura prodotto prima dell'oracle geometrico. Il confronto
nativo indipendente crea una curva direttamente con l'API in una transazione
poi annullata. Stato completo prima/dopo preview e rifiuti, cleanup delle sole
fixture possedute e ripristino dell'installazione sono verifiche distinte.
La revisione invariata della preview viene asserita durante l'esecuzione, ma
i token intermedi non sono archiviati nel report r1: questa asserzione non è
ricalcolabile indipendentemente dai soli file salvati.

## Perimetro ancora aperto

Questo incremento non implementa editing dei punti, maniglie o tensione,
inferenze e opzioni complete del comando desktop, associazioni miste,
aggiornamenti delle dipendenze e persistenza delle spline. Non prova Core via
HTTPS, UI, Quest, input fisico o prestazioni. Le righe spline dell'audit dei
77 comandi restano parziali; il completamento PC e S1 rimane aperto.
