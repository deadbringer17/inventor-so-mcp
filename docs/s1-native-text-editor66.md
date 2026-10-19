# S1 — Fondazione editor XR del testo, incremento 66

14 ottobre 2026. **18 gate S1 OPEN**; capacità testo non ancora pubblicate.

L'editor condiviso Parte/Lamiera prepara bozze per testo adattato o in
rettangolo, combinando elementi letterali, nomi di parametri locali e a
capo. Posizione e due angoli accettano input numerico o penna. È possibile
scegliere uno stile del documento per nome, sostituire interamente il
contenuto di un testo selezionato, modificarne solo la posizione o eliminarlo.
Ogni operazione mantiene il flusso esplicito Anteprima → Applica.

Il composer mantiene distinti testo e campi parametrici: non importa XML
nativo né interpreta markup digitato. Verifica massimo 64 elementi/4096
caratteri letterali e Unicode valido. Parametri e stili sono risolti dal
backend nativo durante la preview. La composizione e i callback sono
revocati al cambio di documento/revisione, refresh, cancellazione o chiusura.
Update/delete richiedono un singolo riferimento intero corrente; dopo una
revisione nuova occorre riconfermare la selezione senza perdere il contenuto
congelato della bozza.

Le azioni vengono emesse solo quando il backend pubblica le corrispondenti
capacità. Il backend attuale continua a nascondere text_create/update/delete:
questo incremento prepara l'editor, non abilita un comando incompleto.

## Evidenze

`text66-unity-r2.xml`: **14/14 EditMode PASS** con backend fittizio che
pubblica esplicitamente le capacità. Copre payload strutturato, fitted e
rettangolo, input penna/numerico, stile/default, update/delete, limiti,
Unicode non valido, cancellazione, refresh e riconferma nella revisione
successiva. Contatori verificano che la composizione non invochi preview
o commit implicitamente. Non simula il rendering della preview nativa.

Il primo tentativo non compilava per un import mancante di QuantityUnit;
corretto l'import. Sorgenti e log r1 conservati; nessun test eseguito in r1.
I sorgenti r2 sono conservati separatamente. La regressione completa,
**1.084/1.084 PASS**, è registrata in `text66-unity-full-r3.xml` con i
binari effettivi archiviati.
Gli assembly del focused r2 non sono stati copiati separatamente prima del
full r3: la provenienza binaria conservata è quella della regressione completa,
con sorgenti UI invariati fra r2 e r3.

**NOT COVERED:** UI con backend testo pubblicato, lista/selettore dei
parametri o degli stili, editing del contenuto nativo preesistente con
formattazione conservata, opzioni complete di font/allineamento/orientamento,
testo su geometria, oracle desktop, runner Quest e prove fisiche. Le prove
native60–63 del backend sono evidenze distinte dai test UI di questo incremento.
Nessuna nuova APK creata o installata.
