# S1 — Arco centro/punto, verifica integrata82

15 ottobre2026. La sonda PC reale `increment82/native-r2` termina
**PASS_INTEGRATED_SUBSET**, exit0:16 workflow e14 rifiuti. Nessuna nuova
capacità viene promossa e tutti i18 gate S1 restano aperti.

Il progetto net8 usa bootstrap73 monouso, HTTPS pinned, Core e add-in reali,
senza acquisire COM. La conferma rendering della `DesignSession` è
**sintetica PC**: non prova preview Unity, Quest o controller. L'host73
possiede una nuova istanza e due fixture dedicate, con entrambi gli
`S1_Create` inizialmente vuoti.

## Matrice e verifiche

Parte/Lamiera × angolo iniziale0/170° × sweep+90/-90/+270/-270°:16 creazioni
di raggio10mm, con posizioni distinte. Ogni workflow richiede preview
senza consenso implicito, rollback esatto dello schizzo rappresentato e
della revisione eventi, consenso sintetico esplicito e commit confermato.

Il primo request del client dopo Apply confermato legge lo snapshot nativo
con LastCommit. Non interpone COM, oracolo, ricostruzione o lettura contesto.
Verifica una nuova chiave arco, conservazione delle entità precedenti,
centro/raggio/insieme degli estremi e locus dei punti, tolleranza1e-5mm.
Il confronto distingue arco maggiore/minore e segno senza inventare una
parità dell'ordine degli estremi negativi. Seconda lettura identica e
revisione invariata dalle letture.

I14 rifiuti comprendono due stale, dieci geometrie invalide (raggio0/-1,
sweep0/+360/-360) e due batch con creazione valida seguita da geometria
invalida. Questi ultimi devono restituire ROLLED_BACK con step_code
INVALID_ARGUMENT; un errore API generico non viene accettato come PASS.
Schizzo rappresentato e revisione restano esatti dopo ogni rifiuto.

Il limite HTTP ordinario240/minuto è mantenuto con una pausa65s fra i
contesti, prima della nuova baseline Lamiera. Nessun retry post-Apply o
commit incerto. Il report conserva before/after e codici strutturati.

## Producer e ripristino

- [Report](../artifacts/s1-verification/increment82/native-r2/client-report.json)
  e [verbale wrapper](../artifacts/s1-verification/increment82/native-r2/wrapper-result.json).
- Sorgenti, trasporto e assembly effettivi in `native-r2/client-sources`,
  `client-bin`, `sources` e `bin`; wrapper eseguito archiviato nel tentativo.
- DLL add-in esatta80:
  `711D8451C6740F5290A67F48E2F07DD0319534CB226CD4EB83FBD157A39F16C4`.
- Native-r1 rimane FAIL con zero casi: la bozza costruiva points_mm come
  array piatto tramite il costruttore di copia JArray. La correzione usa
  un array contenente l'array punto; r1 non viene riscritto come PASS.
- Build-r1 conserva due errori della diagnostica e due warning tuple;
  build-r4 corretto termina con0 errori/0 warning. La prima build e i
  precedenti sorgenti eseguiti rimangono archiviati.

L'ispezione finale è readonly; il processo owned101384 termina normalmente,
exit0. Manifest e DLL installati sono verificati e ripristinati; istanza
utente91056 con identico start-time preservata senza acquisizione COM.
Nessun accesso ai documenti personali e nessuna installazione sul Quest.

## Lavoro ancora richiesto

L'oracolo è analitico applicato ai dati del reader di produzione, non un
confronto nativo/desktop indipendente. Mancano ordine/default/inferenze
desktop completi, rollback di tutto lo stato nativo, due editing parametrici
del raggio, SaveCopy/reopen e runner Quest. Le precedenti prove31 restano
distinte. Arco tangente, cerchio tangente, passaggio linea/arco e3D non
sono coperti. [Ricerca83](s1-tangent-arc-api83-notes.md) prepara la capacità
tangente mancante senza dichiararla implementata.
