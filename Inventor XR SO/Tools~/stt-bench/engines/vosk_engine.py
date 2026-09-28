"""Vosk (Kaldi). pip install vosk; modello italiano da https://alphacephei.com/vosk/models"""
from __future__ import annotations

import json
import os

import numpy as np

from .base import Engine, EngineSpec, EngineUnavailable


class VoskEngine(Engine):
    def _load(self) -> None:
        try:
            import vosk
        except ImportError as e:
            raise EngineUnavailable("vosk non installato") from e
        path = self.spec.options.get("model_path")
        if not path or not os.path.isdir(path):
            raise EngineUnavailable(f"modello Vosk non trovato: {path!r}")
        vosk.SetLogLevel(-1)
        self._vosk = vosk
        self.model = vosk.Model(path)

    def _recognizer(self, sample_rate: int):
        if self.spec.family == "vosk-grammar":
            from router import grammar_phrases
            return self._vosk.KaldiRecognizer(self.model, sample_rate, json.dumps(grammar_phrases()))
        return self._vosk.KaldiRecognizer(self.model, sample_rate)

    def transcribe(self, audio, sample_rate=16000) -> str:
        rec = self._recognizer(sample_rate)
        pcm = (np.clip(audio, -1, 1) * 32767).astype(np.int16).tobytes()
        for i in range(0, len(pcm), 8000):
            rec.AcceptWaveform(pcm[i:i + 8000])
        text = json.loads(rec.FinalResult()).get("text", "")
        return " ".join(w for w in text.split() if w != "[unk]")


def make(spec: EngineSpec) -> Engine:
    return VoskEngine(spec)
