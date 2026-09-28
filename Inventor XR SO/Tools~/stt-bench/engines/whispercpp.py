"""whisper.cpp via pywhispercpp (opzionale). pip install pywhispercpp"""
from __future__ import annotations

from .base import Engine, EngineSpec, EngineUnavailable


class WhisperCppEngine(Engine):
    def _load(self) -> None:
        try:
            from pywhispercpp.model import Model
        except ImportError as e:
            raise EngineUnavailable("pywhispercpp non installato") from e
        try:
            self.model = Model(self.spec.options.get("model", "small"), language="it",
                               print_progress=False, print_realtime=False)
        except Exception as e:
            raise EngineUnavailable(f"impossibile caricare il modello: {type(e).__name__}") from e

    def transcribe(self, audio, sample_rate=16000) -> str:
        return " ".join(s.text.strip() for s in self.model.transcribe(audio)).strip()


def make(spec: EngineSpec) -> Engine:
    return WhisperCppEngine(spec)
