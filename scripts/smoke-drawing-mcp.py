"""Generate one drawing/PDF from an isolated fixture, never save source files."""
import argparse
import hashlib
import json
from pathlib import Path
import runpy
import uuid
import win32com.client

Mcp = runpy.run_path(str(Path(__file__).with_name('smoke-atomic-mcp.py')))['Mcp']

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    parser.add_argument('--format', choices=('pdf', 'native'), default='pdf')
    parser.add_argument('--isolated-project', action='store_true', help='Requires zero open documents; restores the original project after testing.')
    parser.add_argument('--expect-outside-project', action='store_true')
    args = parser.parse_args()
    fixture = (Path(__file__).resolve().parent.parent / 'artifacts/live-fixtures/f8000325f30b4823bb3ec05b97f8ab97/reference-roundtrip.ipt').resolve()
    digest = hashlib.sha256(fixture.read_bytes()).hexdigest()
    app = win32com.client.GetActiveObject('Inventor.Application')
    previous = app.ActiveDocument
    original = {d.InternalName for d in app.Documents}
    prior_project = app.DesignProjectManager.ActiveDesignProject
    prior_project_path = prior_project.FullFileName
    test_project = None
    if args.isolated_project:
        assert not original, 'Isolated project test requires zero open documents.'
    assert not any(d.FullFileName and Path(d.FullFileName).resolve() == fixture for d in app.Documents)
    part = drawing = None
    client = Mcp(args.server)
    try:
        if args.isolated_project:
            project_root = fixture.parent.parent / uuid.uuid4().hex
            project_root.mkdir()
            test_project = app.DesignProjectManager.DesignProjects.Add(36353, 'InventorSO_drawing_test', str(project_root))
            test_project.WorkspacePath = str(fixture.parent)
            test_project.Activate(False)
        client.initialize()
        reader = Mcp(args.server, readonly=True)
        try:
            reader.initialize()
            assert 'inventor_create_drawing_safe' not in {t['name'] for t in reader.send('tools/list', {})['tools']}
        finally:
            reader.close()
        part = app.Documents.Open(str(fixture), True)
        source = (part.Dirty, part.ComponentDefinition.ModelGeometryVersion, part.FullFileName)
        def state():
            return json.loads(client.send('resources/read', dict(uri='inventor://active-document'))['contents'][0]['text'])
        def create(scale=None, preview=True, stale=False, **extra):
            s = state()
            args = dict(document_id=s['id'], expected_revision='stale' if stale else s['revision'], preview=preview, **extra)
            if scale is not None:
                args['scale'] = scale
            return client.tool('inventor_create_drawing_safe', **args)
        def undisturbed():
            assert app.Documents.Count == count and app.ActiveDocument.InternalName == part.InternalName
        result = create(2, stale=True)
        assert result.get('ok') is False and 'STALE_REVISION' in result['error']['message'], result
        count = app.Documents.Count
        result = create(2)
        assert result.get('status') == 'preview_rolled_back' and result['view_count'] == 4, result
        assert result['scale_mode'] == 'explicit' and result['projection'] == 'first', result
        undisturbed()
        # Layout failures are machine-readable codes, not prose prefixes: a caller must be able to
        # branch on error.code without parsing the message.
        result = create(100, False)
        assert result.get('ok') is False, result
        assert result['error']['code'] == 'VIEW_OUTSIDE_LAYOUT' or 'E_INVALIDARG' in result['error']['message'], result
        undisturbed()
        result = create(4, False)
        assert result.get('ok') is False and result['error']['code'] == 'VIEW_OUTSIDE_LAYOUT', result
        assert result['error']['details']['required_width_mm'] > 0, result
        undisturbed()
        # Automatic scaling: the planner must pick a normalised ISO step on its own.
        ladder = (10, 5, 2, 1, 0.5, 0.2, 0.1, 0.05, 0.02, 0.01, 0.005, 0.002)
        result = create()
        assert result.get('status') == 'preview_rolled_back' and result['scale_mode'] == 'auto', result
        assert result['scale'] in ladder, result
        auto_scale = result['scale']
        undisturbed()
        # A projected view written before 'front' is legal: order in the list must not matter, and
        # the response must echo the caller's order rather than the internal creation order.
        result = create(2, views='top,front,right')
        assert result.get('status') == 'preview_rolled_back' and result['views'] == 'top,front,right', result
        assert result['view_count'] == 3, result
        undisturbed()
        # 'back' is a base view, not a projected one: AddProjectedView derives direction, not distance.
        result = create(2, views='front,right,back')
        assert result.get('status') == 'preview_rolled_back' and result['view_count'] == 3, result
        undisturbed()
        # Argument validation must arrive as INVALID_ARGUMENT, distinguishable from an Inventor fault.
        result = create(2, views='front,plan')
        assert result.get('ok') is False and result['error']['code'] == 'INVALID_ARGUMENT', result
        undisturbed()
        print(json.dumps(dict(auto_scale=auto_scale, projection='first')), flush=True)
        result = create(2, False)
        assert result.get('status') == 'created' and result['manufacturing_ready'] is False, result
        assert 'doc_' + app.ActiveDocument.InternalName == result['document_id']
        drawing = app.ActiveDocument
        assert drawing.ActiveSheet.DrawingViews.Count == 4
        if args.format == 'native':
            print('Source drawing: InventorDWG=' + str(drawing.IsInventorDWG), flush=True)
        s = state()
        exported = client.tool('inventor_save_artifact', document_id=s['id'], expected_revision=s['revision'], format=args.format)
        if args.expect_outside_project:
            assert exported.get('ok') is False and 'REFERENCE_OUTSIDE_PROJECT' in exported['error']['message'], exported
            print('Native copy rejected out-of-project references without changing project.', flush=True)
            return
        assert exported.get('ok') is not False, exported
        artifact = Path(exported['path'])
        assert artifact.stat().st_size == exported['bytes'] > 0
        if args.format == 'pdf':
            assert artifact.read_bytes().startswith(b'%PDF-')
        else:
            assert artifact.suffix.lower() in ('.idw', '.dwg')
            assert exported['native_dependencies_packaged'] is False
            assert exported['required_project'] == app.DesignProjectManager.ActiveDesignProject.FullFileName
            assert fixture in {Path(r['path']).resolve() for r in exported['required_references']}
            native_digest = hashlib.sha256(artifact.read_bytes()).hexdigest()
            drawing.Close(True)
            drawing = None
            assert (part.Dirty, part.ComponentDefinition.ModelGeometryVersion, part.FullFileName) == source
            part.Close(True)
            part = None
            prior_silent = app.SilentOperation
            try:
                app.SilentOperation = True
                drawing = app.Documents.Open(str(artifact), True)
            finally:
                app.SilentOperation = prior_silent
            print('Reopened sheets: ' + str([(s.Name,s.DrawingViews.Count) for s in drawing.Sheets]), flush=True)
            assert drawing.Sheets.Count == 1 and drawing.ActiveSheet.DrawingViews.Count == 4
            refs = list(drawing.ReferencedDocumentDescriptors)
            assert len(refs) == 1 and not refs[0].ReferenceMissing
            part = refs[0].ReferencedDocument
            assert Path(refs[0].ReferencedDocument.FullFileName).resolve() == fixture
            assert hashlib.sha256(artifact.read_bytes()).hexdigest() == native_digest
        assert (part.Dirty, part.ComponentDefinition.ModelGeometryVersion, part.FullFileName) == source
        assert hashlib.sha256(fixture.read_bytes()).hexdigest() == digest
        print(json.dumps(dict(format=args.format, path=str(artifact), bytes=exported['bytes'], views=4, source_unchanged=True)), flush=True)
    finally:
        client.close()
        if drawing:
            drawing.Close(True)
        if part:
            part.Close(True)
        if previous:
            previous.Activate()
        if test_project:
            assert app.Documents.Count == 0, 'Refusing project change with open documents.'
            prior_project.Activate(False)
            assert app.DesignProjectManager.ActiveDesignProject.FullFileName == prior_project_path
            test_project.Remove()  # unregister only; retain the test IPJ on disk
        assert {d.InternalName for d in app.Documents} == original
        assert hashlib.sha256(fixture.read_bytes()).hexdigest() == digest
    assert {d.InternalName for d in app.Documents} == original
    print('Drawing MCP passed: filtering, stale rejection, preview cleanup, layout rejection, four views and ' + args.format + ' output.', flush=True)

if __name__ == '__main__':
    main()
