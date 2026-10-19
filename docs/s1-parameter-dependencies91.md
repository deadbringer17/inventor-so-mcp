# S1 — ricerca delle dipendenze native del testo parametrico

La sonda PC91 è compilata, senza errori. Rimane l'avviso NU1903 della
dipendenza SQLite già presente nel server. R1 completa le osservazioni:
venti parametri, dodici dipendenti e nessun collegamento diretto ai quattro
TextBox noti. I due parametri S1Copy88Length, cui i testi sono legati,
restituiscono entrambi una raccolta Dependents vuota. L'esito di ownership
rimane UNKNOWN, senza dedurre assenza di binding.

Il confronto indipendente conferma lo stato nativo completo prima/dopo
esatto. Cleanup normale, processo dedicato terminato, manifest e DLL
ripristinati. `increment91/native-r1/verification-dependencies91-r1.json`
registra VERIFIED_OBSERVATIONS_READONLY_ONLY: nessun resolver generico o
supporto alla copia dei campi parametro è dimostrato da questa prova.

Legge `Parameter.Dependents` attraverso le interfacce pubbliche della
interop Inventor2027 effettivamente installata. Confronta l'identità COM
di ogni dipendente con i TextBox registrati dai creator delle fixture
Parte/Lamiera, includendo parametri omonimi con identico valore13mm.
Richiede lo stato nativo completo prima/dopo esatto, inclusi geometria,
glifi, chiavi, stili, parametri, Dirty e DatabaseRevision.

La ricerca non modifica parametri per dedurre il legame. Un elenco vuoto,
un tipo non disponibile o un errore sono UNKNOWN, senza autorizzare la
copia. Un eventuale collegamento diretto al TextBox è aggregato e non
risolve automaticamente l'owner di ciascun campo XML.

Il primo profilo usa documenti salvati e campi con percorso assoluto
locale. Alias storici, documenti non salvati, campi esterni, testi
multicampo e altre famiglie di parametri restano da provare. Nessuna
capacità di produzione viene abilitata dalla sola ricerca; tutti i18
gate complessivi S1 restano aperti e Quest rimane rinviato.

Ricerca preliminare:
`artifacts/s1-verification/increment91/parameter-copy-binding-research.md`.
Sonda: `bridge/tests/S1ParameterDependencyOwnedHost91/`.
Wrapper separato: `artifacts/s1-verification/run-parameter-dependencies91.ps1`.
