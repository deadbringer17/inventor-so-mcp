# S1 — Bootstrap QA e host isolato73

Il 15 ottobre 2026 sono stati verificati 1.247 test Core e una prova nativa
completa del ciclo dell'host dedicato. Non è una prova Quest e non chiude
nessuno dei 18 gate complessivi.

Il bootstrap QA ha schema chiuso di undici campi, dimensione massima16KiB,
scadenza entro30minuti, indirizzo privato o loopback, target esatto e due
identità documento distinte. Gli errori non riportano contenuto o token.
L'app QA legge un file dal proprio persistentDataPath, lo elimina prima
della sessione, azzera il buffer e usa credenziali solo in RAM. Non ripiega
sul pairing ordinario; scadenza o richiesta non valida arrestano la sessione.
Il consumo Android del file deve ancora essere provato sul dispositivo.

`S1QuestOwnedHost73` crea un nuovo processo Inventor, verifica PID e ora di
avvio prima di accedere ai documenti, verifica il percorso DLL caricato e
SHA su disco, prepara due fixture con corpo, schizzo vuoto e due testi
nativi, e avvia HTTPS con target esatto e token dedicato. La DLL provata è
quella67, SHA256 `29ACD9BCC640847650D4E361737B67C8F19882E814C76FB4F23855CB859A8A50`.
La verifica DLL non è un hash dei byte in memoria.

La prova `host-smoke-r5` termina con exit0: bootstrap491byte con ACL privata,
ispezione readonly senza modifica di revisione/Dirty, chiusura SkipSave di
entrambe le fixture, zero documenti, Quit e uscita del PID dedicato25928.
Manifest originale e DLL installata sono invariati, come PID91056 e ora di
avvio del processo personale. Nessun suo documento è stato letto via COM.
La prova non valida ancora trasporto USB, UI, controller, WiFi o performance.

Gli insuccessi sono conservati: build iniziale con `Environment` ambiguo;
wrapper r1/r2 prima dell'avvio; r3 senza attestazione completa; r4 con errore
ACL e rifiuto di cleanup. L'ACL viene ora assegnata atomicamente alla
creazione del file con handle FullControl. Il cleanup distingue il sentinel
idle di Inventor dalla transazione identificata tramite il controllo
readonly già usato dal prodotto. Non esegue Abort/End su transazioni ignote.
Il processo disposable85448 rimasto dalla r4 è stato terminato con recupero
esplicito, dopo verifica esatta di PID/avvio, HTTP arrestato, fixture su disco
invariate e processo personale preservato; questa r4 resta FAIL.

La build dell'host passa senza errori; resta il warning NuGet NU1903 della
dipendenza SQLite già presente. Sorgenti, binari effettivamente eseguiti,
log e risultati sono in `artifacts/s1-verification/increment73`. Token,
bootstrap e certificato privato non fanno parte dell'archivio pubblico.
