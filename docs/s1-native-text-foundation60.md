# S1 — Fondazione del testo nativo, incremento 60

14 ottobre 2026. Tier sperimentale; **18 gate S1 OPEN**. Le varianti
`text_create`, `text_update`, `text_delete` sono eseguibili nel batch
sperimentale, ma restano escluse da `SupportedVariants` finché rendering
dei glifi e editor XR non saranno integrati e verificati. Non costituiscono
la consegna dei due comandi Testo/Testo su geometria.

Il bridge crea TextBox fitted/rettangolari, sceglie uno stile già presente
nel documento durante la creazione, modifica contenuto/posizione e cancella
un oggetto selezionato. Controlla documento, schizzo, revisione, tipo,
sottoparte e proprietario nativo prima delle modifiche. Non ricrea il testo
per modificarlo: la chiave nativa rimane identica.

`SketchTextContent` accetta 1–64 segmenti strutturati, testo letterale entro
4.096 caratteri e nomi di parametri risolti nel documento proprietario prima
di mutare. Produce XML nativo con escaping, interruzioni Br e parametri
locali; non accetta XML libero, componenti esterni o proprietà arbitrarie.
Sedici test mirati verificano injection letterale, Unicode, nomi escapati,
controlli/surrogate invalidi, schema, budget aggregati e opzioni estranee.
Precisione dei parametri è attualmente zero: le altre opzioni restano da
implementare e non sono presentate come già disponibili.

## Verifica reale

`text60-native-r12.json`: **Parte e Lamiera PASS**, attraverso add-in,
host HTTPS/MCP e Core di produzione. Otto anteprime ripristinano esattamente
stato nativo/revisione; otto commit espliciti verificano risultato. Quattro
aggiornamenti successivi del parametro conservano identità, testo corrente
nello snapshot e geometria dei glifi. Quest’ultima viene confrontata con
una creazione API indipendente fatta soltanto dopo aver verificato l’oggetto
e lo snapshot di produzione: quattro confronti di vertici, facce e punti
centrati alla risoluzione di 0,00001 mm. Quattordici riferimenti/contenuti
errati vengono rifiutati con rollback e revisione invariati.

`ConfirmRendered` è simulato nel Core della sonda. Non prova rendering
Unity/Quest. La sonda finale non corregge il modello mediante Rebuild o
cambi del contesto di modifica esterni al percorso sotto test.
**1.631 test bridge PASS**; add-in experimental con zero warning/errori.
Sorgenti/DLL di produzione in `increment60/native-source-r12/`, eseguibili
e sorgenti effettivi della sonda in `increment60/inprocess-tested-r12/`.
Verificatore: `verify-text60-native.ps1`.

## Divergenza isolata e risolta

`Update2` dopo FormattedText non registrava correttamente i nuovi legami:
il tag formattato leggeva 22 mentre TextBox.Text e i glifi restavano a 10
(96 vertici/88 facce contro 110/100 della creazione diretta). I confronti
separano questo difetto da virgolette XML, proxy COM e contesto di modifica.
Una ricostruzione nativa completa prima degli aggiornamenti ripristina i
legami. Il bridge esegue quindi `PartDocument.Rebuild2(false)` **nella stessa
transazione posseduta**, dopo la modifica del contenuto. Il run finale
verifica entrambi gli aggiornamenti successivi, glifi corretti e Abort
esatto. Il run diagnostico 11, con rigenerazione esterna, rimane distinto
dal run finale 12. I tentativi precedenti e le DLL effettive sono conservati.

La rigenerazione completa può costare più di Update2 su modelli complessi:
benchmark, documenti con feature consumatrici e tutti i contesti di edit
restano da verificare. Nessun bypass della transazione o copia sostitutiva.

Registrazione temporanea verso directory nuova; manifest/DLL installati
originali ripristinati e processo dedicato finale 5036 terminato. Il processo
utente 91056 conserva la stessa ora d’avvio e i suoi documenti non vengono
letti dalle sonde. Nessuna APK creata o installata in questo incremento.

**NOT COVERED:** renderer/asset glifi del prodotto, editor/input XR completo,
testo su geometria, editing dello stile/allineamento/orientamento completo,
tutte le opzioni e default desktop, parametri di altri tipi/unità/precisioni,
consumatori/dipendenze complesse, storico, performance, Quest e prove fisiche.
