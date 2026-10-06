"""Audit the explicit M10 icon mapping against fixed action declarations; no source mutation."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
XR = ROOT / "Inventor XR SO/Assets/XrSo/Xr"
CATALOG = json.loads((ROOT / "assets/inventor-icons/m10-catalog.json").read_text(encoding="utf-8"))
mapped = {action: icon for icon in CATALOG["icons"] for action in icon["actions"]}

def arguments(source, start):
    stack, quoted, escape, begin, result = [")"], False, False, start, []
    for index in range(start, len(source)):
        char = source[index]
        if quoted:
            if escape:
                escape = False
            elif char == "\\":
                escape = True
            elif char == '"':
                quoted = False
            continue
        if char == '"':
            quoted = True
        elif char in "([{":
            stack.append({"(": ")", "[": "]", "{": "}"}[char])
        elif char in ")]}":
            assert stack.pop() == char
            if not stack:
                return result + [source[begin:index].strip()]
        elif char == "," and len(stack) == 1:
            result.append(source[begin:index].strip())
            begin = index + 1
    raise ValueError("Unterminated constructor")

rows, seen = [], set()
for filename in ["DesignActions", "LamieraActions", "AssemblyActions", "InspectActions", "DocumentActions", "ViewActions", "DesignFeatureEdit", "LamieraFeatureEdit"]:
    source = (XR / (filename + ".cs")).read_text(encoding="utf-8")
    constants = dict(re.findall(r'\b(\w+)\s*=\s*"([^"]*)"', source))
    for match in re.finditer(r"new XrAction\(", source):
        args = arguments(source, match.end())
        token = args[0]
        if token.startswith('"') and re.fullmatch(r'"[^"]+"', token):
            action = token[1:-1]
        elif token in constants:
            action = constants[token]
        elif token.startswith("CommitIds."):
            action = token
        else:
            continue  # Dynamic picker/parameter/feature values are summarized below.
        seen.add(action)
        icon = mapped.get(action)
        if icon:
            assert any('icon:' in arg and '"' + icon["key"] + '"' in arg for arg in args), action
            disposition = "icona " + icon["key"]
            if "conditions" in icon and action in icon["conditions"]:
                disposition += "; testo quando contiene un nome CAD"
        elif token.startswith("CommitIds."):
            disposition = "testo: conferma CAD esplicita"
        elif any("XrActionKind.Numeric" in arg for arg in args) or not re.fullmatch(r'"[^"]*"', args[1]):
            disposition = "testo: valore/stato corrente o nome CAD"
        else:
            disposition = "testo: comando XR o corrispondenza nativa non verificata"
        label = args[1].replace("|", "\\|").replace("\n", " ")
        rows.append(f"| {filename} | `{action}` | `{label}` | {disposition} |")
# Shape() declares these three actions using its explicit enum-to-key mapping.
for action in ["design.shape.line", "design.shape.rectangle", "design.shape.circle"]:
    seen.add(action)
    icon = mapped[action]
    rows.append(f"| DesignActions.Shape | `{action}` | {icon['label']} | icona {icon['key']} |")
assert set(mapped) <= seen, "Mapping IDs missing from source: " + str(set(mapped) - seen)
header = """# M10 — inventario dei comandi

Generato con `python scripts/inventory-m10-actions.py`. Mapping esplicito:
`assets/inventor-icons/m10-catalog.json`: **34 risorse, 46 azioni**.
Le righe testuali sono decisioni di copertura, non pulsanti senza asset.
Picker, parametri e campi di modifica feature generati a runtime conservano
sempre nomi, numeri e unità; tastierino e barra di conferma restano testuali.
`m10-pilot.json` conserva la selezione iniziale di 11 azioni come riferimento.

| Sorgente | Azione | Etichetta / espressione | Presentazione |
|---|---|---|---|
"""
(ROOT / "docs/xr-m10-action-inventory.md").write_text(header + "\n".join(rows) + "\n", encoding="utf-8")
print(f"{len(rows)} fixed declarations audited; all {len(mapped)} mapped action IDs found")
