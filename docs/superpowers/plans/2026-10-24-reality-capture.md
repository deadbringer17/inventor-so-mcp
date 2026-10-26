# Reality Capture — piano di implementazione

Riferimenti: `Inventor XR SO/REALITY_CAPTURE_SPEC.md` e [design della sezione indipendente](../specs/2026-10-24-reality-capture-independent-section-design.md).

## RC0 — contenitore autonomo

1. Aggiungere l'ingresso nella Home MR/VR e nel percorso senza PC.
2. Comporre `RealityCaptureWorkspace` con UI separata e passthrough.
3. Sospendere strumenti, input, selezione e voce CAD; rendere persistente la soppressione delle radici UI durante i refresh.
4. Gestire menu, ritorno alla Home, cambi di connessione e nuovo ingresso CAD.
5. Verificare i gate RC0-01…06 in EditMode e portare i sottocasi deterministici nel runner Quest su fixture dedicata.
6. Registrare separatamente compilazione/test, run sintetico Quest e prova fisica.

## RC1 — acquisizione reale (MVP 1 della specifica)

Prima del codice: verificare le API camera effettivamente disponibili nel Meta SDK installato, permessi, intrinseci, estrinseci, timestamp e sincronizzazione con la posa. Il percorso QR esistente non fornisce da solo il contratto metrico necessario.

Implementare contratti `CaptureFrame`/`ScanSession` nel core, sorgente camera Quest, selezione frame, salvataggio locale immutabile, limiti di spazio e stop su perdita tracking/sospensione. UI Oggetto/Area, delimitazione, avvio/pausa/stop, feedback di qualità osservabile. La copertura deve derivare da dati reali; in assenza di ricostruzione provvisoria non dichiarare copertura geometrica completa.

Verifica: unità e trasformazioni, frame/posa sincronizzati, permessi negati, tracking perso, stop/ripresa, spazio esaurito, riapertura sessione. Runner sintetico per i sottocasi riproducibili e prova reale delle camere sul Quest. Nessun errore dimensionale promesso in questa fase.

## RC2 — trasferimento locale e ricostruzione metrica (MVP 2)

Leggere `bridge/CLAUDE.md` prima di modificare il backend. Definire endpoint `/capture/v1`, ownership per token, chunk binari con hash, quote/retention e ripresa upload. Contratti di controllo tramite MCP, elaborazione pesante in worker locale isolato.

Selezionare e provare la pipeline multi-view su un dataset fisico controllato prima dell'integrazione nel client. Aggiungere artefatti misurati e marker, barre metriche, distanza nota e diametro, solver con pesi/incertezze, residui e gestione conflitti. Produrre point cloud, mesh e rapporto qualità; conservare raw e revisioni del solve.

Verifica: ricostruzione con una camera RGB e senza depth, scala, vincoli incompatibili, errore del worker, isolamento token, hash errato, cancellazione. Riservare quote indipendenti alla validazione.

## RC3 — revisione e geometria semantica (MVP 3)

Viewer della scansione separato dal CAD; punti/piani/cilindri/cerchi/assi, misure e incertezza dichiarata, riferimenti dei vincoli riconoscibili, GLB/PLY/OBJ con unità esplicite. Revisione delle zone deboli e riesecuzione del solve senza alterare i raw.

Verifica su oggetti reali: superfici opache, riflettenti, poco texturizzate, occlusioni; errori assoluti/relativi, RMS/P95 e ripetibilità. `VALIDATED` soltanto con misure indipendenti.

## RC4 — integrazione Inventor (MVP 4)

Allineamento a tre punti o piano/asse, matrice persistente scan→CAD, reference part, work geometry e parametri. Seguire identità/revisione documento, preview, transazione e rollback. Overlay CAD sul reale. Nessuna conversione implicita della mesh in B-Rep.

Verifica su fixture Inventor dedicata e sul Quest; prova fisica distinta per allineamento e overlay.

## Stato iniziale

RC0 implementato e verificato a livello software: suite completa 1125 PASS, ricontrollo finale mirato 120 PASS e compilazione del runner QA riuscita. Run Quest e prova fisica aperti; dettagli nel [verbale](../../xr-reality-capture-verification.md). RC1–RC4 da implementare. Nessuna scansione reale, ricostruzione metrica o validazione dimensionale viene certificata da RC0.

## Avanzamento software del 24 ottobre 2026

- **RC1, codice di acquisizione presente**: MRUK 207.0.0, camera RGB sinistra, metadata del frame conservati prima della lettura GPU asincrona, intrinseci ricavati dalla proiezione effettiva con crop e risoluzione; posa testa derivata dalla stessa posa camera e dagli estrinseci. Contratti core con trasformazioni rigide in mm, selezione immagini, raw RGB8 immutabili con SHA-256, volume Oggetto/Area, avvio esplicito, pausa/ripresa, interruzione su perdita tracking/recenter/sospensione, quota e riserva di spazio, recupero dopo riavvio e verifica delle sessioni locali. La qualità mostrata è un’euristica sulle immagini; copertura geometrica e accuratezza restano non disponibili.
- **RC2, pipeline sparsa sul PC presente**: al trasferimento HTTPS si aggiungono controlli MCP, worker .NET 8 separato con OpenCV, matching RGB multi-view, triangolazione da pose Quest e bundle adjustment congiunto con quote pesate. Marker ArUco e barra metrica usano corner realmente misurati; distanze e diametro circolare, conflitti e quote di validazione sono espliciti. Disponibili GLB/PLY/OBJ, point cloud, report e revisioni immutabili. Verifiche su dataset sintetico e percorso MCP/HTTP con worker Release reale; [design RC2 PC](../specs/2026-10-24-reality-capture-rc2-pc-design.md).
- **RC3, codice di revisione presente**: viewer Unity separato dal CAD, selezione con ID originali, fitting manuali di punto/asse/piano/cerchio/cilindro, misure in mm e incertezza non stimata, quote note versionate, residui, zone con debole supporto osservativo, nuovo solve e export GLB/PLY/OBJ con unità. [Design e gate RC3](../specs/2026-10-24-reality-capture-rc3-review-design.md).
- **RC4, codice di integrazione presente (25 ottobre)**: allineamento rigido a tre punti o piano/asse con base esplicita LH→RH, matrice versionata, nuova parte workspace con point cloud nativa, work geometry, parametri e provenienza; preview/commit atomico e salvataggio esplicito. Confronto CAD separato e overlay 1:1 subordinato a nuova registrazione fisica, invalidato da tracking/recenter o cambi CAD/PC. Nessuna B-Rep né importazione nativa implicita della mesh. [Design e gate RC4](../specs/2026-10-25-reality-capture-rc4-inventor-design.md).
- **Gate ancora aperti**: ricostruzione densa, dataset fisico controllato, calibrazione dei prior del tracking, prestazioni su acquisizioni reali, collaudo nativo Inventor della nuova reference part, run sintetici Quest e prove fisiche RC1–RC4. La mesh sparsa resta un artefatto separato dalla parte nativa. Il piano completo non è concluso: codice MVP presente, validazione nativa e fisica distinta ancora da svolgere.
- Il runner M1 contiene sottocasi RC1 **sintetici** in uno storage temporaneo separato. Non acquisisce immagini, non invia scansioni e non certifica sincronizzazione, camere o tracking fisico.
- Per richiesta dell’utente, nessun run o test fisico sul Quest viene eseguito durante questa fase di sviluppo. Gli esiti software e gli artefatti sono nel [verbale](../../xr-reality-capture-verification.md).
