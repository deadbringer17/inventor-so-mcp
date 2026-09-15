"""Verify the two claims a passing smoke test still cannot see:
   1. first vs third angle actually place the plan view on opposite sides of the front view;
   2. a preview=true call leaves the machine's drawing standard style untouched.
Opens only the shared fixture, saves nothing, closes everything it opened."""
import hashlib
import json
from pathlib import Path
import runpy
import sys
import win32com.client

Mcp = runpy.run_path(str(Path(__file__).with_name('smoke-atomic-mcp.py')))['Mcp']
SERVER = sys.argv[1]
REPO = Path(__file__).resolve().parent.parent
fixture = (REPO / 'artifacts/live-fixtures/f8000325f30b4823bb3ec05b97f8ab97/reference-roundtrip.ipt').resolve()
digest = hashlib.sha256(fixture.read_bytes()).hexdigest()

app = win32com.client.GetActiveObject('Inventor.Application')
original = {d.InternalName for d in app.Documents}
assert not original, 'Run this with no documents open.'

client = Mcp(SERVER)
part = None
opened = []
findings = {}
try:
    client.initialize()
    part = app.Documents.Open(str(fixture), True)
    source = (part.Dirty, part.ComponentDefinition.ModelGeometryVersion)

    def state():
        return json.loads(client.send('resources/read', dict(uri='inventor://active-document'))['contents'][0]['text'])

    # --- 2. preview must not touch the machine's drawing standard -------------------------
    # Capture every standard style's projection flag before and after a preview call.
    DRAWING = 12292  # DocumentTypeEnum.kDrawingDocumentObject

    def standard_flags():
        doc = app.Documents.Add(DRAWING, app.FileManager.GetTemplateFile(DRAWING), True)
        opened.append(doc)
        try:
            drawing = doc
            flags = {st.Name: (st.FirstAngleProjection, int(st.StyleLocation))
                     for st in drawing.StylesManager.StandardStyles}
        finally:
            doc.Close(True)
            opened.remove(doc)
        return flags

    before = standard_flags()

    part.Activate()
    s = state()
    preview = client.tool('inventor_create_drawing_safe', document_id=s['id'], expected_revision=s['revision'],
                          projection='third', preview=True)
    assert preview.get('status') == 'preview_rolled_back', preview

    after = standard_flags()
    findings['standards_before'] = before
    findings['standards_after'] = after
    findings['preview_left_standards_unchanged'] = (before == after)

    # --- 1. first vs third angle place the plan view on opposite sides --------------------
    def geometry(projection):
        part.Activate()
        st = state()
        made = client.tool('inventor_create_drawing_safe', document_id=st['id'], expected_revision=st['revision'],
                           projection=projection, views='front,top', scale=2, preview=False)
        assert made.get('status') == 'created', made
        drawing = app.ActiveDocument
        opened.append(drawing)
        sheet = drawing.ActiveSheet
        views = list(sheet.DrawingViews)
        assert len(views) == 2, [v.Name for v in views]
        # kProjectedDrawingViewType identifies the plan view; the front view is the base it hangs off.
        child = next(v for v in views if v.ParentView is not None)
        base = next(v for v in views if v.ParentView is None)
        out = dict(projection=made['projection'],
                   first_angle_flag=drawing.StylesManager.ActiveStandardStyle.FirstAngleProjection,
                   front_y=round(base.Position.Y, 4), plan_y=round(child.Position.Y, 4))
        out['plan_below_front'] = out['plan_y'] < out['front_y']
        drawing.Close(True)
        opened.remove(drawing)
        return out

    findings['first'] = geometry('first')
    findings['third'] = geometry('third')
    findings['conventions_are_opposite'] = (
        findings['first']['plan_below_front'] != findings['third']['plan_below_front'])
    assert (part.Dirty, part.ComponentDefinition.ModelGeometryVersion) == source, 'source changed'
finally:
    client.close()
    for d in list(opened):
        d.Close(True)
    if part:
        part.Close(True)
    assert hashlib.sha256(fixture.read_bytes()).hexdigest() == digest, 'FIXTURE MODIFIED'
    assert {d.InternalName for d in app.Documents} == original

print(json.dumps(findings, indent=2))
