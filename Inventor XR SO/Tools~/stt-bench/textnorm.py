"""Normalizzazione del testo italiano condivisa da router, parser e metriche."""
from __future__ import annotations

import re
import unicodedata


def strip_accents(s: str) -> str:
    return "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn")


def basic_normalize(text: str) -> str:
    """Minuscolo, senza accenti/punteggiatura, spazi singoli. Non tocca le cifre."""
    s = strip_accents(text.lower())
    s = re.sub(r"[^a-z0-9\s]", " ", s)
    return re.sub(r"\s+", " ", s).strip()
