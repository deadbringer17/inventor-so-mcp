# S1 — Raccordo: raggio parametrico e perdita delle quote sorgenti

12 ottobre 2026, incremento 40, B04/S1-06. Perimetro invariato: 77 comandi,
Parte/Lamiera e estensioni contestuali; tutti i 18 gate restano aperti.

La sonda `arc-ui-fillet-radius` confronta `AddByFillet` con espressione
nominata e lo stesso risultato seguito da una `RadiusDim` driving esplicita,
collegata al parametro del raggio. Tre coppie (linea-linea, linea-arco,
arco-arco), sorgenti libere oppure quotate, stessa soluzione dichiarata ++.
Build riuscita, zero warning/errori.

Parte e Lamiera producono lo stesso esito: **24 casi, 72 aggiornamenti,
16 casi completati e 8 divergenti** rispetto alla politica dichiarata.
40 aggiornamenti completano tutti i controlli, 32 falliscono. Tutti i
24 Abort ripristinano esattamente geometria, vincoli, quote e parametri;
cleanup finale verificato con zero documenti aperti prima della successiva
riapertura della fixture richiesta dall'utente.

La quota del raccordo esplicita segue il parametro in **36 aggiornamenti
su 36**, con identità, membro nativo, espressione, valore e `DrivenBy`
verificati. Questa evidenza isolata non rende parametricamente corretta
l'intera operazione: i casi con sorgenti lineari quotate divergono.

- Linea-linea: le quote driving restano associate agli estremi originali,
  mentre le linee accorciate hanno nuovi estremi. Al primo aggiornamento
  il valore delle quote è 40 mm e la lunghezza delle linee è circa 17 mm.
  L'identità della quota sopravvive; il controllo della linea corrente no.
- Linea-arco: `AddByFillet` elimina la quota della linea. Leggere l'oggetto
  COM precedente produce `E_FAIL`; le fotografie native prima/dopo
  confermano che la quota non appartiene più alla collezione.
- Arco-arco: entrambe le quote raggio delle sorgenti conservano membri e
  dipendenze durante gli aggiornamenti, nella soluzione provata.

Gli errori di `CanBindKeyToObject` sono conservati come diagnostica, senza
spacciarli per risoluzione dei riferimenti. Le verifiche di identità e membri
si basano anche sul readback diretto e sulle collezioni native.

Il campo esterno `outcome: PASS` dei JSON significa che la sonda ha terminato
e ripristinato la fixture: **non significa che tutti i casi siano passati**.
I contatori delle divergenze e i singoli controlli sono l'esito della matrice.
Non sono state attenuate le aspettative per ottenere un risultato verde.

Non sono verificati equivalenza/default desktop, conservazione/rimozione
dei vincoli nelle altre soluzioni, invalidi, geometrie proiettate, dipendenze
downstream, Smusso, preview/commit prodotto o Quest. Nessuna capability è
abilitata da questa ricerca. Occorre una strategia esplicita per le quote
sorgenti, confrontata con l'oracolo desktop, prima dell'integrazione.

Evidenze: `radius40-{part,sheet}.json`, prepare/cleanup e before/after,
`native-build40.log` in `artifacts/s1-verification`; sorgenti e DLL testate
in `increment40/native-tested`, manifest con hash dell'intero incremento.
