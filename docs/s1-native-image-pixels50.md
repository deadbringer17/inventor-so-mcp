# S1 — Lettura bitmap dentro Inventor, incremento 50

14 ottobre 2026. Ricerca/implementazione sperimentale, **18 gate aperti**.

`get_sketch_image_payload` ammette `source_mode=native_bitmap`, mantenendo
`managed_original` come default compatibile. L'estrazione verifica il PID
proprietario della finestra prima di accedere a IPictureDisp/HBITMAP, limita
il risultato a 32 MiB e copia con GetDIBits dentro Inventor. Produce BMP32
top-down BGRX, con X azzerato, senza inferire alpha. Libera soltanto il DC
creato dalla richiesta e azzera i buffer; non libera l'handle di Inventor.
Trasparenza e linked restano rifiutati. Source mode fa parte del binding.

L'adattatore server aggiunge DownloadNativeAsync e SourceKind, verificando
il modo confermato e mantenendolo su tutti i chunk e sul discard.
**36 test dedicati PASS**, **1.556 bridge PASS**, zero FAIL e un SKIP.

La prova usa il vero add-in caricato in una distinta istanza Inventor e il
pipe autenticato. Il manifest viene temporaneamente indirizzato a una copia
immutabile locale dell'assembly; manifest e DLL originali sono ripristinati
e verificati con hash. I processi dedicati sono poi confermati terminati.
Il conteggio immediato dopo Quit può ancora mostrarli durante la chiusura;
la conferma successiva dei PID è conservata separatamente.

La prima run fallisce sull'accesso dinamico a Type. La versione tipizzata
IPictureDisp supera **40 osservazioni**, 20 Parte e 20 Lamiera: BMP24/32,
top-down/bottom-up e cinque operazioni. Otto casi raw hanno colori esatti;
tutte le letture preservano lo stato CAD e tutte le fixture sono ripristinate
vuote dopo Abort e riparazione OLE dei soli descrittori catturati.

Risultato decisivo: Image restituisce i pixel non trasformati anche dopo
MirrorHorizontal, MirrorVertical e RotateLeft. Il riordino dei bordi nativi
cambia invece insieme all'operazione. Il solo bitmap non prova quindi il
rendering corretto: servono anche i ruoli dei bordi, verificati nella tranche
successiva. Non dichiarati alpha, equivalenza visiva, texture XR o Quest.

Gli assembly e i sorgenti nativi effettivi sono conservati per entrambe le
run. Per l'adattatore server sono conservati gli assembly/test effettivi;
il suo snapshot sorgente specifico 50 non è stato copiato prima delle
successive modifiche 51. Nessun eseguibile installato permanentemente,
nuova APK, test Core/Unity o prova fisica in questa tranche.
