"""Own temporary assembly only; test persistent move/clearance over MCP."""
import argparse
import json
from pathlib import Path
import runpy
import win32com.client

Mcp = runpy.run_path(str(Path(__file__).with_name('smoke-atomic-mcp.py')))['Mcp']

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    args = parser.parse_args()
    fixture = (Path(__file__).resolve().parent.parent / 'artifacts/live-fixtures/f8000325f30b4823bb3ec05b97f8ab97/reference-roundtrip.ipt').resolve()
    assert fixture.is_file()
    app = win32com.client.GetActiveObject('Inventor.Application')
    previous = app.ActiveDocument
    original = {d.InternalName for d in app.Documents}
    assembly = None
    client = Mcp(args.server)
    try:
        client.initialize()
        reader = Mcp(args.server, readonly=True)
        try:
            reader.initialize()
            assert 'inventor_move_component_safe' not in {t['name'] for t in reader.send('tools/list', {})['tools']}
            assert 'inventor_edit_constraint_safe' not in {t['name'] for t in reader.send('tools/list', {})['tools']}
        finally:
            reader.close()
        assembly = app.Documents.Add(12291, app.FileManager.GetTemplateFile(12291), True)
        occurrences = assembly.ComponentDefinition.Occurrences
        a = occurrences.Add(str(fixture), app.TransientGeometry.CreateMatrix())
        transform = app.TransientGeometry.CreateMatrix()
        transform.SetTranslation(app.TransientGeometry.CreateVector(5,0,0))
        b = occurrences.Add(str(fixture), transform)
        a.Grounded = True
        b.Grounded = False
        assembly.SelectSet.Select(b)
        component = client.tool('inventor_get_selection')['selection'][0]['id']
        def move(dx, preview, stale=False, angle=None):
            resource = client.send('resources/read', dict(uri='inventor://active-document'))
            state = json.loads(resource['contents'][0]['text'])
            assert state['id'] == 'doc_' + assembly.InternalName
            rotation = {} if angle is None else dict(rotation_axis=[0,1,0], rotation_center_mm=[30,0,0], rotation_degrees=angle)
            return client.tool('inventor_move_component_safe', document_id=state['id'],
                expected_revision='stale' if stale else state['revision'], component_id=component,
                translation_mm=[dx,0,0], minimum_clearance_mm=5, preview=preview, **rotation)
        stale = move(-20, False, True)
        assert stale.get('ok') is False and 'STALE_REVISION' in stale['error']['message'], stale
        preview = move(-20, True)
        assert preview.get('status') == 'preview_rolled_back', preview
        assert abs(b.Transformation.Translation.X - 5) < 1e-7
        for dx, expected in ((-26,'CLEARANCE_FAILED'),(-40,'INTERFERENCE')):
            rejected = move(dx, False)
            assert rejected.get('ok') is False and expected in rejected['error']['message'], rejected
            assert 'ROLLED_BACK' in rejected['error']['message']
            assert abs(b.Transformation.Translation.X - 5) < 1e-7
        committed = move(-20, False)
        assert committed.get('status') == 'committed', committed
        assert abs(b.Transformation.Translation.X - 3) < 1e-7
        assert abs(committed['checks_at_proposed_position'][0]['distance_mm'] - 10) < 1e-5
        print('Assembly MCP passed: stale rejection, preview, collision/clearance rollback, 20 mm commit with 10 mm clearance', flush=True)
        rotation_preview = move(0, True, angle=90)
        assert rotation_preview.get('status') == 'preview_rolled_back', rotation_preview
        assert abs(b.Transformation.Cell(1,1)-1)<1e-7
        rejected_rotation = move(0, False, angle=-90)
        assert rejected_rotation.get('ok') is False and 'ROLLED_BACK' in rejected_rotation['error']['message'], rejected_rotation
        assert abs(b.Transformation.Cell(1,1)-1)<1e-7
        rotation_commit = move(0, False, angle=90)
        assert rotation_commit.get('status') == 'committed', rotation_commit
        assert abs(b.Transformation.Cell(1,3)-1)<1e-7 and abs(b.Transformation.Translation.X-3)<1e-7
        print('Assembly MCP rotation passed: preview, unsafe endpoint rollback, +90-degree commit', flush=True)
        b.Transformation = transform
        constraints = assembly.ComponentDefinition.Constraints
        for plane in range(1,4):
            pa = a.CreateGeometryProxy(a.Definition.WorkPlanes.Item(plane))
            pb = b.CreateGeometryProxy(b.Definition.WorkPlanes.Item(plane))
            constraints.AddFlushConstraint(pa, pb, 5.0 if plane == 1 else 0.0)
        assembly.Update2()
        start_x = b.Transformation.Translation.X
        listed = client.tool('inventor_list_constraints')['constraints']
        constraint = next(c for c in listed if c['type'] == 'flush' and abs(c['value']-50)<1e-6)
        assert constraint['id'].startswith('ent_') and constraint['units'] == 'mm', constraint
        def edit(value, preview, units='mm', stale=False):
            state = json.loads(client.send('resources/read', dict(uri='inventor://active-document'))['contents'][0]['text'])
            return client.tool('inventor_edit_constraint_safe', document_id=state['id'], expected_revision='stale' if stale else state['revision'],
                constraint_id=constraint['id'], value=value, units=units, minimum_clearance_mm=5, preview=preview)
        result = edit(30, False, stale=True)
        assert result.get('ok') is False and 'STALE_REVISION' in result['error']['message'], result
        result = edit(30, False, units='deg')
        assert result.get('ok') is False and 'units=mm' in result['error']['message'], result
        assert abs(b.Transformation.Translation.X-start_x)<1e-7
        result = edit(30, True)
        assert result.get('status') == 'preview_rolled_back', result
        assert abs(b.Transformation.Translation.X-start_x)<1e-7
        result = edit(24, False)
        assert result.get('ok') is False and 'CLEARANCE_FAILED' in result['error']['message'] and 'ROLLED_BACK' in result['error']['message'], result
        assert abs(b.Transformation.Translation.X-start_x)<1e-7
        result = edit(30, False)
        assert result.get('status') == 'committed', result
        assert abs(abs(b.Transformation.Translation.X)-3)<1e-7
        print('Constraint MCP passed: persistent ID, preview restored, clearance rollback, 30 mm commit', flush=True)
    finally:
        client.close()
        if assembly:
            assembly.Close(True)
        for document in list(app.Documents):
            if document.InternalName not in original and document.FullFileName and Path(document.FullFileName).resolve() == fixture:
                document.Close(True)
        if previous:
            previous.Activate()
    assert {d.InternalName for d in app.Documents} == original
    print('Own assembly/reference closed; original document set restored', flush=True)

if __name__ == '__main__':
    main()
