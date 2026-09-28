"""WER/CER, percentili e helper di misura. Nessuna dipendenza esterna."""
from __future__ import annotations

import re
from typing import Sequence

from numbers_it import integer_to_words
from textnorm import basic_normalize, strip_accents


def normalize_for_wer(text: str) -> str:
    """Porta le cifre a parole (12,5 -> dodici virgola cinque) e uniforma le unita."""
    s = strip_accents(text.lower())
    s = re.sub(r"(\d)\s*[,.]\s*(\d)", r"\1 virgola \2", s)
    s = re.sub(r"(^|\s)[-−]\s*(?=\d)", r"\1meno ", s)
    s = s.replace("°", " gradi ")
    s = re.sub(r"(\d)\s*mm\b", r"\1 millimetri", s)
    s = re.sub(r"\bmm\b", "millimetri", s)

    def repl(m: re.Match) -> str:
        digits = m.group(0)
        n = int(digits)
        return " " + (integer_to_words(n) if n <= 999_999 and not digits.startswith("0") or digits == "0"
                      else " ".join(integer_to_words(int(c)) for c in digits)) + " "

    s = re.sub(r"\d+", repl, s)
    return " ".join(w for w in basic_normalize(s).split() if w != "e")


def _edit_distance(a: Sequence, b: Sequence) -> int:
    prev = list(range(len(b) + 1))
    for i, x in enumerate(a, 1):
        cur = [i]
        for j, y in enumerate(b, 1):
            cur.append(min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + (x != y)))
        prev = cur
    return prev[-1]


def error_counts(ref: str, hyp: str) -> tuple[int, int, int, int]:
    """(errori_parola, parole_ref, errori_car, car_ref) sui testi normalizzati."""
    r, h = normalize_for_wer(ref), normalize_for_wer(hyp)
    rw, hw = r.split(), h.split()
    return _edit_distance(rw, hw), len(rw), _edit_distance(r, h), len(r)


def percentile(values: Sequence[float], p: float) -> float:
    if not values:
        return float("nan")
    v = sorted(values)
    k = (len(v) - 1) * p / 100.0
    lo = int(k)
    hi = min(lo + 1, len(v) - 1)
    return v[lo] + (v[hi] - v[lo]) * (k - lo)
