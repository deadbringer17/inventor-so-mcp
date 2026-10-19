# S1 — Runner testo e parametri74

Il runner QA conserva gli otto casi linea38 e aggiunge apertura, lettura,
selezione e highlight dei testi nativi in Parte e Lamiera. Confronta mesh,
indici, colori, bounds e riferimenti con lo snapshot corrente, e verifica
che lettura e selezione non cambino revisione o contenuto nativo.

In Progettazione aggiunge valori Boolean/Text, espressioni parametriche con
Preview/Cancel e Preview/Apply20→35mm, consumatori testo/glifi e CRUD di un
parametro inutilizzato. Usa azioni di catalogo e keypad reali con input
sintetico; il renderer del prodotto conferma il piano. Non costruisce bozze
nel runner e non simula la conferma del rendering.

I1.107 test Unity EditMode passano, senza fallimenti o test saltati. Questi
test comprendono i contratti del runner ma non eseguono il percorso Android.
La build QA e ordinaria termina con exit0; il manifest Android della QA
conferma `debuggable=true`, necessario al trasferimento bootstrap via stdin
con `run-as`. I campi Entry usati per ispezionare le mesh hanno Preserve solo
nelle build QA.

APK in `artifacts/s1-verification/increment74/apk-build-r1`:

- QA: SHA256 `0A9DB3689D7F41831F90AA4946DB029BB4C28AF0B0555509C01FED71F5DF97F3`.
- Ordinaria: SHA256 `EA9A2BEDF881783D5C27D9FE70F58F607181D9ADB32FEA05FB1A5D75AA0D86AF`.

Questo incremento archivia compilazione, sorgenti e APK. L'esecuzione Quest
è documentata separatamente nell'incremento75; non viene dedotta dalla
build. Il runner termina PARTIAL anche quando i sottocasi passano: parametri
dentro lo schizzo/Lamiera, varianti restanti, desktop, WiFi, performance e
prova fisica rimangono esplicitamente non coperti. Testo authoring e copia
non sono abilitati. Tutti i18gate complessivi restano aperti.
