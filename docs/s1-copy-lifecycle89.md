# S1 — aggiornamento sorgenti e persistenza Copia89

Sonda PC dedicata, separata dal plugin e da host88. Usa il DLL p3 già
verificato nell'incremento88, senza modificarlo. Prima degli esperimenti
deve completare nello stesso processo la matrice88 Core/HTTPS: sei workflow,
otto confronti COM e dieci rifiuti con venti checkpoint esatti.

Il passaggio successivo arresta HTTPS e invalida il bearer. Due transazioni
native per documento cambiano estremi della linea, contenuto e origine del
testo letterale, WidthScale e proprietà Color del solo TextBox. Gli stili
condivisi non vengono modificati. Le copie e le altre entità dello schizzo,
le loro chiavi, proprietà, glifi, vincoli, dimensioni, stili e parametri utente
devono restare esatti. Sono modifiche native della fixture, non aggiornamenti
parametrici o chiamate Core dopo l'edit.

La fase di persistenza scrive SaveCopy in un nuovo percorso del run,
chiude l'originale senza salvarlo e apre la copia. Identità, percorso,
sottotipo e inventario vengono verificati prima dell'adozione. La prima
lettura semantica dopo Open non esegue Solve, Update, Rebuild o retry.
Originali e copie devono mantenere i rispettivi hash; i quattro IPT vengono
archiviati dal wrapper solo dopo esito completo.

## Stato delle prove

Il primo tentativo `increment89/native-r1` resta **FAILED**. La matrice88
iniziale passa, ma il primo edit Parte si ferma sul confronto WidthScale
con il valore richiesto1.35 e tolleranza1e-12. Abort ripristina l'intero stato
nativo esattamente; HTTP viene arrestato, Inventor dedicato termina e
manifest/DLL ordinario sono ripristinati. Nessuna persistenza viene dedotta.

La sonda successiva usa fattori1.5 e0.75, rappresentabili esattamente, e
registra il readback prima delle asserzioni. Mantiene la tolleranza stretta.
Confronta inoltre la proprietà Color del TextBox con il valore richiesto:
`GetColor`, usato per il colore effettivo dei glifi, è una lettura distinta.
Sono stati aggiunti il controllo della transazione prima di End/Abort,
la validazione prima dell'adozione e l'hash verificato anche dell'originale
archiviato. `native-r2` è la prova della sonda aggiornata.

R2 resta **FAILED**: nella Parte passano entrambi gli edit reali con copie e
stili invariati, ma il confronto esatto della prima lettura dopo riapertura
fallisce. L'esito non viene convertito in PASS: cleanup normale, nessun
processo Inventor dedicato residuo e installazione ripristinata. La versione
successiva conserva nel report anche la lettura riaperta fallita e il primo
campo diverso, prima dell'asserzione; r3 raccoglie questa diagnostica con lo
stesso DLL p3 e senza cambiare la tolleranza.

R3 si ferma prima del lifecycle: la lettura di un ack del checkpoint incontra
una sharing violation Windows. Worker e Inventor terminano normalmente,
installazione ripristinata. La pubblicazione dei metadati pubblici passa ora
da scrittura diretta a file temporaneo chiuso e rename atomico; le sole
sharing violation di I/O vengono attese in modo limitato. Nessuna operazione
CAD o prima snapshot viene ripetuta. R4 usa questa correzione della sonda.
L'originale e il SaveCopy Parte di r2 sono conservati con hash verificati in
`native-r2/diagnostic-fixtures-r1`, come materiale diagnostico del run FAILED.

R4 completa la diagnostica: la sola differenza dopo riapertura Parte è il
`ComponentIdentifier` dell'unico campo parametro NON copiato, che Inventor
trasferisce dal percorso assoluto originale al SaveCopy. Tutti gli altri
dati nativi sono esatti. R4 resta FAILED con il confronto precedente.

Il confronto di persistenza successivo accetta soltanto questa relocation
esplicita della fixture: markup e attributi chiusi, Resolved=True, parametro
S1Copy88Length con stessa chiave/valore/unità, e ComponentIdentifier uguale
al percorso assoluto del documento nativo posseduto in ciascuna fase.
L'identità semantica usa documento e chiave parametro, non DisplayName.
Nessuna coordinata, proprietà, glifo o chiave viene ignorata o tollerata
diversamente. Copiare testi parametro rimane rifiutato dal plugin.

Diciotto controlli puri della policy passano, inclusi owner esterno,
parametro/documento diverso, modifiche ai glifi/stili e il readback reale r4.
Il verificatore PowerShell indipendente conferma separatamente il caso
reale e rifiuta un percorso estraneo. Sono verifiche di metadati, non un
PASS nativo di persistenza. R5 esegue la matrice completa con tale confronto.

R5 verifica entrambi gli edit e la riapertura nella Parte; si ferma poi su
access denied durante la sostituzione di un file di avanzamento letto dal
wrapper. Resta FAILED, con cleanup normale. Il lettore del wrapper consente
ora FileShare.Delete; la pubblicazione dei metadati ha attesa limitata per
sharing/access denied sul solo file regolare posseduto. Non ripete CAD.

**R6 PASS_NATIVE_LIFECYCLE_SUBSET** sul DLL p3:

- Matrice88 iniziale completa nello stesso processo:6workflow,8coppie COM,
  10rifiuti e20checkpoint esatti.
- Quattro modifiche reali delle sorgenti, due per Parte e Lamiera: copie,
  glifi, proprietà, chiavi, stili e parametri invariati.
- Due SaveCopy/chiusure/riaperture, prime letture native esatte dopo la sola
  relocation verificata del campo parametro non copiato.
- Quattro IPT archiviati con hash confrontati alle baseline native.
- HTTP fermato, tutti i documenti temporanei chiusi, processo dedicato
  terminato, processi preesistenti preservati, manifest e DLL ripristinati.

`native-r6/verification-r1.json` e
`native-r6/verification-lifecycle-r1.json` confermano separatamente matrice88
e confronto indipendente dei dati lifecycle, non soltanto i flag della sonda.
I tentativi r1–5 restano conservati con i propri esiti.

Build host corrente senza errori; rimane l'avviso NU1903 preesistente della
dipendenza SQLite del server. I sorgenti e binari eseguiti vengono conservati
in ogni directory di run. Una bozza isolata precedente rimane archiviata in
`increment89/copy-lifecycle-draft`; non è evidenza di esecuzione.

## Limiti

Un eventuale PASS89 prova solo indipendenza e persistenza native di linee
libere e testo letterale nel profilo88. Core/HTTPS dopo gli aggiornamenti e
dopo riapertura richiedono verifiche separate. Restano testo parametrico,
altre geometrie/opzioni, confronto desktop, UI, Quest e input fisico.
Tutti i18 gate complessivi S1 restano aperti.
