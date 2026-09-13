"""Create constraints through MCP on disposable assembly fixtures, never save source parts."""
import argparse
import hashlib
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
    before_hash = hashlib.sha256(fixture.read_bytes()).hexdigest()
    app = win32com.client.GetActiveObject('Inventor.Application')
    previous = app.ActiveDocument
    original = {d.InternalName for d in app.Documents}
    client = Mcp(args.server)
    try:
        client.initialize()
        reader = Mcp(args.server, readonly=True)
        try:
            reader.initialize()
            assert 'inventor_create_constraint_safe' not in {t['name'] for t in reader.send('tools/list', {})['tools']}
            assert 'inventor_insert_component_safe' not in {t['name'] for t in reader.send('tools/list', {})['tools']}
        finally:
            reader.close()
        for kind in ('flush', 'mate'):
            assembly = app.Documents.Add(12291, app.FileManager.GetTemplateFile(12291), True)
            try:
                definition = assembly.ComponentDefinition
                a = definition.Occurrences.Add(str(fixture), app.TransientGeometry.CreateMatrix())
                a.Grounded = True
                source_id = 'doc_' + a.Definition.Document.InternalName
                def insert(z, preview=True, source=source_id):
                    state = json.loads(client.send('resources/read', dict(uri='inventor://active-document'))['contents'][0]['text'])
                    return client.tool('inventor_insert_component_safe', document_id=state['id'], expected_revision=state['revision'],
                        source_document_id=source, translation_mm=[0,0,z], minimum_clearance_mm=5, preview=preview)
                result = insert(60, source='doc_missing')
                assert result.get('ok') is False and 'SOURCE_NOT_READY' in result['error']['message'], result
                result = insert(60)
                assert result.get('status') == 'preview_rolled_back' and result.get('component_id') is None, result
                assert definition.Occurrences.Count == 1
                for z, error in ((20, 'INTERFERENCE'), (34, 'CLEARANCE_FAILED')):
                    result = insert(z, False)
                    assert result.get('ok') is False and error in result['error']['message'] and 'ROLLED_BACK' in result['error']['message'], result
                    assert definition.Occurrences.Count == 1
                result = insert(60, False)
                assert result.get('status') == 'committed' and result['component_id'].startswith('ent_'), result
                assert definition.Occurrences.Count == 2
                b = definition.Occurrences.Item(2)
                assert not b.Grounded and abs(b.Transformation.Translation.Z-6)<1e-7
                print('Insertion: missing source rejected, preview/collision/clearance rollback, commit passed', flush=True)
                def selected_id(entity):
                    assembly.SelectSet.Clear()
                    assembly.SelectSet.Select(entity)
                    return client.tool('inventor_get_selection')['selection'][0]['id']
                def top(occ):
                    faces = [f for f in occ.SurfaceBodies.Item(1).Faces if int(f.SurfaceType) == 5890]
                    assert len(faces) == 2
                    return max(faces, key=lambda f: f.PointOnFace.Z)
                face_a, face_b = selected_id(top(a)), selected_id(top(b))
                def create(offset, preview=True, stale=False, same=False):
                    state = json.loads(client.send('resources/read', dict(uri='inventor://active-document'))['contents'][0]['text'])
                    return client.tool('inventor_create_constraint_safe', document_id=state['id'],
                        expected_revision='stale' if stale else state['revision'], type=kind,
                        face_a_id=face_a, face_b_id=face_a if same else face_b,
                        offset_mm=offset, minimum_clearance_mm=5, preview=preview)
                def pose():
                    return tuple(b.Transformation.Cell(r,c) for r in range(1,4) for c in range(1,5))
                initial = pose()
                safe = 100 if kind == 'mate' else 40
                for flags in ({'stale': True}, {'same': True}):
                    result = create(safe, False, **flags)
                    assert result.get('ok') is False, result
                    assert definition.Constraints.Count == 0 and pose() == initial
                result = create(safe)
                assert result.get('status') == 'preview_rolled_back' and result.get('constraint_id') is None, result
                assert definition.Constraints.Count == 0
                assert all(abs(x-y)<1e-7 for x,y in zip(pose(), initial))
                if kind == 'flush':
                    result = create(20, False)
                    assert result.get('ok') is False and 'ROLLED_BACK' in result['error']['message'], result
                    assert definition.Constraints.Count == 0
                    assert all(abs(x-y)<1e-7 for x,y in zip(pose(), initial))
                result = create(safe, False)
                assert result.get('status') == 'committed' and result['constraint_id'].startswith('ent_'), result
                assert definition.Constraints.Count == 1
                assert abs(definition.Constraints.Item(1).Offset.Value*10-safe)<1e-7
                measured = app.MeasureTools.GetMinimumDistance(a,b)
                distance_cm = measured[0] if isinstance(measured, tuple) else measured
                assert distance_cm*10 >= 5
                resolved = client.tool('inventor_resolve_entity', entity_id=result['constraint_id'])
                assert resolved.get('status') == 'resolved', resolved
                print(kind + ': persistent face proxies, stale/same-face rejection, preview and commit passed', flush=True)
                state = json.loads(client.send('resources/read', dict(uri='inventor://active-document'))['contents'][0]['text'])
                rejected = client.tool('inventor_save_artifact', document_id=state['id'], expected_revision=state['revision'], format='native')
                assert rejected.get('ok') is False and 'STEP only' in rejected['error']['message'], rejected
                before_export = (assembly.FullFileName, assembly.Dirty, definition.ModelGeometryVersion)
                output = client.tool('inventor_save_artifact', document_id=state['id'], expected_revision=state['revision'], format='step')
                assert output.get('ok') is not False, output
                path = Path(output['path']).resolve()
                assert path.is_file() and path.stat().st_size == output['bytes'] > 0
                assert path.read_text(errors='replace').lstrip().startswith('ISO-10303-21;')
                assert (assembly.FullFileName, assembly.Dirty, definition.ModelGeometryVersion) == before_export
                print('Assembly STEP retained: ' + str(path), flush=True)
                expected_volume = definition.MassProperties.Volume
                before_import = {d.InternalName for d in app.Documents}
                prior_ui = app.UserInterfaceManager.UserInteractionDisabled
                imported = None
                try:
                    app.UserInterfaceManager.UserInteractionDisabled = True
                    imported = app.Documents.Open(str(path), True)
                    actual_volume = imported.ComponentDefinition.MassProperties.Volume
                    assert abs(actual_volume-expected_volume)<1e-6, (actual_volume, expected_volume)
                    print('STEP reopened: matching volume ' + str(actual_volume) + ' cm3', flush=True)
                finally:
                    try:
                        if imported:
                            imported.Close(True)
                        for child in list(app.Documents):
                            if child.InternalName not in before_import:
                                assert child.FullFileName and Path(child.FullFileName).resolve().is_relative_to(path.parent), 'Unexpected import document; preserve it'
                                child.Close(True)
                    finally:
                        app.UserInterfaceManager.UserInteractionDisabled = prior_ui
                        assembly.Activate()
                assert {d.InternalName for d in app.Documents} == before_import
            finally:
                assembly.Close(True)
    finally:
        client.close()
        for document in list(app.Documents):
            if document.InternalName not in original and document.FullFileName and Path(document.FullFileName).resolve() == fixture:
                document.Close(True)
        if previous:
            previous.Activate()
    assert {d.InternalName for d in app.Documents} == original
    assert hashlib.sha256(fixture.read_bytes()).hexdigest() == before_hash
    print('Source file unchanged; original documents restored', flush=True)

if __name__ == '__main__':
    main()
