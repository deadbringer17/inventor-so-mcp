# S1 — Catalogo risorse nel client e primo runner

12 ottobre 2026. Incremento 38, successivo al [rapporto 37](s1-parallel-wave37.md).
Perimetro invariato: 77 comandi, Parte/Lamiera, Scheletro/3D e 18 gate aperti.

## F03 — Percorso condiviso nel prodotto

Il client Core legge catalogo paginato, descrittore e preview dal solo host
associato, con bearer e trasporto TLS già utilizzato dall'app. Non usa URL
restituiti dal server né la cache GLB. ID rigorosi, metadata, scadenza UTC,
dipendenze, tipi JSON e hash originale/PNG sono validati separatamente.
Il PNG viene controllato per framing, CRC, encoding e dimensioni prima del
decoder Unity; la verifica Core non sostituisce la decompressione del PNG.

L'editor condiviso Parte/Lamiera offre «Risorse sul server» nel gruppo
Inserisci. Consente di aggiornare/navigare l'elenco e mostrare la preview
su un pannello con proporzioni originali, senza creare bozze o oggetti CAD.
Il descrittore viene riletto prima del download. Annullamento, cambio
documento/revisione, chiusura e dispose revocano i callback; la scadenza
della risorsa chiude la preview. Texture e pannello vengono rilasciati.
Il pannello è dichiarato preview della risorsa; l'inserimento CAD rimane
disabilitato, come `cad_import_available=false` nel protocollo.

Limiti client espliciti per questa integrazione BMP: JSON 1 MiB, PNG 8 MiB,
pagina 32 risorse, profondità JSON 32, sorgente 32 MiB, immagine 4.194.304
pixel, richiesta 30 secondi e TTL residua massima 24 ore. Sono limiti
implementativi del client, non default Inventor o budget Quest misurati.
L'host deve ancora abilitare esplicitamente la policy di importazione e il
renderer dell'incremento 37; una route assente viene riportata come errore.

`TransportRequest.MaxResponseBytes` attiva un handler Unity che rifiuta
Content-Length troppo grande e chunk che superano il limite prima di
conservarli. Il buffer non cresce oltre il cap. Il cap riguarda payload e
capacità del backing array, non memoria del processo, buffer nativi o copia
finale. Le richieste esistenti mantengono il comportamento precedente.

Core: **990 PASS**, zero falliti/saltati, codice 0, **49 nuovi casi**.
Incluso client→host HTTP reale su loopback con upload di setup, elenco,
descrittore, preview/hash e scadenza; quel trasporto di test non prova TLS.
Unity: **1.043 PASS**, zero falliti/saltati, codice 0, **13 nuovi casi**:
budget/chunk, download HTTPS pinned reale, preview condivisa senza mutazioni,
callback tardivi e contratto del runner. Fallimenti iniziali conservati:
using mancante nel nuovo test UI, poi lookup incompleto degli assembly nel
test reflection. Corretti, non contati come PASS.

Il test host .NET aveva un riferimento sorgente mancante a FaceFeatureModel
usato da FakeAddIn: aggiunti lo stesso modello puro, UnitConvert e le
infrastrutture già incluse dai test Core. Build finale zero warning/errori;
il fallimento precedente resta nel log. Bridge invariato dal 37: 1.372 PASS
precedenti, senza una nuova esecuzione della suite bridge in questo incremento.

## C02 — Dipendenza Coincident dall'intera linea

La nuova sonda ricerca un punto coincidente con l'intera linea, diverso
dalla semplice identità di un endpoint condiviso. Quattro casi per contesto:
linea H/V e osservazione senza replay oppure replay esplicito verso il primo
pezzo dichiarato. H/V viene ripristinato in entrambi i bracci per isolare la
dipendenza Coincident. La quota trasversale della sorgente viene modificata
da 5 a 8 e 3 mm; il punto è endpoint di una linea vicina e ha quota
longitudinale di 6 mm. Si osserva anche un'espressione downstream collegata
al parametro modello originario della quota trasversale.

Parte/Lamiera: **otto osservazioni completate, 16 aggiornamenti, otto Abort
esatti**, zero divergenze rispetto ai controlli dichiarati. Nei quattro
controlli senza replay il punto non segue la sorgente in nessun aggiornamento;
nei quattro replay segue entrambi gli aggiornamenti, con membri nativi,
identità e appartenenza al pezzo controllate. Quote/parametri downstream
restano collegati; la baseline completa viene ripristinata.

Questa politica di replay è ricerca, non semantica attestata del comando
desktop Dividi. Altri vincoli/curve, profili consumati, proiezioni e mapping
di riferimenti del prodotto restano da implementare/verificare. Non viene
esposta una capability Taglia/Estendi/Dividi sulla base della sola sonda.

## H01 — Runner e fixture

Il nuovo runner QA S1 usa catalogo, tastierino, editor e backend reali.
Subset pianificato su Parte/Lamiera: apertura, linea numerica, preview con
renderer effettivo e rollback, annullamento, Applica con readback e revisione,
Concludi senza una nuova mutazione. Input catalogo/tastierino sintetico;
controller fisici, tracking e leggibilità non sono esercitati. L'esito del
runner è sempre PARTIAL e ogni lacuna viene dichiarata NOT COVERED.

Fixture dedicate con corpo base e schizzo S1_Create vuoto, manifest di
ownership per path e InternalName, guardie sulle identità e ripristino senza
salvare. Preparazione, lettura COM e ripristino su Inventor reale completati:
due schizzi realmente vuoti, ritorno a zero documenti. Questa prova del
lifecycle non esegue il runner né certifica il reader del gateway.

Il wrapper S1 prepara/ispeziona/ripristina le fixture e reinstalla/verifica
l'hash dell'APK ordinaria anche dopo errori. Le due APK sono compilate:
QA 54.702.278 byte, SHA256 `4B876BDF8934036B779599E7C74AC91065B49E50B804EE7B8434E4386F457E37`;
ordinaria 53.986.226 byte, SHA256 `A96DCF47D2D1C0AD08960BC78A0B5BB7D64D34B9199D629E92B7C743CCC12E53`.

Add-in e host aggiornati installati dal pacchetto `inventor-so-mcp-20261012-110645`.
DLL effettivamente caricata nel PID 52620: SHA256
`4C5E2C598D3BD50BBB0F5CC90B9B025FC8A5B97DDB52FE485EBA15D52C307107`.
Host HTTPS corrente PID 37644. Il Quest è Awake e il collegamento esistente funziona.

`device38/run1` si arresta prima del runner per fixture precedente ancora aperta.
`device38/run2`: **FAIL** dopo due PASS sintetici Parte (apertura e preview/annullamento).
Il commit produce la linea corretta (0,0)-(20,0) mm e due punti, ma il runner
presuppone erroneamente zero vincoli. La sonda `diagnose-line-constraints38.json`
riproduce nativamente AddByTwoPoints in una transazione poi annullata:
due Coincident non eliminabili, ognuno linea/punto. Non è un PASS del runner.
Ripristino fixture senza salvataggio e hash dell'APK ordinaria verificati entrambi.
Lamiera e casi successivi non esercitati. Il runner è stato corretto per richiedere
le due associazioni esatte linea/estremo/punto; nuova build e run ancora necessari.
Tutti i 18 gate S1 restano aperti.

## Esito finale del primo subset Quest

`device38/run3` fallisce sul controllo dei riferimenti strutturali: il reader
espone il punto sia come Entity sia come Point, quindi quattro associazioni
per Coincident. La prima correzione ne contava tre. Ripristino verificato.

`device38/run4`, APK QA corretta: **PARTIAL, exit 4**, otto sottocasi PASS
su Parte/Lamiera (apertura, preview/annullamento senza mutazione, commit linea,
Concludi senza nuova mutazione). Entrambi i readback COM finali contengono
una linea (0,0)-(20,0) mm, due punti, due Coincident strutturali non eliminabili,
zero quote. Riferimenti e revisione del gateway verificati nel runner.
Il wrapper chiude le fixture senza salvare e verifica zero documenti finali;
reinstalla l'APK ordinaria e ne verifica l'hash sul dispositivo.

QA finale: 54.704.686 byte, SHA256
`1C2F2F935BC28076BCC377A8E66A3F7ADE3333277B6039973062C4FCE0F43EC4`.
Ordinaria ripristinata: 53.986.226 byte, SHA256
`D8D6D341CCBDCF9BA06FF282908576CA3F5FCFCF5DFFAF6529AA68AC70B04F81`.
Build doppia corretta `apk-build38-r3.log` e QA finale `apk-build38-r4.log`
terminate con exit 0. `apk-build38-r2.log` conserva l'invocazione fallita
per namespace errato del metodo editor; non è una build riuscita.

Sei screenshot in-app dei passaggi e uno finale del compositor conservati.
Il visore appoggiato punta fuori dal foglio: la cattura commit esaminata non
mostra la linea, quindi non prova la sua visibilità sul display o leggibilità.
Le asserzioni automatiche controllano LineRenderer, vertici e piano realmente
associato alla preview; geometria confermata dal readback nativo. Nessun PASS
fisico. Riattivazione ADB sintetica usata per riprendere l'app dallo standby;
non sono state cambiate proprietà di tracking/prossimità/guardian.

Gli altri comandi e casi stale/offline, storico completo, prestazioni e fisico
restano NOT COVERED da questo run; tutti i 18 gate complessivi restano aperti.
