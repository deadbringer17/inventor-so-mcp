"""Motore finto SOLO per smoke test della pipeline (non e un STT).

Restituisce il testo di riferimento (suggerito da bench.py) degradato in modo
deterministico, cosi le metriche non sono banali. Non usarlo per decidere nulla.
"""
from __future__ import annotations

import time
import zlib

from .base import Engine, EngineSpec


class MockEngine(Engine):
    hint: str = ""

    def _load(self) -> None:
        pass

    def transcribe(self, audio, sample_rate=16000) -> str:
        time.sleep(0.002)
        words = self.hint.split()
        h = zlib.crc32(self.hint.encode()) % 10
        if h == 0 and len(words) > 1:
            words = words[:-1]          # parola persa
        elif h == 1:
            words = ["boh"]             # testo inutile
        return " ".join(words)


def make(spec: EngineSpec) -> Engine:
    return MockEngine(spec)
