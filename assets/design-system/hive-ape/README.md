# HIVE A.P.E. → M8 grapics

Snapshot selettivo del design system **EnerBot**, effettivamente usato da HIVE
nel repository privato [deadbringer17/APE_Hive](https://github.com/deadbringer17/APE_Hive).
Scaricato il 4 ottobre 2026 (Europe/Rome), branch `master`, commit
`436a52a1450be2f3330c055670515df20696f031`.

## Contenuto e precedenza

- `source/colors_and_type.css`: fondazione **caricata dall'app**; colori, font,
  spaziatura, raggi, easing. Sans attuale: **Satoshi**, non Inter.
- `source/app-overrides.css`: componenti e stati della piattaforma realmente
  caricati da `templates/base.html`.
- `fonts/Satoshi-Variable.ttf`: font locale della piattaforma, originale integro.
- `reference/base.html`, `hive-installation-*.html`: riferimenti della UI HIVE;
  sono template Django, non pagine direttamente apribili o codice da eseguire.
- `reference/website-styles.css`, `ds-manifest.json`, `enerbot-ds-readme.md`:
  bundle originale di riferimento; il manifest elenca anche componenti e preview
  che **non sono presenti** nello snapshot. Il README è precedente alla scelta
  Satoshi e cita Inter: prevale il CSS di produzione.
- `reference/enerbot-ds-reskin-design.md`: spec storica, subordinata ai file
  attualmente caricati. Il suo elenco di fogli CSS non coincide con `base.html`.
- `provenance.json`: percorso upstream, URL fissato al commit, Git blob SHA,
  SHA-256 e dimensione dei dieci file scaricati. Ogni blob è stato verificato
  con `git hash-object` dopo il download.

Lo snapshot è una fonte di progettazione esterna alla cartella Unity `Assets`.
Non carica CSS in Unity: i token vengono adattati nei primitivi C# M8. Non sono stati
scaricati backend Django, dati utenti, foto di impianti, loghi EnerBot o librerie
JavaScript: per il tema Inventor SO bastano le fondazioni e i componenti di UI.
L'identità e il nome del prodotto restano Inventor SO / Inventor XR SO.

Il CSS importa Playfair Display e JetBrains Mono da Google Fonts; i relativi
binari **non sono inclusi**. M8 usa Satoshi per i controlli; l'acquisizione di
font complementari e delle relative licenze è un task esplicito del piano.
Il bundle GitHub non conteneva la licenza Satoshi. Nell'implementazione sono
stati acquisiti separatamente i **font statici ufficiali** Medium/Bold da
Fontshare e la [Free Font License](licenses/Satoshi-FFL.txt), con hash in
`font-provenance.json`. `scripts/prepare-m8-fonts.py` riproduce l'estrazione e
rifiuta un archivio con hash diverso prima di scrivere i font. I binari non
sono convertiti né sottoinsiemizzati; SDF/9-slice sono risorse di rendering.
La licenza è referenziata dall'asset tema Unity e copiata nel pacchetto Windows.
Per i nomi CAD greci/cirillici Unity usa un fallback dedicato dal Liberation
già incluso in TMP, con OFL referenziata nel tema; hash e avviso in `licenses/`.
CJK non incluso. Gli asset TMP globali dell'utente non vengono modificati.
Lo snapshot del design system resta materiale di riferimento dal repository
dell'utente: non attribuirgli automaticamente la licenza del gateway.

## Documentazione M8

- [Spec di adattamento](../../../docs/superpowers/specs/2026-10-04-m8-grapics-design.md).
- [Piano di sviluppo](../../../docs/superpowers/plans/2026-10-04-m8-grapics.md).
- [Stato dei gate](../../../docs/xr-m8-verification.md).

I colori web sono dati sRGB: l'implementazione deve verificare la conversione
nel progetto Unity Linear. Dimensioni CSS in px/rem non sono misure fisiche XR.
