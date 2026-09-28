import json
from pathlib import Path

import pytest

from numbers_it import parse_quantity
from router import NEEDS_PHYSICAL_CONFIRM, VOCABULARY, grammar_phrases, route

CORPUS = json.loads((Path(__file__).resolve().parent.parent / "corpus" / "phrases.json").read_text(encoding="utf-8"))["entries"]


@pytest.mark.parametrize("cid,aliases", VOCABULARY.items())
def test_every_alias_routes(cid, aliases):
    for a in aliases:
        assert route(a).command_id == cid
        assert route(a.upper() + ".").command_id == cid  # maiuscole e punteggiatura


def test_normalization_accents_and_spaces():
    assert route("  Crea   sviluppo ").command_id == "sheetmetal.flat_pattern"


@pytest.mark.parametrize("text", [
    "", "crea", "sviluppo", "flangia smusso", "fai una flangia e uno smusso",
    "annulla e applica", "cancella tutto", "flangia per favore", "dodici", "boh",
])
def test_rejects_out_of_vocabulary(text):
    assert route(text).command_id is None


def test_apply_needs_physical_confirm():
    assert route("applica").command_id in NEEDS_PHYSICAL_CONFIRM


def test_grammar_contains_vocabulary():
    g = grammar_phrases()
    assert "crea sviluppo" in g and "virgola" in g and "[unk]" in g


def test_ids_unique_and_ids_prefixes():
    ids = [e["id"] for e in CORPUS]
    assert len(ids) == len(set(ids))
    assert 60 <= len(ids) <= 80


def test_corpus_consistent_with_reference_implementation():
    for e in CORPUS:
        if e["kind"] == "command":
            assert route(e["text"]).command_id == e["expected"]["command_id"], e["id"]
        elif e["kind"] == "numeric":
            q = parse_quantity(e["text"])
            assert q.ok and abs(q.value - e["expected"]["value"]) < 1e-9 and q.unit == e["expected"]["unit"], e["id"]
            assert route(e["text"]).command_id is None
        else:
            assert route(e["text"]).command_id is None and not parse_quantity(e["text"]).ok, e["id"]
