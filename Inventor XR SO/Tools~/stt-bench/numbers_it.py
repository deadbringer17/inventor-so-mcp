"""Parser di numeri italiani a parole con segno e unita (mm / gradi).

Riferimento per il benchmark, non codice di produzione. Accetta anche cifre
("12,5 millimetri"), perche molti motori STT le emettono al posto delle parole.
"""
from __future__ import annotations

import math
import re
from dataclasses import dataclass
from typing import Optional

from textnorm import strip_accents

MAX_ABS = 100_000.0  # proposta: fuori range oltre questo valore

_UNITS = {"zero": 0, "uno": 1, "un": 1, "due": 2, "tre": 3, "quattro": 4, "cinque": 5,
          "sei": 6, "sette": 7, "otto": 8, "nove": 9}
_TEENS = {"dieci": 10, "undici": 11, "dodici": 12, "tredici": 13, "quattordici": 14,
          "quindici": 15, "sedici": 16, "diciassette": 17, "diciotto": 18, "diciannove": 19}
_TENS = {"venti": 20, "trenta": 30, "quaranta": 40, "cinquanta": 50, "sessanta": 60,
         "settanta": 70, "ottanta": 80, "novanta": 90}
# Radici elise davanti a "uno"/"otto": ventuno, trentotto, centotto...
_STEMS = {"vent": 20, "trent": 30, "quarant": 40, "cinquant": 50, "sessant": 60,
          "settant": 70, "ottant": 80, "novant": 90}

# atomo -> (categoria, valore)
_ATOMS: dict[str, tuple[str, int]] = {}
_ATOMS.update({k: ("unit", v) for k, v in _UNITS.items()})
_ATOMS.update({k: ("teen", v) for k, v in _TEENS.items()})
_ATOMS.update({k: ("tens", v) for k, v in _TENS.items()})
_ATOMS.update({k: ("stem", v) for k, v in _STEMS.items()})
_ATOMS.update({"cento": ("hundred", 100), "cent": ("hstem", 100),
               "mille": ("mille", 1000), "mila": ("mila", 1000)})
_ATOM_KEYS = sorted(_ATOMS, key=len, reverse=True)

_UNIT_WORDS = {
    "millimetro": "mm", "millimetri": "mm", "mm": "mm",
    "grado": "deg", "gradi": "deg",
}
_AMBIGUOUS_UNITS = {"centimetro", "centimetri", "cm", "metro", "metri", "m",
                    "pollice", "pollici", "pollici", "micron", "micrometri", "radianti"}
_MINUS = {"meno"}
_PLUS = {"piu"}


@dataclass
class Quantity:
    ok: bool
    value: Optional[float] = None
    unit: Optional[str] = None  # "mm", "deg" oppure None (unita non pronunciata)
    reason: str = ""


def _fail(reason: str) -> Quantity:
    return Quantity(False, reason=reason)


def _segment(word: str) -> Optional[list[str]]:
    """Spezza una parola composta ("centoventitre") in atomi noti."""
    if not word:
        return []
    for k in _ATOM_KEYS:
        if word.startswith(k):
            rest = _segment(word[len(k):])
            if rest is not None:
                return [k] + rest
    return None


def _atoms(tokens: list[str]) -> Optional[list[str]]:
    out: list[str] = []
    for t in tokens:
        seg = _segment(t)
        if not seg:
            return None
        out.extend(seg)
    return out


def parse_integer_words(tokens: list[str]) -> Optional[int]:
    """Valore intero da parole (gia normalizzate, senza "e" residue). None se invalido."""
    tokens = [t for t in tokens if t != "e"]
    atoms = _atoms(tokens)
    if not atoms:
        return None
    total = 0
    cur = 0
    prev_cat = ""
    seen_any_group = False
    for i, a in enumerate(atoms):
        cat, val = _ATOMS[a]
        nxt = atoms[i + 1] if i + 1 < len(atoms) else ""
        if cat in ("stem", "hstem"):
            if not nxt or nxt[0] not in "uo":  # solo davanti a uno/un/otto/ottanta
                return None
            if cat == "hstem" and nxt not in ("otto", "ottanta", "ottant"):
                return None
            if cat == "stem" and nxt not in ("uno", "un", "otto"):
                return None
        if cat in ("unit", "teen", "tens", "stem"):
            if val == 0 and (len(atoms) > 1):
                return None  # "zero" solo da solo
            if prev_cat in ("unit", "teen"):
                return None
            if prev_cat == "tens" and cat != "unit":
                return None
            if prev_cat == "tens" and cat == "unit" and val == 0:
                return None
            cur += val
        elif cat in ("hundred", "hstem"):
            if prev_cat in ("tens", "teen", "hundred", "hstem") or (prev_cat == "unit" and cur >= 10):
                return None
            if cur >= 100 and cat == "hundred":
                return None
            cur = (cur or 1) * 100
            if cur > 900:
                return None
        elif cat == "mille":
            if cur >= 1000 or seen_any_group:
                return None
            total += (cur or 1) * 1000
            cur = 0
            seen_any_group = True
        elif cat == "mila":
            if cur == 0 or seen_any_group or cur >= 1000:
                return None
            total += cur * 1000
            cur = 0
            seen_any_group = True
        prev_cat = "tens" if cat == "stem" else ("hundred" if cat == "hstem" else cat)
        if cat in ("mille", "mila"):
            prev_cat = ""
    return total + cur


def _parse_int_part(tokens: list[str]) -> Optional[int]:
    if len(tokens) == 1 and re.fullmatch(r"\d+", tokens[0]):
        return int(tokens[0])
    return parse_integer_words(tokens)


def _parse_fraction(tokens: list[str]) -> Optional[str]:
    """Cifre decimali: "cinque" -> "5", "zero cinque" -> "05", "venticinque" -> "25", "due cinque" -> "25"."""
    if not tokens:
        return None
    if len(tokens) == 1 and re.fullmatch(r"\d+", tokens[0]):
        return tokens[0]
    single = [str(_UNITS[t]) if t in _UNITS and t != "un" else None for t in tokens]
    if all(d is not None for d in single):
        return "".join(single)  # cifra per cifra
    zeros = 0
    while zeros < len(tokens) and tokens[zeros] == "zero":
        zeros += 1
    rest = tokens[zeros:]
    n = parse_integer_words(rest) if rest else None
    if n is None:
        return None
    return "0" * zeros + str(n)


def parse_quantity(text: str, max_abs: float = MAX_ABS) -> Quantity:
    """Interpreta "meno dodici virgola cinque millimetri" -> Quantity(-12.5, "mm")."""
    s = strip_accents(text.lower())
    s = re.sub(r"(\d),(\d)", r"\1 virgola \2", s)   # 12,5 -> 12 virgola 5
    s = re.sub(r"(\d)\.(\d)", r"\1 virgola \2", s)  # 12.5 -> 12 virgola 5
    s = re.sub(r"(^|\s)-\s*(?=\d)", r"\1meno ", s)  # -12 -> meno 12
    s = s.replace("°", " gradi ")
    s = re.sub(r"(\d)(mm|cm|m)\b", r"\1 \2", s)
    s = re.sub(r"[^a-z0-9\s]", " ", s)
    tokens = s.split()
    if not tokens:
        return _fail("empty")

    sign = 1
    if tokens[0] in _MINUS:
        sign, tokens = -1, tokens[1:]
    elif tokens[0] in _PLUS:
        tokens = tokens[1:]

    unit: Optional[str] = None
    if tokens and tokens[-1] in _UNIT_WORDS:
        unit, tokens = _UNIT_WORDS[tokens[-1]], tokens[:-1]
    elif tokens and tokens[-1] in _AMBIGUOUS_UNITS:
        return _fail("ambiguous_unit")
    if not tokens:
        return _fail("no_number")

    if tokens.count("virgola") > 1:
        return _fail("multiple_decimal_separators")
    if "virgola" in tokens:
        i = tokens.index("virgola")
        int_tokens, frac_tokens = tokens[:i], tokens[i + 1:]
    else:
        int_tokens, frac_tokens = tokens, []

    if not int_tokens:
        return _fail("no_integer_part")
    int_val = _parse_int_part(int_tokens)
    if int_val is None:
        return _fail("not_a_number")
    value = float(int_val)
    if "virgola" in tokens:
        frac = _parse_fraction(frac_tokens)
        if frac is None:
            return _fail("bad_fraction")
        value += float("0." + frac)
    value *= sign
    if not math.isfinite(value) or abs(value) > max_abs:
        return _fail("out_of_range")
    return Quantity(True, value=value, unit=unit)


def integer_to_words(n: int) -> str:
    """0..999999 -> parole italiane (usata dalla normalizzazione WER delle cifre)."""
    if n < 0 or n > 999_999:
        raise ValueError("fuori intervallo")
    if n == 0:
        return "zero"
    if n >= 1000:
        th, rest = divmod(n, 1000)
        head = "mille" if th == 1 else integer_to_words(th) + "mila"
        return head + (integer_to_words(rest) if rest else "")
    parts = ""
    h, r = divmod(n, 100)
    if h:
        parts += ("" if h == 1 else _name(h)) + "cento"
    if r:
        if h and r >= 80 and r < 90:
            parts = parts[:-1]  # centottanta
        parts += _under_100(r, elide=bool(h) is False)
    return parts


def _name(n: int) -> str:
    return next(k for k, v in {**{k: v for k, v in _UNITS.items() if k != "un"}}.items() if v == n)


def _under_100(n: int, elide: bool = True) -> str:
    if n < 10:
        return _name(n)
    if n < 20:
        return next(k for k, v in _TEENS.items() if v == n)
    tens, unit = divmod(n, 10)
    base = next(k for k, v in _TENS.items() if v == tens * 10)
    if unit == 0:
        return base
    if unit in (1, 8):
        base = base[:-1]
    return base + _name(unit)
