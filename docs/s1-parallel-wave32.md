# S1 — Prima ondata parallela, 11 ottobre 2026

Incremento dei pacchetti C02, D04 e F03 del piano parallelo. Il perimetro S1
resta integrale: questi moduli Core sono dipendenze dei comandi finali, non
chiudono Taglia/Estendi/Dividi, visualizzazione vincoli o importazioni.

## Moduli e verifica

| Sottopunto | Risultato | Casi Core |
|---|---|---:|
| C02-A | Intersezioni analitiche di segmenti, cerchi e archi finiti firmati; ordine parametrico, contatti, sovrapposizioni/coincidenze ambigue | 39 |
| D04-A | Layout deterministico delle annotazioni dei vincoli; ancore native, associazioni validate, selezione, filtri, budget e omissioni | 19 |
| F03-A | Risorse importabili immutabili e validatore di metadata, policy esplicita, unità, byte dichiarati e grafo delle dipendenze | 44 |

Suite Core finale: **906 PASS, zero fallimenti/skipped**, rapporto
`artifacts/s1-verification/core-parallel32-final.trx`. Sono 102 casi nuovi.
Il primo run (`core-parallel32.trx`) conserva quattro fallimenti diagnostici:
tre literal InlineData int invece di nullable long e una cancellazione
numerica nell'intersezione di segmenti quasi paralleli lunghi 2e15 mm.
Il calcolo finale usa coordinate centrate; il caso e la sua traslazione
restano verificati senza aumentare la tolleranza. Nessun warning C# nel log
Core finale.

Unity EditMode: **971 PASS, zero fallimenti/skipped**, rapporto
`artifacts/s1-verification/unity-parallel32.xml`, processo terminato con codice
zero. I quattro assembly Unity verificati e i due assembly Core/test .NET
sono conservati in `parallel32-clearance/`. Nessun nuovo test Unity è stato
aggiunto: questo run verifica compilazione/compatibilità e regressione dei
percorsi già coperti, non l'integrazione UI dei tre moduli.
Nessun nuovo APK è stato generato o installato. Nessuna mutazione CAD viene
aggiunta dai tre moduli. La fixture desktop spline rimasta dall'esplorazione
precedente è stata chiusa senza salvare; `parallel32-final-state.json`
conferma zero documenti aperti. L'esplorazione incompleta della chiusura spline
non costituisce evidenza di equivalenza desktop.

## Integrazione ancora necessaria

C02 deve includere ellissi/spline e collegarsi a selezione del tratto,
ricostruzione nativa, trasferimento relazioni e invalidazione dei riferimenti.
Il motore di intersezioni non modifica entità CAD e non espone nuovi comandi.

D04 deve collegarsi al renderer e al percorso Ray condiviso Parte/Lamiera.
`SketchDimensionLabel` attuale usa un canvas 320 × 44 con dimensione fisica
costante e orientamento verso la testa: il footprint Core predefinito 8 × 4
mm CAD non rappresenta quel rettangolo. Serve una view misurabile, un layer
con hit-test sul rettangolo visualizzato e footprint coerente con scala/posa.
L'highlight deve usare le associazioni validate dal layout, non espandere
direttamente riferimenti contraddittori del payload. Renderer, filtri UI,
diagnostica omissioni, glyph desktop, pooling/cache misurati e prove Quest
restano da realizzare. Il budget del modello è una policy di vista,
non un limite Inventor o una prestazione verificata sul dispositivo.

F03 deve collegarsi a selettore Quest, trasferimento/registro host, byte reali,
autorizzazione, decodifica, opzioni native e preview/commit/rollback.
Il contratto attuale delle immagini descrive raster; le risorse OLE richiedono
un'estensione esplicita se ammesse dall'oracolo. La policy non pubblica
capability CAD. Un collegamento a immagine non può affidarsi senza verifica
al TTL del registro esistente degli asset di output: la retention deve
rispettare la durata del collegamento documentale. Le immagini sono ancora
unsupported nel reader nativo, altro prerequisito della preview completa.

Tutti i gate S1-01…18 restano aperti. Unitario, Inventor reale, Quest sintetico
e prova fisica restano livelli di evidenza distinti.
