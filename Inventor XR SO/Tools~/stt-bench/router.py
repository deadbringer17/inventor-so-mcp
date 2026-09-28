"""Router di riferimento a vocabolario finito: testo -> command ID o rifiuto.

Solo per il benchmark. I command ID sono PROVVISORI: in M5 devono coincidere
con quelli della UI manuale. Corrispondenza esatta dopo normalizzazione, nessuna
interpretazione di testo libero.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

from textnorm import basic_normalize

# command_id -> alias italiani (il primo e il nome del pulsante nel mirror)
VOCABULARY: dict[str, list[str]] = {
    "sheetmetal.flange": ["flangia", "fai una flangia", "crea una flangia", "aggiungi flangia"],
    "sheetmetal.flat_pattern": ["crea sviluppo", "sviluppo piano", "fai lo sviluppo", "crea lo sviluppo"],
    "feature.chamfer": ["smusso", "fai uno smusso", "crea uno smusso", "smussa lo spigolo"],
    "feature.fillet": ["raccordo", "fai un raccordo", "crea un raccordo", "aggiungi raccordo"],
    "inspect.measure": ["misura", "misurazione", "fai una misura", "avvia misura"],
    "inspect.isolate": ["isola", "isola componente", "isola il pezzo", "fai isolamento"],
    "draft.cancel": ["annulla", "annulla bozza", "annulla operazione", "torna indietro"],
    "draft.apply": ["applica", "conferma", "applica modifica", "applica operazione"],
    "sketch.create": ["crea schizzo", "nuovo schizzo", "fai uno schizzo", "crea uno schizzo"],
}

# Comandi che la voce non puo mai eseguire da sola (serve pressione fisica).
NEEDS_PHYSICAL_CONFIRM = {"draft.apply"}


def _build_index() -> dict[str, str]:
    index: dict[str, str] = {}
    for cid, aliases in VOCABULARY.items():
        for a in aliases:
            key = basic_normalize(a)
            if key in index and index[key] != cid:
                raise ValueError(f"alias ambiguo: {key!r}")
            index[key] = cid
    return index


_INDEX = _build_index()


@dataclass
class RouteResult:
    command_id: Optional[str]  # None = rifiutato
    normalized: str = ""


def route(text: str) -> RouteResult:
    key = basic_normalize(text)
    return RouteResult(_INDEX.get(key), key)


def grammar_phrases() -> list[str]:
    """Frasi per il decoding vincolato di Vosk: alias, numeri, segni, unita."""
    from numbers_it import integer_to_words

    phrases = sorted(_INDEX)
    words = set()
    for n in range(0, 10):
        words.add(integer_to_words(n))
    for n in list(range(10, 100)) + list(range(100, 1000, 10)) + [1000, 1050]:
        words.add(integer_to_words(n))
    return phrases + sorted(words) + ["virgola", "meno", "piu", "e", "mille", "millimetri", "gradi", "[unk]"]
