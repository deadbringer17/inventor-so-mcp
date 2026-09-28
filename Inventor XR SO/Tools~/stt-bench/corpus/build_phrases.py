"""Rigenera phrases.json (fonte unica leggibile). Uso: python corpus/build_phrases.py"""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from router import VOCABULARY  # noqa: E402

entries = []


def add(prefix, kind, text, expected, note=""):
    entries.append({"id": f"{prefix}{sum(e['id'].startswith(prefix) for e in entries) + 1:02d}",
                    "kind": kind, "text": text, "expected": expected, "note": note})


for cid, aliases in VOCABULARY.items():
    short = cid.split(".")[-1]
    for a in aliases[:3]:  # il 4o alias resta solo nel router
        add(f"cmd-{short}-", "command", a, {"command_id": cid})

NUM = [
    ("zero", 0, None), ("cinque millimetri", 5, "mm"), ("dodici millimetri", 12, "mm"),
    ("ventuno millimetri", 21, "mm"), ("ventitre millimetri", 23, "mm"),
    ("quarantacinque gradi", 45, "deg"), ("novanta gradi", 90, "deg"),
    ("centoventi", 120, None), ("centoventi millimetri", 120, "mm"),
    ("centottanta gradi", 180, "deg"), ("duecentocinquanta millimetri", 250, "mm"),
    ("mille millimetri", 1000, "mm"), ("mille e cinquanta millimetri", 1050, "mm"),
    ("milleduecento millimetri", 1200, "mm"), ("duemilatrecento millimetri", 2300, "mm"),
    ("dodici virgola cinque millimetri", 12.5, "mm"),
    ("zero virgola cinque millimetri", 0.5, "mm"),
    ("zero virgola cinque", 0.5, None),
    ("zero virgola zero cinque millimetri", 0.05, "mm"),
    ("uno virgola due millimetri", 1.2, "mm"),
    ("tre virgola cinque gradi", 3.5, "deg"),
    ("quarantacinque virgola cinque gradi", 45.5, "deg"),
    ("cento virgola due cinque millimetri", 100.25, "mm"),
    ("dieci virgola otto millimetri", 10.8, "mm"),
    ("meno cinque millimetri", -5, "mm"), ("meno dodici virgola cinque millimetri", -12.5, "mm"),
    ("meno quindici gradi", -15, "deg"), ("meno zero virgola cinque gradi", -0.5, "deg"),
    ("meno cento millimetri", -100, "mm"),
    ("piu dieci millimetri", 10, "mm"),
    ("diciotto millimetri", 18, "mm"), ("sessantasette virgola tre millimetri", 67.3, "mm"),
]
for text, value, unit in NUM:
    add("num-", "numeric", text, {"value": value, "unit": unit})

REJECT = [
    ("fai una flangia e uno smusso", "due comandi"),
    ("annulla e applica", "due comandi"),
    ("crea", "incompleto"),
    ("sviluppo", "incompleto, non e un alias"),
    ("cancella tutto", "distruttivo, fuori vocabolario"),
    ("elimina il modello", "distruttivo, fuori vocabolario"),
    ("salva il documento", "fuori vocabolario"),
    ("esporta in step", "fuori vocabolario"),
    ("ruota la vista", "fuori vocabolario"),
    ("che ore sono", "testo libero"),
    ("va bene grazie", "conversazione"),
    ("scusa un attimo", "conversazione"),
    ("dodici centimetri", "unita ambigua"),
    ("dodici metri", "unita ambigua"),
    ("dodici virgola millimetri", "decimale incompleto"),
    ("virgola cinque millimetri", "manca parte intera"),
    ("cinque venti millimetri", "numero malformato"),
    ("dodici virgola cinque virgola tre", "doppia virgola"),
    ("un milione di millimetri", "fuori range"),
    ("cento cento gradi", "numero malformato"),
]
for text, note in REJECT:
    add("rej-", "reject", text, None, note)

out = pathlib.Path(__file__).with_name("phrases.json")
out.write_text(json.dumps({"version": 1, "language": "it", "entries": entries},
                          ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
print(len(entries), "voci scritte in", out)
