#!/usr/bin/env python3
"""Benchmark STT locale sul corpus M5. Vedi README.md.

  python bench.py --recordings D:/stt-recordings \
      --engines faster-whisper:small:int8:cpu faster-whisper:medium:int8:cpu vosk vosk-grammar \
      --vosk-model D:/models/vosk-model-it-0.22
  python bench.py --smoke        # pipeline end-to-end senza modelli ne audio reale
"""
from __future__ import annotations

import argparse
import gc
import json
import os
import platform
import subprocess
import sys
import tempfile
import time
import wave
from datetime import datetime
from pathlib import Path

import numpy as np

import engines as eng
from engines.base import EngineUnavailable
from metrics import error_counts, percentile
from numbers_it import parse_quantity
from router import route

HERE = Path(__file__).resolve().parent
SAMPLE_RATE = 16000


# ---------- audio ----------
def read_wav(path: Path) -> np.ndarray:
    with wave.open(str(path), "rb") as w:
        sr, ch, sw = w.getframerate(), w.getnchannels(), w.getsampwidth()
        raw = w.readframes(w.getnframes())
    if sw != 2:
        raise ValueError(f"{path.name}: serve PCM 16 bit")
    a = np.frombuffer(raw, dtype=np.int16).astype(np.float32) / 32768.0
    if ch > 1:
        a = a.reshape(-1, ch).mean(axis=1)
    if sr != SAMPLE_RATE:  # ricampionamento lineare, sufficiente per il confronto
        n = int(len(a) * SAMPLE_RATE / sr)
        a = np.interp(np.linspace(0, len(a) - 1, n), np.arange(len(a)), a).astype(np.float32)
    return a


def make_smoke_recordings(root: Path, entries: list[dict]) -> None:
    """WAV sintetici (tono a bassa ampiezza + silenzio): niente audio reale."""
    d = root / "smoke"
    d.mkdir(parents=True, exist_ok=True)
    for i, e in enumerate(entries):
        n = int(SAMPLE_RATE * (0.8 + 0.05 * (i % 10)))
        t = np.arange(n) / SAMPLE_RATE
        pcm = (0.05 * np.sin(2 * np.pi * (200 + 10 * i) * t) * 32767).astype(np.int16)
        with wave.open(str(d / f"{e['id']}.wav"), "wb") as w:
            w.setnchannels(1)
            w.setsampwidth(2)
            w.setframerate(SAMPLE_RATE)
            w.writeframes(pcm.tobytes())


# ---------- risorse ----------
class ResourceMonitor:
    """RSS del processo (psutil) e VRAM (nvidia-smi) se disponibili."""

    def __init__(self):
        try:
            import psutil
            self.proc = psutil.Process()
        except ImportError:
            self.proc = None
        self.gpu_ok = self._gpu_used() is not None
        self.rss_before = self.rss()
        self.gpu_before = self._gpu_used()
        self.rss_peak = self.rss_before
        self.gpu_peak = self.gpu_before

    def rss(self):
        if not self.proc:
            return None
        mi = self.proc.memory_info()
        return max(mi.rss, getattr(mi, "peak_wset", 0)) / 2**20  # MiB

    @staticmethod
    def _gpu_used():
        try:
            out = subprocess.run(["nvidia-smi", "--query-gpu=memory.used", "--format=csv,noheader,nounits"],
                                 capture_output=True, text=True, timeout=5)
            if out.returncode != 0:
                return None
            return float(out.stdout.strip().splitlines()[0])
        except Exception:
            return None

    def sample(self):
        r = self.rss()
        if r is not None and self.rss_peak is not None:
            self.rss_peak = max(self.rss_peak, r)
        if self.gpu_ok:
            g = self._gpu_used()
            if g is not None and self.gpu_peak is not None:
                self.gpu_peak = max(self.gpu_peak, g)

    def report(self) -> dict:
        def d(a, b):
            return None if a is None or b is None else round(a - b, 1)
        return {"rss_peak_mib": None if self.rss_peak is None else round(self.rss_peak, 1),
                "rss_delta_mib": d(self.rss_peak, self.rss_before),
                "vram_peak_mib": self.gpu_peak, "vram_delta_mib": d(self.gpu_peak, self.gpu_before)}


# ---------- valutazione intento ----------
def judge(entry: dict, text: str) -> dict:
    """Esito di un item: correct, oltre a categorie di errore 'pericolose'."""
    kind, exp = entry["kind"], entry["expected"]
    routed = route(text).command_id
    q = parse_quantity(text)
    out = {"routed": routed, "quantity": {"ok": q.ok, "value": q.value, "unit": q.unit},
           "correct": False, "error": None}
    if kind == "command":
        out["correct"] = routed == exp["command_id"]
        if not out["correct"]:
            out["error"] = "wrong_command" if routed else "missed"
    elif kind == "numeric":
        if routed:
            out["error"] = "wrong_command"          # un numero ha attivato un comando
        elif q.ok and q.value is not None and abs(q.value - exp["value"]) < 1e-9 and q.unit == exp["unit"]:
            out["correct"] = True
        else:
            out["error"] = "wrong_value" if q.ok else "missed"
    else:  # reject
        if routed:
            out["error"] = "false_accept_command"
        elif q.ok:
            out["error"] = "false_accept_number"
        else:
            out["correct"] = True
    return out


UNSAFE = {"wrong_command", "wrong_value", "false_accept_command", "false_accept_number"}


def summarize(items: list[dict]) -> dict:
    lat = [i["latency_ms"] for i in items]
    rtf = [i["rtf"] for i in items]
    we = sum(i["word_err"] for i in items)
    wn = sum(i["word_ref"] for i in items)
    ce = sum(i["char_err"] for i in items)
    cn = sum(i["char_ref"] for i in items)
    by_kind = {}
    for k in ("command", "numeric", "reject"):
        ks = [i for i in items if i["kind"] == k]
        by_kind[k] = {"n": len(ks), "correct": sum(i["correct"] for i in ks)}
    n = len(items)
    return {
        "n": n,
        "intent_accuracy": sum(i["correct"] for i in items) / n if n else None,
        "by_kind": by_kind,
        "unsafe_errors": sum(i["error"] in UNSAFE for i in items),
        "false_accepts_on_reject": sum(i["error"] in ("false_accept_command", "false_accept_number") for i in items),
        "wer": we / wn if wn else None,
        "cer": ce / cn if cn else None,
        "latency_ms": {"p50": percentile(lat, 50), "p95": percentile(lat, 95), "max": max(lat) if lat else None},
        "rtf": {"mean": float(np.mean(rtf)) if rtf else None, "p95": percentile(rtf, 95)},
    }


# ---------- esecuzione ----------
def run_engine(spec, entries, rec_root: Path, conditions: list[str], keep_text: bool, verbose: bool) -> dict:
    res = {"name": spec.name, "family": spec.family, "options": {k: v for k, v in spec.options.items() if k != "download_root"}}
    gc.collect()
    mon = ResourceMonitor()
    try:
        e = eng.create(spec)
        e.load()
    except EngineUnavailable as ex:
        res.update(status="skipped", reason=str(ex))
        return res
    mon.sample()
    res.update(status="ok", load_seconds=round(e.load_seconds, 3), conditions={})

    first = True
    for cond in conditions:
        items = []
        for entry in entries:
            wav = rec_root / cond / f"{entry['id']}.wav"
            if not wav.exists():
                continue
            audio = read_wav(wav)
            if spec.family == "mock":
                e.hint = entry["text"]
            if first:  # riscaldamento non misurato (allocazioni, JIT, cache GPU)
                e.transcribe(audio)
                first = False
            t0 = time.perf_counter()
            text = e.transcribe(audio)          # fine audio -> testo finale
            lat = time.perf_counter() - t0
            mon.sample()
            dur = len(audio) / SAMPLE_RATE
            we, wn, ce, cn = error_counts(entry["text"], text)
            j = judge(entry, text)
            item = {"id": entry["id"], "kind": entry["kind"], "duration_s": round(dur, 3),
                    "latency_ms": lat * 1000, "rtf": lat / dur if dur else 0.0,
                    "word_err": we, "word_ref": wn, "char_err": ce, "char_ref": cn, **j}
            if keep_text:
                item["transcript"] = text
            if verbose:
                print(f"    {entry['id']}: {'OK' if j['correct'] else j['error']}")
            items.append(item)
        if items:
            res["conditions"][cond] = {"summary": summarize(items), "items": items}
    res["resources"] = mon.report()
    e.close()
    del e
    gc.collect()
    return res


def pct(x):
    return "n/d" if x is None else f"{x * 100:.1f}%"


def write_summary(path: Path, results: dict) -> None:
    L = ["# Benchmark STT M5 — riepilogo", "",
         f"Data: {results['meta']['timestamp']}  ·  host: {results['meta']['platform']}  ·  "
         f"CPU logiche: {results['meta']['cpu_count']}  ·  Python {results['meta']['python']}", ""]
    if results["meta"].get("smoke"):
        L += ["> **SMOKE TEST**: audio sintetico e motore finto. I numeri non hanno alcun valore di misura.", ""]
    L += ["> **La scelta del motore e del budget va fatta solo su misure eseguite sulla postazione "
          "Windows reale**, con il microfono reale (Quest o cuffia) e il rumore reale. "
          "Questi numeri, se ottenuti altrove, non sono una decisione.", ""]
    conds = sorted({c for e in results["engines"] if e["status"] == "ok" for c in e["conditions"]})
    for c in conds:
        L += [f"## Condizione: {c}", "",
              "| Motore | N | Intent acc. | Errori pericolosi | Falsi accept OOV | WER | CER | "
              "Lat. p50 ms | p95 ms | max ms | RTF medio | Load s | RSS pk MiB | VRAM pk MiB |",
              "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
        for e in results["engines"]:
            if e["status"] != "ok" or c not in e["conditions"]:
                continue
            s = e["conditions"][c]["summary"]
            r = e["resources"]
            L.append(f"| {e['name']} | {s['n']} | {pct(s['intent_accuracy'])} | {s['unsafe_errors']} | "
                     f"{s['false_accepts_on_reject']} | {pct(s['wer'])} | {pct(s['cer'])} | "
                     f"{s['latency_ms']['p50']:.0f} | {s['latency_ms']['p95']:.0f} | {s['latency_ms']['max']:.0f} | "
                     f"{s['rtf']['mean']:.2f} | {e['load_seconds']:.1f} | "
                     f"{r['rss_peak_mib'] if r['rss_peak_mib'] is not None else 'n/d'} | "
                     f"{r['vram_peak_mib'] if r['vram_peak_mib'] is not None else 'n/d'} |")
        L.append("")
    skipped = [e for e in results["engines"] if e["status"] != "ok"]
    if skipped:
        L += ["## Motori saltati", ""] + [f"- `{e['name']}`: {e['reason']}" for e in skipped] + [""]
    L += ["## Come leggere", "",
          "- **Errori pericolosi** = comando sbagliato, valore numerico sbagliato accettato, o frase fuori "
          "vocabolario accettata. Devono essere zero nel criterio di decisione.",
          "- **Latenza** = fine dell'audio -> testo finale (elaborazione del motore, senza trasporto di rete).",
          "- **RSS/VRAM**: il picco RSS include cio che il processo aveva gia in memoria; per confronti puliti "
          "esegui un solo motore per invocazione e guarda `rss_delta_mib` in `results.json`.",
          "- WER/CER sui numeri e approssimato (cifre convertite in parole); l'accuratezza di intento e la misura guida.",
          ""]
    path.write_text("\n".join(L), encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--recordings", type=Path, help="cartella con sottocartelle per condizione")
    ap.add_argument("--conditions", nargs="*", help="default: tutte le sottocartelle")
    ap.add_argument("--engines", nargs="*", help="es. faster-whisper:small:int8:cpu vosk vosk-grammar whispercpp:small")
    ap.add_argument("--vosk-model", help="cartella del modello Vosk italiano")
    ap.add_argument("--model-dir", help="cartella di cache modelli faster-whisper")
    ap.add_argument("--corpus", type=Path, default=HERE / "corpus" / "phrases.json")
    ap.add_argument("--out", type=Path, default=HERE / "results")
    ap.add_argument("--no-transcripts", action="store_true", help="non salvare le trascrizioni in results.json")
    ap.add_argument("--verbose", action="store_true")
    ap.add_argument("--smoke", action="store_true", help="WAV sintetici + motore finto, senza modelli")
    args = ap.parse_args()

    entries = json.loads(args.corpus.read_text(encoding="utf-8"))["entries"]
    tmp = None
    if args.smoke:
        tmp = tempfile.TemporaryDirectory()
        args.recordings = Path(tmp.name)
        make_smoke_recordings(args.recordings, entries)
        args.engines = args.engines or ["mock", "faster-whisper:small:int8:cpu", "vosk", "vosk-grammar"]
    if not args.recordings or not args.recordings.is_dir():
        ap.error("--recordings mancante o inesistente (oppure usa --smoke)")
    if not args.engines:
        ap.error("--engines obbligatorio")
    conds = args.conditions or sorted(p.name for p in args.recordings.iterdir() if p.is_dir())
    if not conds:
        ap.error("nessuna condizione trovata nella cartella delle registrazioni")

    results = {"meta": {"timestamp": datetime.now().isoformat(timespec="seconds"),
                        "platform": platform.platform(), "python": platform.python_version(),
                        "cpu_count": os.cpu_count(), "smoke": args.smoke, "conditions": conds,
                        "corpus_entries": len(entries),
                        "note": "Misure valide per la decisione solo se eseguite sulla postazione reale."},
               "engines": []}
    for arg in args.engines:
        spec = eng.parse_engine_arg(arg, args.vosk_model, args.model_dir)
        print(f"== {spec.name}")
        r = run_engine(spec, entries, args.recordings, conds, not args.no_transcripts, args.verbose)
        print("   " + (f"saltato: {r['reason']}" if r["status"] == "skipped" else
                       "  ".join(f"{c}: intent {pct(v['summary']['intent_accuracy'])}, "
                                 f"p95 {v['summary']['latency_ms']['p95']:.0f} ms"
                                 for c, v in r["conditions"].items())))
        results["engines"].append(r)

    out = args.out / datetime.now().strftime("%Y%m%d-%H%M%S")
    out.mkdir(parents=True, exist_ok=True)
    (out / "results.json").write_text(json.dumps(results, ensure_ascii=False, indent=1), encoding="utf-8")
    write_summary(out / "summary.md", results)
    print(f"Risultati in {out}")
    if tmp:
        tmp.cleanup()
    return 0


if __name__ == "__main__":
    sys.exit(main())
