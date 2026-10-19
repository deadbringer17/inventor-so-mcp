# S1 — Lifecycle del layer immagini, incremento 58

14 ottobre 2026. **PASS mirato nel Play Mode dell’Editor; 18 gate S1 OPEN.**
Il test attraversa realmente `EnterPlayMode`/`ExitPlayMode`, disattiva il
GameObject del layer e lo distrugge. Non chiama manualmente `OnDisable` o
`OnDestroy` e non modifica la logica di produzione.

La fixture usa il client PNG Core reale, decodifica Unity e risorse GPU
effettive; il backend CAD è fittizio. Verifica una texture/quad caricati,
azzeramento dei contatori dopo disattivazione, distruzione di mesh/materiale/
texture nel frame successivo, cancellazione della richiesta pendente e
rilascio dell’asset restituito tardivamente da un backend che ignora la
cancellazione. Riattiva il layer, carica una nuova revisione, distrugge il
GameObject e controlla nuovamente risorse e gerarchia possedute.

Run `image-unity56-58-r5.xml`: **7/7 PASS**, comprendenti questo test e sei
test UI dell’incremento 56. Sorgente e assembly effettivi conservati in
`artifacts/s1-verification/increment58/run-r5/`; gli esperimenti precedenti
restano in `run-r1`…`run-r4`, con risultati e diagnostici distinti.
La regressione generale `image-unity56-58-full-r6.xml` passa
**1.066/1.066** test; copie degli assembly in `increment56/unity-full-r6/`.

I primi quattro tentativi fallivano nel test: la lambda di `Assert.Throws`
catturava una variabile della coroutine, generando una closure prima del
passaggio a Play Mode. La diagnosi è sostenuta dal task tardivo osservato
`RanToCompletion` e dall’eccezione sull’assegnazione alla variabile catturata.
Spostare soltanto l’asserzione in un helper crea la closure dopo il domain
reload; tutte le asserzioni sulle risorse restano attive e passano. I primi
run comprendevano anche due errori nei test UI 56, documentati separatamente:
ordine di cleanup e messaggio di apertura sovrascritto dalla selezione.

**NOT COVERED:** Inventor/HTTP in questo test, APK aggiornata, Quest,
controller fisici, tracking, prestazioni del dispositivo e budget GPU.
Un PASS nel Play Mode del PC non chiude i relativi gate XR.
