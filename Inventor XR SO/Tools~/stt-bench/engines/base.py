"""Interfaccia comune dei motori STT del benchmark."""
from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import Optional

import numpy as np


class EngineUnavailable(Exception):
    """Motore o modello non installato/trovato: il benchmark lo salta."""


@dataclass
class EngineSpec:
    name: str                       # es. "faster-whisper:small-int8-cpu"
    family: str                     # "faster-whisper" | "vosk" | "vosk-grammar" | "whispercpp"
    options: dict = field(default_factory=dict)


class Engine:
    """Un motore caricato. transcribe() riceve float32 mono 16 kHz in [-1, 1]."""

    def __init__(self, spec: EngineSpec):
        self.spec = spec
        self.load_seconds: float = 0.0

    def load(self) -> None:
        t0 = time.perf_counter()
        self._load()
        self.load_seconds = time.perf_counter() - t0

    def _load(self) -> None:  # pragma: no cover - interfaccia
        raise NotImplementedError

    def transcribe(self, audio: np.ndarray, sample_rate: int = 16000) -> str:  # pragma: no cover
        raise NotImplementedError

    def close(self) -> None:
        pass

    def vram_hint(self) -> Optional[str]:
        return None
