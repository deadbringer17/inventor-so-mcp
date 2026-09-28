# stt-bench — spike STT locale per la voce M5

Harness per la decisione aperta 2 della [spec M5](../../../docs/superpowers/specs/2026-09-28-inventor-xr-so-m5-design.md)
(sezione *Voce: comando specchio e dettatura numerica*) e il passo 7 del
[piano M5](../../../docs/superpowers/plans/2026-09-28-inventor-xr-so-m5.md):
confrontare almeno due motori STT **locali** sul PC su frasi italiane del
vocabolario M5, misurando accuratezza, latenza e memoria.

> **Il motore, il runtime e il budget si scelgono solo con misure fatte sulla
> postazione Windows reale**, col microfono reale e il rumore reale. I risultati
> ottenuti su Linux, su un altro PC o con audio sintetico (`--smoke`) non sono
> una decisione. Fino ad allora la voce M5 resta **aperta**.

`Tools~` e ignorata da Unity. Tutto e Python 3.12 (provato anche su 3.11), senza cloud.
Il router e il parser numerico qui dentro sono **di riferimento**: i command ID sono
provvisori e vanno allineati a quelli della UI manuale quando si integra la voce.

## Contenuto

| File | Scopo |
|---|---|
| `corpus/phrases.json` | 79 frasi: comandi + alias, dettatura numerica, frasi da rifiutare (fonte: `corpus/build_phrases.py`) |
| `record.py` | registratore guidato, WAV 16 kHz mono per frase e condizione |
| `bench.py` | esegue i motori sulle registrazioni e scrive `results/<timestamp>/` |
| `router.py`, `numbers_it.py`, `textnorm.py` | router a vocabolario finito e parser di numeri italiani |
| `engines/` | interfaccia comune: `fwhisper`, `vosk_engine` (anche variante grammatica), `whispercpp`, `mock` (solo smoke) |
| `tests/` | pytest su router, parser, metriche (nessun audio, nessun motore) |

## Installazione

```powershell
cd "Inventor XR SO\Tools~\stt-bench"
py -3.12 -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt          # numpy, sounddevice, psutil, pytest
pip install faster-whisper               # motore 1
pip install vosk                         # motore 2
pip install pywhispercpp                 # opzionale (whisper.cpp)
```

- **GPU NVIDIA** per faster-whisper `cuda`: servono driver recenti e le librerie
  CUDA 12 + cuDNN 9 richieste da CTranslate2 (vedi la documentazione di
  faster-whisper). Se mancano, l'engine `...:cuda` viene saltato con il motivo.
  `nvidia-smi` nel PATH abilita la misura VRAM.
- Se non c'e una GPU, misura solo `cpu` e `int8`: e quello che conta se la GPU della
  postazione e occupata da Inventor.

### Modelli

| Motore | Modello | Note |
|---|---|---|
| faster-whisper | `small`, `medium` (anche `large-v3`, `turbo`) | scaricati da Hugging Face al primo uso (serve rete una tantum); usa `--model-dir` per una cache locale. Per lavorare offline copia la cache e non serve altro |
| Vosk | `vosk-model-it-0.22` (grande) e/o `vosk-model-small-it-0.22` | da <https://alphacephei.com/vosk/models>, scompatta e passa la cartella con `--vosk-model`. Confronta grande e piccolo con due esecuzioni |
| whisper.cpp | `small`, ecc. | scaricato da pywhispercpp |

Verifica licenze dei modelli prima di distribuirli con il prodotto.
I modelli non vanno nel repository (`models/` e ignorata).

## Protocollo di registrazione

Obiettivo: audio rappresentativo di cio che arrivera al motore in produzione.

1. **Microfono**: il push-to-talk M5 usa il Quest 3; l'audio verrebbe trasmesso al
   PC. Registra quindi **con il microfono del Quest** (es. app di registrazione
   sul visore, copia i file sul PC) oppure con una **cuffia/microfono a distanza
   simile** (2–5 cm dalla bocca per cuffia, circa 10–15 cm per il microfono del
   visore). Idealmente fai entrambe le sessioni in cartelle di condizione
   diverse (`quiet-quest`, `quiet-headset`). Il codice di trasmissione Quest→PC non
   e coinvolto: qui si misura solo il motore.
2. **Stesso parlante e stessa postazione** in tutte le condizioni, cosi i confronti
   sono per condizione e non per voce. Se piu utenti useranno il sistema, ripeti con
   almeno un secondo parlante (cartella dedicata).
3. **Condizioni minime**: `quiet` (stanza silenziosa), `office` (rumore d'ufficio,
   conversazioni, tastiere), `machine` (rumore reale dell'officina/macchinari, se
   e l'uso previsto). Il rumore si crea nell'ambiente, non in post-produzione.
4. **Stile**: frasi brevi come in push-to-talk: premi, parla subito, rilascia. Parla
   in modo naturale, non scandito. Le frasi "da rifiutare" vanno pronunciate come
   scritte (servono a misurare i falsi accept).
5. Registra almeno **una ripetizione per frase**; per numeri ambigui ripeti in una
   seconda sessione (`--skip-existing` non sovrascrive: usa un'altra condizione o
   cartella).

```powershell
python record.py --list-devices
python record.py --out D:\stt-recordings --condition quiet --device 1
python record.py --out D:\stt-recordings --condition office
python record.py --out D:\stt-recordings --condition machine --kind numeric   # solo un sottoinsieme
```

Modalita `enter` (default): Invio per iniziare, Invio per fermare, poi salvare o
ripetere. Modalita `--mode fixed --max-seconds 4`: durata fissa. Le registrazioni
finiscono in `<out>/<condizione>/<id>.wav`. Tienile **fuori dal repository**
(`.gitignore` locale esclude comunque `recordings/`, `results/`, `*.wav`): l'audio
non va mai committato.

## Esecuzione

```powershell
python bench.py --recordings D:\stt-recordings `
  --engines faster-whisper:small:int8:cpu faster-whisper:medium:int8:cpu faster-whisper:small:float16:cuda vosk vosk-grammar `
  --vosk-model D:\models\vosk-model-it-0.22
```

Sintassi `--engines`: `faster-whisper:<modello>:<compute_type>:<device>`, `vosk`,
`vosk-grammar` (Vosk con grammatica ristretta al vocabolario M5), `whispercpp:<modello>`.
Motori o modelli mancanti vengono saltati con il motivo, non bloccano il run.

- **Un motore per invocazione** se vuoi memoria pulita (il picco RSS e del
  processo intero): lancia `bench.py` una volta per motore e confronta i `results.json`.
- Il primo audio serve da riscaldamento e non e misurato. Ripeti il run piu volte e
  a caldo/freddo per vedere la variabilita; chiudi Inventor o lascialo aperto in
  base a cio che vuoi misurare (la postazione reale ha Inventor in esecuzione: misura
  in quello scenario).
- `--no-transcripts` evita di salvare le trascrizioni in `results.json`;
  `--verbose` stampa solo id ed esito. Le trascrizioni sono solo del corpus di test
  e non vanno in log ordinari.
- Smoke test senza audio ne modelli: `python bench.py --smoke` (motore finto
  `mock`, numeri privi di valore).
- Test: `python -m pytest tests`.

## Come leggere i risultati

`results/<timestamp>/summary.md` ha una tabella per condizione; `results.json` ha
il dettaglio per frase (trascrizione, esito, latenza) e le risorse.

| Colonna | Significato |
|---|---|
| Intent acc. | frazione di frasi trattate correttamente da router + parser: comando giusto, valore+unita giusti, frase fuori vocabolario rifiutata |
| Errori pericolosi | comando sbagliato, valore sbagliato accettato, frase fuori vocabolario accettata. Sono i casi che, senza la conferma fisica di Applica, potrebbero far agire il sistema male |
| Falsi accept OOV | sottoinsieme: frasi da rifiutare accettate |
| WER/CER | distanza dal testo atteso dopo normalizzazione (cifre convertite in parole; approssimato sui numeri) |
| Lat. p50/p95/max | tempo dalla fine dell'audio al testo finale, solo motore (nessun trasporto Quest→PC) |
| RTF | tempo di elaborazione / durata audio |
| Load s | caricamento modello |
| RSS / VRAM | picco memoria del processo / GPU (`rss_delta_mib` in JSON = incremento dovuto al motore) |

Il compromesso chiave da guardare: **`vosk-grammar`** riduce gli errori di
vocabolario e la latenza ma, per costruzione, forza qualunque suono verso una frase
valida (rischio di falsi accept); Whisper e libero ma puo trascrivere in modo
creativo. Confronta accuratezza sulle frasi valide *e* falsi accept sulle frasi da rifiutare.

## Criteri di decisione (da compilare dall'utente)

I valori sono **proposte iniziali**, da confermare o cambiare; nessuna soglia e
decisa. Compila la colonna "Valore scelto" dopo aver visto le misure reali.

| Criterio | Proposta (da rivedere) | Valore scelto |
|---|---|---|
| Intent accuracy, condizione `quiet` | >= 95% | ______ |
| Intent accuracy, condizione `office` | >= 90% | ______ |
| Intent accuracy, condizione `machine` | >= ___% | ______ |
| Dettatura numerica corretta (valore+unita) | >= 95% | ______ |
| Errori pericolosi / falsi accept OOV | 0 | ______ |
| Latenza p95 (fine audio → testo) | <= 1500 ms | ______ |
| Latenza max | <= ___ ms | ______ |
| RAM (RSS) motore | <= ___ MiB | ______ |
| VRAM | <= ___ MiB (o "solo CPU") | ______ |
| Tempo di caricamento modello | <= ___ s | ______ |
| Parlanti / microfoni coperti | >= 1 parlante, microfono Quest | ______ |

Motore scelto: ______  ·  modello/compute type: ______  ·  data: ______  ·
postazione (CPU/GPU/RAM): ______  ·  motivazione: ______

Se nessun candidato raggiunge i gate, la voce M5 resta aperta e i comandi manuali
continuano a funzionare (come da spec). Riporta l'esito nel file di verifica/collaudo
della milestone, non come passato sulla base di questo harness da solo.

## Privacy

L'harness elabora solo le registrazioni del corpus di test che hai fatto tu, in
locale, senza rete (salvo il download iniziale dei modelli). Non scrive audio o testo
in log; le trascrizioni stanno solo in `results/` (ignorata da git).
