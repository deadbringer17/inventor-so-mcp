# M5 — Piano di implementazione Lamiera, Flat Pattern XR e voce

Stato: piano derivato dalla [specifica M5](../specs/2026-09-28-inventor-xr-so-m5-design.md),
non ancora eseguito. La [verifica M4](../../xr-m4-collaudo.md) conserva i gate
fisici aperti; non promuoverli per effetto dei test M5.

## Sequenza

1. [ ] Riutilizzare i probe live lamiera già documentati in `docs/DEVELOPMENT.md`
   e aggiungere su un fixture dedicato solo i casi XR mancanti: preview della
   flangia, pattern già esistente, multi-body, fallimento unfold, revisioni,
   rollback e Undo.
2. [ ] Estrarre la mesh del flat pattern reale in una transazione o lettura
   sicura, senza lasciare il documento in Flat Pattern Edit. Misurare unità,
   orientamento, dimensioni, limiti e identità della cache. Se impossibile,
   fermare il solo percorso `Detach`, non fabbricare una mesh appiattita.
3. [ ] Aggiungere contesto XR lamiera legato a documento/revisione e asset del
   pattern con lifetime separato dalla mesh piegata e dalla preview.
4. [ ] Riutilizzare la sessione Design per regola/spessore, faccia, flangia,
   taglio e creazione sviluppo. Aggiungere i validator lamiera necessari senza
   indebolire `rebuild`/`feature_health` delle parti ordinarie.
5. [ ] Costruire LamieraWorkspace, selezione bordi e manipolatore flangia con
   campo numerico sincronizzato. Coprire scala 1:1/ridotta, Grip sola vista,
   preview obbligatoria, Applica/Annulla e messaggi di errore.
6. [ ] Visualizzare il flat pattern a fianco del piegato; `Detach` muove solo
   la rappresentazione locale. Bloccare riferimenti CAD sul pattern staccato e
   invalidare l'asset su cambio revisione/documento.
7. [ ] Fare uno spike STT locale sul PC con frasi italiane e rumore realistico;
   confrontare accuratezza, latenza, memoria e gestione del microfono. Scegliere
   il motore dopo misure e documentare la ragione della scelta.
8. [ ] Integrare push-to-talk del controller, permesso audio, trasporto
   autenticato e router a vocabolario finito. Condividere command ID/abilitazione
   della UI manuale. Dettatura numerica aggiorna solo il campo armato;
   `Applica` vocale richiede conferma fisica.
9. [ ] Testare .NET, Unity EditMode, Inventor nativo e HTTPS, quindi build
   ordinaria e runner opt-in sul Quest. Il runner dimostra il percorso software,
   non microfono o gesto fisico.
10. [ ] Collaudare sul Quest con l'utente flangia, sviluppo affiancato e voce,
    più regressioni M1–M4; registrare ogni gate M5-01–M5-12 come passato o aperto.

## Dipendenze e disciplina del fixture

Le attività 1–2 precedono il rendering del pattern; 7 precede l'integrazione
del motore vocale. UI e router possono essere sviluppati con fake backend
mentre i probe nativi sono in corso. Usare un fixture lamiera temporaneo
separato dal fixture fisico M4 aperto in Inventor; non alterare o salvare
quest'ultimo. APK ordinario e QA restano distinti. Ogni nuova installazione di
add-in richiede verifica dell'istanza Inventor che l'ha caricata.

## Evidenze da produrre

- Probe nativo e HTTPS con file/log per feature, pattern e rollback.
- Test core di stato/revisione, parser numerico italiano e router dei comandi.
- Unity EditMode per gesto, preview, detach, click-through e cleanup.
- Hash APK/deploy, log runner Quest e verbale fisico separato.
- Matrice M5-01–M5-12 con esito, prova e limiti; nessun `PASS` per i casi non
  esercitati davvero sul controller o microfono.
