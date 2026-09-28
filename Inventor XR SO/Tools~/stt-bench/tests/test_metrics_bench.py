import bench
from metrics import error_counts, normalize_for_wer, percentile


def test_normalize_digits():
    assert normalize_for_wer("Meno 12,5 mm") == "meno dodici virgola cinque millimetri"
    assert normalize_for_wer("120 gradi.") == "centoventi gradi"


def test_wer_zero_for_equivalent_forms():
    assert error_counts("dodici virgola cinque millimetri", "12,5 mm")[0] == 0


def test_percentile():
    assert percentile([1, 2, 3, 4, 5], 50) == 3
    assert percentile([10], 95) == 10


def test_judge():
    cmd = {"kind": "command", "expected": {"command_id": "feature.fillet"}}
    assert bench.judge(cmd, "Raccordo.")["correct"]
    assert bench.judge(cmd, "smusso")["error"] == "wrong_command"
    assert bench.judge(cmd, "racord")["error"] == "missed"
    num = {"kind": "numeric", "expected": {"value": 12.5, "unit": "mm"}}
    assert bench.judge(num, "12,5 mm")["correct"]
    assert bench.judge(num, "dodici virgola sei millimetri")["error"] == "wrong_value"
    rej = {"kind": "reject", "expected": None}
    assert bench.judge(rej, "cancella tutto")["correct"]
    assert bench.judge(rej, "flangia")["error"] == "false_accept_command"
