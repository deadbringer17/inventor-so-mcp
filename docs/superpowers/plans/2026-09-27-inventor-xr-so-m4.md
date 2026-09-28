# M4 — Piano di implementazione Assembly

Stato: codice, test PC e runner automatico sul Quest completati;
Grip+Trigger, anteprima e commit nativo osservati nel visore. I restanti
controlli fisici di ergonomia e tracking sono ancora aperti. La selezione A/B
è stata osservata. Il batch autonomo ha isolato e corretto il rifiuto del
Joint Planar su due facce piane; l'add-in aggiornato ha passato anteprima,
Applica e Annulla nell'Inventor reale tramite HTTPS.
Evidenze e lavoro residuo: [collaudo M4](../../xr-m4-collaudo.md).
Contratto: [specifica M4](../specs/2026-09-27-inventor-xr-so-m4-design.md).
La base include le modifiche locali M1–M3: non ripristinare file o rigenerare
la scena per eliminare differenze preesistenti.

## Sequenza e dipendenze

1. [x] Caratterizzare Inventor 2027 su un assieme temporaneo: DOF liberi,
   slider, rotational, geometrie proxy, risoluzione della posa e rollback.
   Stabilire come normalizzare le direzioni senza dedurle dai conteggi.
   Gate: A03–A05 riproducibili a livello nativo.
2. [x] Introdurre query di contesto revision-bound, DTO e capability specifica.
   Registrare schema/tool/catalogo; testare dati null, non finiti, limiti,
   riferimenti di altra occurrence/documento e risposte stale.
3. [x] Estrarre primitive di movimento/vincolo/joint usabili dalla transazione
   atomic; preservare gli handler pubblici esistenti. Aggiungere i tre comandi
   assembly sperimentali e i controlli di document kind.
4. [x] Estendere la cattura preview alle geometrie assembly nella transazione.
   Pubblicare asset separati dalla cache live, senza riferimenti transitori;
   rifiutare export incompleto. Verificare coordinate, normali, istanze ripetute,
   sottoassiemi e rollback della revisione.
5. [x] Adattare il ciclo di bozza M3 al tipo documento e al blocco condiviso
   delle mutazioni. Testare rendering richiesto, expiry, richieste tardive,
   cambio modalità e commit incerto. Mantenere i validator part di Design.
6. [x] Integrare AssemblyWorkspace, selezione A/B, geometrie di riferimento,
   gizmo DOF, tastiera, gesto intenzionale e preview. Cablaggio tramite
   AppController/menu polso, senza rigenerazione indiscriminata di Main.unity.
7. [x] Completare workflow contestuale, opzioni dei vincoli, joint supportati,
   attivazione esplicita dei sottoassiemi e messaggi per casi indisponibili.
8. [x] Eseguire suite .NET e Unity EditMode, compilazione add-in e build Android;
   eseguire prove native e HTTPS integrate; registrare risultati separatamente.
9. [ ] Collaudo Quest A01–A15 con Inventor reale. Il runner automatico ha passato
   preview/Cancel/Apply/Undo/Redo/stale/riapertura. Grip+Trigger sul cilindro,
   anteprima da 3 mm e commit nativo sono stati osservati. Grip semplice e
   selezione A/B sono stati confermati. Il caso Joint Planar con facce piane
   passa il probe integrato con gap 30 mm, inclusi Applica e Annulla; resta
   la conferma fisica nel visore insieme a leggibilità, tracking, scala ridotta
   e confronto tra gesto e input numerico. Distinguere implementato, verificato
   automaticamente e accettato nel verbale.

Le attività 2–4 dipendono dalle conclusioni native dell'attività 1; l'UI non
deve inventare assi/semantiche per aggirare una limitazione del backend.

## Punti di modifica da verificare

| Area | File o componenti esistenti |
|---|---|
| transazioni e mesh | `AtomicCadBatch`, `AtomicBatchHandler`, `XrHandlers`, `PreviewMeshAsset` |
| operazioni native | `MoveComponentHandler`, `CreateConstraintHandler`, `CreateJointHandler` |
| contratti e tool | `CadBatchCommandCatalog`, `ToolContracts`, `XrTools`, `PlanningTools`, registro experimental |
| stato client | `DesignSession`, `DesignBackend`, `DesignHistory`, `InventorBackend` |
| input e UI | `AppController`, `InspectWorkspace`, `ControllerRay`, `HomePanel` |
| visuali e contesto | `DesignPreviewView`, `CadSceneView`, `SelectionVisuals`, `BrowserContext` |

## Comandi di verifica di base

Eseguire dalla radice del repository; i processi di test backend devono usare
la configurazione sperimentale prevista dai test, senza ereditare per errore
quella dell'host di produzione.

```powershell
dotnet test bridge/tests/Bimwright.Ipt.Tests
dotnet test "Inventor XR SO/Tests~/XrSo.Core.Tests"
dotnet build bridge/src/plugin-so27/Inventor.So.AddIn.csproj -p:SoExperimental=true
```

Per Unity usare `Inventor XR SO/Tools~/Invoke-Unity.ps1`, come documentato nel
README del client, preservando l'Editor eventualmente aperto. Le prove native
devono creare/chiudere solo propri documenti temporanei e ripristinare il
documento attivo precedente. Non installare automaticamente una build di test
al posto dell'add-in usato dalla sessione corrente.

Ogni gate registra data, configurazione, comando, conteggi test, log e limiti.
Nessuna casella va marcata completata sulla sola base di questa pianificazione.
