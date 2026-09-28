import math

import pytest

from numbers_it import integer_to_words, parse_integer_words, parse_quantity


@pytest.mark.parametrize("text,value,unit", [
    ("zero", 0, None),
    ("dodici millimetri", 12, "mm"),
    ("ventuno millimetri", 21, "mm"),
    ("ventitre millimetri", 23, "mm"),
    ("ventitré gradi", 23, "deg"),
    ("centoventi", 120, None),
    ("centotto", 108, None),
    ("centottanta gradi", 180, "deg"),
    ("duecentocinquanta", 250, None),
    ("mille", 1000, None),
    ("mille e cinquanta millimetri", 1050, "mm"),
    ("milleduecento", 1200, None),
    ("duemilatrecento", 2300, None),
    ("dodici virgola cinque millimetri", 12.5, "mm"),
    ("zero virgola cinque", 0.5, None),
    ("zero virgola zero cinque", 0.05, None),
    ("cento virgola due cinque millimetri", 100.25, "mm"),
    ("tre virgola venticinque", 3.25, None),
    ("meno cinque millimetri", -5, "mm"),
    ("meno zero virgola cinque gradi", -0.5, "deg"),
    ("più dieci millimetri", 10, "mm"),
    ("Dodici Virgola Cinque Millimetri.", 12.5, "mm"),
    ("12,5 mm", 12.5, "mm"),
    ("12.5 millimetri", 12.5, "mm"),
    ("-3 gradi", -3, "deg"),
    ("45°", 45, "deg"),
    ("meno 12 virgola 5 millimetri", -12.5, "mm"),
])
def test_accepts(text, value, unit):
    q = parse_quantity(text)
    assert q.ok, q
    assert math.isclose(q.value, value, abs_tol=1e-9)
    assert q.unit == unit


@pytest.mark.parametrize("text,reason", [
    ("", "empty"),
    ("millimetri", "no_number"),
    ("dodici centimetri", "ambiguous_unit"),
    ("dodici metri", "ambiguous_unit"),
    ("dodici virgola millimetri", "bad_fraction"),
    ("virgola cinque", "no_integer_part"),
    ("dodici virgola cinque virgola tre", "multiple_decimal_separators"),
    ("cinque venti", "not_a_number"),
    ("cento cento", "not_a_number"),
    ("ventdue", "not_a_number"),
    ("flangia", "not_a_number"),
    ("un milione", "not_a_number"),
    ("meno", "no_number"),
])
def test_rejects(text, reason):
    q = parse_quantity(text)
    assert not q.ok
    assert q.reason == reason


def test_out_of_range():
    assert parse_quantity("novecentonovantanovemila millimetri").reason == "out_of_range"
    assert parse_quantity("dodici", max_abs=10).reason == "out_of_range"


def test_words_roundtrip():
    for n in list(range(0, 1300)) + list(range(1300, 999_999, 997)) + [999_999]:
        assert parse_integer_words([integer_to_words(n)]) == n, n
