#!/usr/bin/env python3
"""Registratore guidato: una frase alla volta, WAV 16 kHz mono in <out>/<condizione>/<id>.wav.

Esempi:
  python record.py --out D:/stt-recordings --condition quiet
  python record.py --out D:/stt-recordings --condition noise --kind numeric --skip-existing
  python record.py --list-devices
Le registrazioni NON vanno nel repository (recordings/ e *.wav sono ignorati).
"""
from __future__ import annotations

import argparse
import json
import queue
import sys
import time
import wave
from pathlib import Path

SAMPLE_RATE = 16000
HERE = Path(__file__).resolve().parent


def load_corpus(path: Path) -> list[dict]:
    return json.loads(path.read_text(encoding="utf-8"))["entries"]


def write_wav(path: Path, samples_int16) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SAMPLE_RATE)
        w.writeframes(samples_int16.tobytes())


def record_one(sd, np, device, mode: str, max_seconds: float):
    q: "queue.Queue" = queue.Queue()

    def cb(indata, frames, t, status):
        q.put(indata.copy())

    with sd.InputStream(samplerate=SAMPLE_RATE, channels=1, dtype="int16", device=device, callback=cb):
        t0 = time.time()
        if mode == "enter":
            input("  [REC] parla, poi premi Invio per fermare... ")
        else:
            time.sleep(max_seconds)
        dur = time.time() - t0
    chunks = []
    while not q.empty():
        chunks.append(q.get())
    audio = np.concatenate(chunks) if chunks else np.zeros((0, 1), dtype="int16")
    return audio[:, 0], dur


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", type=Path, help="cartella radice delle registrazioni (fuori dal repo)")
    ap.add_argument("--condition", default="quiet", help="quiet | office | machine | ... (sottocartella)")
    ap.add_argument("--corpus", type=Path, default=HERE / "corpus" / "phrases.json")
    ap.add_argument("--kind", choices=["command", "numeric", "reject"], help="solo questo tipo di frasi")
    ap.add_argument("--ids", help="elenco di id separati da virgola")
    ap.add_argument("--mode", choices=["enter", "fixed"], default="enter",
                    help="enter: Invio per iniziare e fermare; fixed: durata fissa --max-seconds")
    ap.add_argument("--max-seconds", type=float, default=4.0)
    ap.add_argument("--device", help="indice o nome del dispositivo di ingresso (vedi --list-devices)")
    ap.add_argument("--skip-existing", action="store_true")
    ap.add_argument("--list-devices", action="store_true")
    args = ap.parse_args()

    try:
        import numpy as np
        import sounddevice as sd
    except ImportError:
        print("Servono numpy e sounddevice: pip install -r requirements.txt", file=sys.stderr)
        return 2

    if args.list_devices:
        print(sd.query_devices())
        return 0
    if not args.out:
        ap.error("--out e obbligatorio")
    device = int(args.device) if args.device and args.device.isdigit() else args.device

    entries = load_corpus(args.corpus)
    if args.kind:
        entries = [e for e in entries if e["kind"] == args.kind]
    if args.ids:
        wanted = set(args.ids.split(","))
        entries = [e for e in entries if e["id"] in wanted]

    target = args.out / args.condition
    print(f"Condizione: {args.condition}  ->  {target}\nFrasi: {len(entries)}  (q + Invio al prompt per uscire)\n")
    i = 0
    while i < len(entries):
        e = entries[i]
        path = target / f"{e['id']}.wav"
        if args.skip_existing and path.exists():
            i += 1
            continue
        print(f"[{i + 1}/{len(entries)}] {e['id']}  ({e['kind']})\n  Pronuncia: \"{e['text']}\"")
        if args.mode == "enter":
            cmd = input("  Invio per iniziare (q=esci, s=salta): ").strip().lower()
            if cmd == "q":
                break
            if cmd == "s":
                i += 1
                continue
        audio, dur = record_one(sd, np, device, args.mode, args.max_seconds)
        peak = int(np.abs(audio).max()) if audio.size else 0
        print(f"  durata {dur:.1f}s, picco {peak / 32767:.2f}" + ("  ATTENZIONE: quasi silenzio" if peak < 300 else "")
              + ("  ATTENZIONE: clipping" if peak > 32000 else ""))
        ok = input("  Salvare? [Invio=si, r=ripeti]: ").strip().lower() if args.mode == "enter" else ""
        if ok == "r":
            continue
        write_wav(path, audio)
        i += 1
    print("Fine.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
