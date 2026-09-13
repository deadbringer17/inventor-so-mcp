"""Diagnostic direct-COM save comparison on tool-owned drafts, unique output paths only."""
from pathlib import Path
import time
import uuid
import win32com.client

app = win32com.client.GetActiveObject('Inventor.Application')
original = {d.InternalName for d in app.Documents}
previous = app.ActiveDocument
fixture = (Path(__file__).resolve().parent.parent / 'artifacts/live-fixtures/f8000325f30b4823bb3ec05b97f8ab97/reference-roundtrip.ipt').resolve()
assert not any(d.FullFileName and Path(d.FullFileName).resolve() == fixture for d in app.Documents)
root = Path(__file__).resolve().parent.parent / 'artifacts/live-fixtures' / uuid.uuid4().hex
root.mkdir()
part = drawing = None
silent = app.SilentOperation
try:
    app.SilentOperation = True
    part = app.Documents.Open(str(fixture), True)
    for api in ('SaveAs', 'SaveAs2'):
        drawing = app.Documents.Add(12292, app.FileManager.GetTemplateFile(12292), True)
        drawing.ActiveSheet.DrawingViews.AddBaseView(part, app.TransientGeometry.CreatePoint2d(10,10), 1, 10764, 32258)
        drawing.Update2()
        output = root / (api + '.idw')
        print('Starting ' + api + ' -> ' + str(output), flush=True)
        started = time.monotonic()
        if api == 'SaveAs':
            drawing.SaveAs(str(output), True)
        else:
            options = app.TransientObjects.CreateNameValueMap()
            options.Add('SaveDependents', False)
            drawing.SaveAs2(str(output), True, options)
        print(api + ' completed in ' + str(time.monotonic()-started) + ' seconds; bytes=' + str(output.stat().st_size), flush=True)
        drawing.Close(True)
        drawing = None
finally:
    if drawing:
        drawing.Close(True)
    if part:
        part.Close(True)
    app.SilentOperation = silent
    if previous:
        previous.Activate()
assert {d.InternalName for d in app.Documents} == original
