"""faster-whisper (CTranslate2). pip install faster-whisper"""
from __future__ import annotations

from .base import Engine, EngineSpec, EngineUnavailable


class FasterWhisperEngine(Engine):
    def _load(self) -> None:
        try:
            from faster_whisper import WhisperModel
        except ImportError as e:
            raise EngineUnavailable("faster-whisper non installato") from e
        o = self.spec.options
        try:
            self.model = WhisperModel(o.get("model", "small"), device=o.get("device", "cpu"),
                                      compute_type=o.get("compute_type", "int8"),
                                      download_root=o.get("download_root"),
                                      local_files_only=o.get("local_files_only", False))
        except Exception as e:  # modello mancante, CUDA assente, ecc.
            raise EngineUnavailable(f"impossibile caricare il modello: {type(e).__name__}") from e

    def transcribe(self, audio, sample_rate=16000) -> str:
        o = self.spec.options
        segments, _ = self.model.transcribe(
            audio, language="it", beam_size=o.get("beam_size", 1), vad_filter=False,
            condition_on_previous_text=False, temperature=0.0,
            initial_prompt=o.get("initial_prompt"))
        return " ".join(s.text.strip() for s in segments).strip()


def make(spec: EngineSpec) -> Engine:
    return FasterWhisperEngine(spec)
