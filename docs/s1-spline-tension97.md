# S1 — Tensione spline: osservazioni native, incremento 97

24 ottobre 2026. Ricerca PC sull'API Inventor 2027; nessuna equivalenza
desktop o capacità di prodotto certificata da questa sonda.

Il run `14d53e8051ff4a05b64a473918d337c4` ha completato 60 casi:
Parte/Lamiera, tre metodi interpolanti 26369–26371, aperta/chiusa e tensione
0/25/50/75/100. La sonda crea la curva nativa, imposta la chiusura e poi
la tensione, esegue Solve/Update una volta e annulla la propria transazione.
Non scrive le opzioni globali dello schizzo.

Tutti i 60 setter sono ritornati e i 60 valori dopo Update coincidono con
la richiesta. Nessun errore API, Update falso o cambio del metodo di
interpolazione osservato. Queste osservazioni riguardano le fixture e i cinque
valori esercitati: non dimostrano tutti gli interi, geometrie vincolate,
comportamento dei controlli desktop o un valore predefinito globale.

I 60 annullamenti conservano esattamente gli stati catturati, compresi
Dirty, DatabaseRevisionId e definizioni spline. Il worker è uscito normalmente;
manifest e DLL ordinari sono ripristinati. L'assenza del PID posseduto 128092
è stata verificata separatamente dopo l'uscita.

Il verificatore offline `verification-spline-tension97-r2.json` ricontrolla
matrice, identità entro il documento, sidecar e hash, stadi API, risultati
effettivi e annullamenti. Sette alterazioni semantiche sono rifiutate, incluso
un parametro modificato dopo Abort con hash ricalcolati. Il primo tentativo
del verificatore falliva nella costruzione dei dati alterati per la conversione
PowerShell/Json.NET; log conservato, corretto con JValue espliciti.

Evidenze: `artifacts/s1-verification/increment97/native-r1`.
Candidato 96 congelato, SHA256
`2AF7E77A8A34EA82052AD84445398CDFA6858A2E9BC2AF13A6B577B6A13D15E0`.
Il successivo incremento 99 implementa il percorso plugin, ancora da provare
attraverso preview/commit e prima snapshot. Tutti i 18 gate S1 restano aperti.
