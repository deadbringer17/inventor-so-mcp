"""Registro dei motori. Ogni famiglia espone make(spec) -> Engine."""
from __future__ import annotations

from . import fwhisper, mock, vosk_engine, whispercpp
from .base import Engine, EngineSpec, EngineUnavailable

FAMILIES = {
    "faster-whisper": fwhisper.make,
    "vosk": vosk_engine.make,
    "vosk-grammar": vosk_engine.make,
    "whispercpp": whispercpp.make,
    "mock": mock.make,  # solo smoke test
}


def create(spec: EngineSpec) -> Engine:
    return FAMILIES[spec.family](spec)


def parse_engine_arg(arg: str, vosk_model: str | None, download_root: str | None = None) -> EngineSpec:
    """Sintassi: famiglia[:opzioni]
      faster-whisper:small:int8:cpu   (modello:compute_type:device)
      vosk | vosk-grammar             (usano --vosk-model)
      whispercpp:small
    """
    parts = arg.split(":")
    fam = parts[0]
    if fam not in FAMILIES:
        raise ValueError(f"motore sconosciuto: {fam} (validi: {', '.join(FAMILIES)})")
    if fam == "faster-whisper":
        model = parts[1] if len(parts) > 1 else "small"
        ct = parts[2] if len(parts) > 2 else "int8"
        dev = parts[3] if len(parts) > 3 else "cpu"
        return EngineSpec(f"faster-whisper:{model}-{ct}-{dev}", fam,
                          {"model": model, "compute_type": ct, "device": dev, "download_root": download_root})
    if fam == "whispercpp":
        model = parts[1] if len(parts) > 1 else "small"
        return EngineSpec(f"whispercpp:{model}", fam, {"model": model})
    if fam == "mock":
        return EngineSpec("mock", fam, {})
    return EngineSpec(fam, fam, {"model_path": vosk_model})


__all__ = ["Engine", "EngineSpec", "EngineUnavailable", "create", "parse_engine_arg", "FAMILIES"]
