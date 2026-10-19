# Sorgenti dei collaudi S1

Sono versionati esplicitamente i wrapper/verificatori Copia88–94 e i client
Core usati dai relativi collaudi. La directory artifacts resta ignorata per
i dati generati: bin/obj, DLL/APK, bootstrap, certificati, log, documenti CAD,
snapshot raw e copie congelate dei singoli tentativi non vanno committati.

I progetti host nativi sono in bridge/tests. Compilare host e client in serie
prima di usare il wrapper corrispondente; per94 vedere
bridge/tests/S1CurveCopyCoreOwnedHost94/README.md e
docs/s1-copy-core-curves94.md. Gli script che avviano Inventor richiedono
Windows e Inventor2027; i verificatori degli archivi non avviano COM.

I report in docs distinguono profili verificati e requisiti ancora aperti.
Il codice sperimentale presente non costituisce completamento dell'intera S1.
