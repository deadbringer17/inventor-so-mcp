# S1 — Bitmap e orientamento nativo nel client, incremento 51

14 ottobre 2026. Implementazione sperimentale F03; **18 gate aperti**.

La query distingue il bitmap nativo dall'originale gestito. I quattro angoli
UV derivano dai ruoli dei bordi di `BoundaryGeometry`: superiore/inferiore
e sinistro/destro. Non vengono ordinati per coordinate e non si presume che
Width/Height descrivano il rettangolo corrente dopo RotateLeft.

Il vero add-in, caricato in un processo Inventor dedicato, supera **64 casi**,
32 Parte e 32 Lamiera: BMP24/32, top-down/bottom-up e otto trasformazioni,
comprese specchiature combinate e rotazione geometrica di 37 gradi. Ogni
caso confronta i sei pixel generati e gli angoli con una trasformazione
calcolata indipendentemente dal rettangolo iniziale. Tutte le letture
preservano lo stato CAD; tutte le fixture vengono ripristinate vuote.
Manifest originale ripristinato byte per byte e DLL installata invariata.
SaveCopy/riapertura non è coperto da questa matrice.

`inventor_get_sketch_image_xr` scarica il BMP con target fissato, lo converte
in PNG RGBA opaco entro budget espliciti, ricontrolla la revisione e pubblica
un asset accessibile al solo proprietario autenticato. La metadata condivisa
per hash non contiene identità o geometria del documento. Trasparenza e
immagini linked rimangono rifiutate.

Il client verifica riferimenti, rettangolo, URL HTTPS del server abbinato,
dimensioni, tipo PNG, CRC e hash prima di consegnare il buffer al renderer.
L'URL dell'asset è relativo, per usare l'endpoint abbinato anche quando
PublicBaseUrl contiene un alias o un prefisso differente.
I trasporti disabilitano i redirect automatici. Il layer Unity crea quad
senza collider, usa il piano CAD corrente, condivide texture identiche per
revisione e annulla le risposte obsolete. Possiede e rilascia mesh, materiali
e texture; limita immagini e pixel decodificati. Gli editor Parte e Lamiera
usano lo stesso layer. Lo shader URP è incluso tramite Resources.

Verifiche completate: **179 test server mirati**, **1.614 bridge PASS**
(un SKIP per host di pairing già attivo), **1.078 Core PASS**. Unity supera
**1.050 test generali** e **sette test immagini separati**, compreso il
controllo dei colori copiati dalla texture GPU. Il primo run generale
escludeva i sette test per un GUID errato; il primo run mirato conserva
due fallimenti diagnostici: readback in un formato non supportato e una
aspettativa di OnDisable durante EditMode. La verifica finale usa una copia
GPU in RenderTexture e verifica Clear esplicito. Il callback OnDisable non
è quindi certificato da questo test EditMode. I sorgenti specifici dei due
run diagnostici Unity non sono stati conservati prima delle correzioni;
log e risultati restano disponibili. Nessuna APK installata.

Restano la prova integrata HTTP con Inventor reale, SaveCopy/riapertura del
bitmap trasformato, runner Quest e prova fisica. I test del client non
certificano equivalenza visiva desktop, alpha, immagini linked, selezione
dei bordi delle immagini o cronologia OLE. I gate complessivi restano aperti.
