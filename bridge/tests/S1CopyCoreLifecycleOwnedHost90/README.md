# Host Core lifecycle90

Clone sorgenti89, senza bin/obj. Assemblaggio `S1CopyCoreLifecycleOwnedHost90`.
Protocollo: `artifacts/s1-verification/increment90/PROTOCOL.md`.

Comando nuovo stdin: `copy-core90`; legge il request fisso dalla directory
privata owned e produce ack univoca atomica + marker
`COPY_CORE90_ACKNOWLEDGED`. HTTP resta vivo. Il ramo lifecycle89 automatico
è rimosso dal clone; CopyLifecycle89.cs conserva solo guardie/binding/reader/
comparatore riusati. Le letture legacy88 sono bloccate durante la sequenza90.

Partial nuovi: CopyCoreProtocol90 (14stati/nonce/metadata),
CopyCoreSourceEdits90 (4edit e4inspect separati), CopyCorePersistence90
(2SaveCopy-close-Open e2inspect_reopen separati). Prepare richiede VerifyCopies88
nel medesimo processo. Ack contiene due documenti; parameter_key è Base64
nativa, source_ids/sketch_id sono PersistentEntityReference opache senza
revision. Nessuna geometria o glifo nell'ack.

Zero readback semantici/GetFacets dopo End o Open prima degli inspect inviati
dal client dopo la prima snapshot Core. Baseline prima edit e oracle dopo
Abort fallito sono consentite e archiviate. HTTP/client quiescence e ordine
delle prime letture richiedono il wrapper/client90 indipendenti; l'host non
finge di osservare il rendering o un LastCommit del prodotto.

Report copy-native-lifecycle90.json: source_updates/reopens interi,2documenti,
2updates per documento con copies_independent_exact, persistence con paths,
hashes, stato reopened_verified e raw first native read. La sola
normalizzazione consentita è OwnedParameterRelocation89 sul Parameter text
NON copiato della fixture, con binding locale/value/key esatti.

Files73.cs e OwnedParameterRelocation89.cs copiati dalla versione actual89
con pubblicazione atomica/retry bounded per known owned target. Nessuna build,
test, COM o Inventor eseguiti dall'agente implementatore; gate da verificare
serialmente dal coordinatore. Non abilita capacità nuove in produzione/UI.
