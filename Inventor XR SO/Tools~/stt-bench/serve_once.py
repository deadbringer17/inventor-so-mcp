"""Contratto del motore esterno del gateway (POST /voice/transcribe).

Uso:  python serve_once.py [--engine faster-whisper:small:int8:cpu] [--vosk-model DIR] file.wav

Legge il WAV (PCM 16 bit mono 16 kHz) indicato in argv, stampa SOLO la trascrizione su
stdout e termina con codice 0. Errori su stderr (senza testo riconosciuto), codice != 0.
Nessuna rete: usa i motori locali di engines/. Non interpreta il testo come comando.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import engines as eng  # noqa: E402
from engines.base import EngineUnavailable  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("wav", type=Path)
    ap.add_argument("--engine", default="faster-whisper:small:int8:cpu")
    ap.add_argument("--vosk-model", default=None)
    args = ap.parse_args()
    try:
        from bench import read_wav
        audio = read_wav(args.wav)
        engine = eng.create(eng.parse_engine_arg(args.engine, args.vosk_model))
        engine.load()
        try:
            text = engine.transcribe(audio, 16000)
        finally:
            engine.close()
    except EngineUnavailable as e:
        print(f"engine unavailable: {e}", file=sys.stderr)
        return 3
    except Exception as e:  # never echo audio-derived content
        print(f"failed: {type(e).__name__}", file=sys.stderr)
        return 1
    sys.stdout.write(text.strip())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
