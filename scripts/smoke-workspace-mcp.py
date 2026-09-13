"""End-to-end workspace lifecycle over MCP: create, model, save, assemble, close.

Creates and removes only its own host-workspace documents. Never saves, closes or touches a
user document, and restores the original open-document set before exiting.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import runpy
import time
import win32com.client

Mcp = runpy.run_path(str(Path(__file__).with_name('smoke-atomic-mcp.py')))['Mcp']

DEFAULT_WORKSPACE = Path(os.environ['LOCALAPPDATA']) / 'InventorSO' / 'workspace'


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    parser.add_argument('--keep', action='store_true', help='Retain the generated workspace documents.')
    parser.add_argument('--workspace', default=str(DEFAULT_WORKSPACE),
        help='Expected workspace root; must match INVENTOR_SO_WORKSPACE in the Inventor process.')
    args = parser.parse_args()
    workspace = Path(args.workspace).resolve()
    app = win32com.client.GetActiveObject('Inventor.Application')
    original = {d.InternalName for d in app.Documents}
    previous = app.ActiveDocument
    stamp = time.strftime('%H%M%S')
    part_name, assembly_name = 'sotest part ' + stamp, 'sotest asm ' + stamp
    drawing_name = 'sotest dwg ' + stamp
    created = []
    client = None
    outside = None
    try:
        client = Mcp(args.server)
        client.initialize()
        names = {t['name'] for t in client.send('tools/list', {})['tools']}
        assert {'inventor_new_document_safe', 'inventor_save_document_safe',
                'inventor_close_document_safe', 'inventor_list_workspace_documents'} <= names, sorted(names)

        def ctx_revision(_client):
            return state()['revision']

        def state():
            data = client.send('resources/read', dict(uri='inventor://active-document'))
            return json.loads(data['contents'][0]['text'])

        rejected = client.tool('inventor_new_document_safe', name='../escape', kind='part')
        assert rejected.get('ok') is False and 'name accepts' in rejected['error']['message'], rejected
        rejected = client.tool('inventor_new_document_safe', name=part_name, kind='drawing')
        assert rejected.get('ok') is False, rejected

        part = client.tool('inventor_new_document_safe', name=part_name, kind='part')
        assert part.get('status') == 'created' and part['workspace_managed'] is True, part
        part_path = Path(part['path'])
        created.append(part_path)
        assert part_path.parent == workspace and part_path.exists(), part
        assert Path(part['workspace_root']) == workspace, part
        assert part['active'] is True and digest(part_path) == hashlib.sha256(part_path.read_bytes()).hexdigest()
        assert part['document_id'] == state()['id'], part
        print('Created workspace part ' + str(part_path) + ' in_active_project=' + str(part['in_active_project']), flush=True)

        taken = client.tool('inventor_new_document_safe', name=part_name, kind='part')
        assert taken.get('ok') is False and 'already exists' in taken['error']['message'], taken

        before = digest(part_path)
        current = state()
        batch = client.tool('inventor_atomic_batch', document_id=current['id'], expected_revision=current['revision'],
            operations=[dict(command='create_parameter', arguments=dict(name='PlateWidth', expression='40 mm', unit='mm')),
                        dict(command='create_sketch', arguments=dict(plane='XY')),
                        dict(command='draw_rectangle', arguments=dict(x1=-20, y1=-12.5, x2=20, y2=12.5)),
                        dict(command='close_sketch', arguments={})])
        assert batch.get('status') == 'committed', batch
        sketch_name = batch['steps'][1]['data']['sketch_name']
        current = state()
        batch = client.tool('inventor_atomic_batch', document_id=current['id'], expected_revision=current['revision'],
            operations=[dict(command='extrude', arguments=dict(sketch_name=sketch_name, distance_mm=8, operation='join'))])
        assert batch.get('status') == 'committed', batch
        document = app.Documents.ItemByName(str(part_path))
        assert document.Dirty, 'Modelling should leave the part dirty until saved'

        current = state()
        stale = client.tool('inventor_save_document_safe', document_id=current['id'], expected_revision='stale')
        assert stale.get('ok') is False and 'STALE_REVISION' in stale['error']['message'], stale
        saved = client.tool('inventor_save_document_safe', document_id=current['id'], expected_revision=current['revision'])
        assert saved.get('status') == 'saved_in_place' and saved['dependents_saved'] is False, saved
        assert not document.Dirty and digest(part_path) != before, saved
        assert saved['sha256'] and saved['bytes'] == part_path.stat().st_size
        print('Modelled and saved the workspace part in place: ' + str(saved['bytes']) + ' bytes', flush=True)

        listed = client.tool('inventor_list_workspace_documents')
        entry = next(d for d in listed['documents'] if Path(d['path']) == part_path)
        assert entry['open'] is True and entry['dirty'] is False and entry['document_id'] == current['id'], entry

        assembly = client.tool('inventor_new_document_safe', name=assembly_name, kind='assembly')
        assert assembly.get('status') == 'created', assembly
        assembly_path = Path(assembly['path'])
        created.append(assembly_path)
        inserted = client.tool('inventor_insert_component_safe', document_id=assembly['document_id'],
            expected_revision=assembly['revision'], source_document_id=part['document_id'],
            translation_mm=[0, 0, 0], minimum_clearance_mm=0, preview=False)
        assert inserted.get('status') == 'committed' and inserted['component_id'], inserted
        assembly_document = app.Documents.ItemByName(str(assembly_path))
        assert assembly_document.ComponentDefinition.Occurrences.Count == 1, 'Expected exactly one inserted occurrence'

        current = state()
        assert current['id'] == assembly['document_id'], current
        saved_assembly = client.tool('inventor_save_document_safe', document_id=current['id'], expected_revision=current['revision'])
        assert saved_assembly.get('status') == 'saved_in_place', saved_assembly
        assert not assembly_document.Dirty and assembly_path.exists()
        print('Assembled the saved workspace part and saved the assembly: ' + str(saved_assembly['bytes']) + ' bytes', flush=True)

        # A drawing draft has no file of its own until it is named once into the workspace.
        drawing = client.tool('inventor_create_drawing_safe', document_id=current['id'],
            expected_revision=ctx_revision(client), scale=1, preview=False)
        assert drawing.get('status') == 'created' and drawing['view_count'] == 4, drawing
        unnamed = client.tool('inventor_save_document_safe', document_id=drawing['document_id'],
            expected_revision=drawing['revision'])
        assert unnamed.get('ok') is False, unnamed
        saved_drawing = client.tool('inventor_save_document_safe', document_id=drawing['document_id'],
            expected_revision=drawing['revision'], name=drawing_name)
        assert saved_drawing.get('status') == 'saved_into_workspace', saved_drawing
        drawing_path = Path(saved_drawing['path'])
        created.append(drawing_path)
        # The host default drawing template decides IDW or Inventor DWG; both are workspace-managed.
        assert drawing_path.suffix in ('.idw', '.dwg'), saved_drawing
        assert drawing_path.parent == workspace and drawing_path.exists(), saved_drawing
        renamed = client.tool('inventor_save_document_safe', document_id=drawing['document_id'],
            expected_revision=ctx_revision(client), name='sotest renamed')
        assert renamed.get('ok') is False and 'ALREADY_ON_DISK' in renamed['error']['message'], renamed
        closed_drawing = client.tool('inventor_close_document_safe', document_id=drawing['document_id'])
        assert closed_drawing.get('status') == 'closed', closed_drawing
        print('Saved the drawing draft into the workspace as ' + drawing_path.name, flush=True)

        # A document outside the workspace is never written in place, even when it is this test's own.
        outside = app.Documents.Add(12290, app.FileManager.GetTemplateFile(12290), True)
        outside_path = Path(os.environ['TEMP']) / ('sotest-outside-' + stamp + '.ipt')
        outside.SaveAs(str(outside_path), False)
        current = state()
        assert current['id'] == 'doc_' + outside.InternalName, current
        refused = client.tool('inventor_save_document_safe', document_id=current['id'], expected_revision=current['revision'])
        assert refused.get('ok') is False and 'OUTSIDE_WORKSPACE' in refused['error']['message'], refused
        refused = client.tool('inventor_close_document_safe', document_id=current['id'])
        assert refused.get('ok') is False and 'OUTSIDE_WORKSPACE' in refused['error']['message'], refused
        assert outside.InternalName in {d.InternalName for d in app.Documents}, 'Refused close must leave the document open'
        outside.Close(True)
        outside = None
        outside_path.unlink(missing_ok=True)
        print('Refused in-place save and close for a document outside the workspace', flush=True)

        held = client.tool('inventor_close_document_safe', document_id=part['document_id'])
        assert held.get('ok') is False and 'REFERENCED_BY_OPEN_DOCUMENT' in held['error']['message'], held

        closed = client.tool('inventor_close_document_safe', document_id=assembly['document_id'])
        assert closed.get('status') == 'closed' and closed['saved_on_close'] is False and closed['file_retained'] is True, closed
        part_hash = digest(part_path)
        app.Documents.ItemByName(str(part_path)).Activate()
        current = state()
        client.tool('inventor_atomic_batch', document_id=current['id'], expected_revision=current['revision'],
            operations=[dict(command='set_parameter', arguments=dict(name='PlateWidth', value='45 mm'))])
        dirty = client.tool('inventor_close_document_safe', document_id=part['document_id'])
        assert dirty.get('ok') is False and 'UNSAVED_CHANGES' in dirty['error']['message'], dirty
        discarded = client.tool('inventor_close_document_safe', document_id=part['document_id'], discard_changes=True)
        assert discarded.get('status') == 'closed' and discarded['discarded_changes'] is True, discarded
        assert digest(part_path) == part_hash, 'Discarding changes must not rewrite the saved file'
        print('Close refused while referenced and while dirty; discard closed without writing', flush=True)

        reopened_part = client.tool('inventor_open_document_safe', file=part_path.name)
        assert reopened_part.get('status') == 'opened' and not reopened_part['missing_references'], reopened_part
        assert reopened_part['dirty'] is False and Path(reopened_part['path']) == part_path, reopened_part
        again = client.tool('inventor_open_document_safe', file=part_path.name)
        assert again.get('status') == 'already_open' and again['document_id'] == reopened_part['document_id'], again
        refused_open = client.tool('inventor_open_document_safe', file='sub/' + part_path.name)
        assert refused_open.get('ok') is False, refused_open
        missing_open = client.tool('inventor_open_document_safe', file='sotest missing.ipt')
        assert missing_open.get('ok') is False, missing_open
        closed_again = client.tool('inventor_close_document_safe', document_id=reopened_part['document_id'])
        assert closed_again.get('status') == 'closed', closed_again
        print('Reopened the workspace part over MCP and closed it again', flush=True)

        # Reopening from disk is the real proof that saved workspace documents survive the session.
        prior_silent = app.SilentOperation
        try:
            app.SilentOperation = True
            reopened = app.Documents.Open(str(assembly_path), True)
        finally:
            app.SilentOperation = prior_silent
        try:
            occurrences = reopened.ComponentDefinition.Occurrences
            missing = [d.ReferenceMissing for d in reopened.ReferencedDocumentDescriptors]
            reopened_ok = occurrences.Count == 1 and not any(missing)
            assert reopened_ok, 'Reopened assembly did not resolve its workspace part: ' + str(missing)
            referenced = [Path(d.FullFileName) for d in reopened.AllReferencedDocuments]
            assert part_path in referenced, referenced
        finally:
            # Closing an assembly disconnects its reference proxies; guard every close.
            for document in list(app.Documents):
                try:
                    if document.InternalName not in original:
                        document.Close(True)
                except Exception:
                    pass
        print('Reopened the saved assembly from disk with its workspace part resolved', flush=True)

        print(json.dumps(dict(workspace_lifecycle='passed', reopened_with_resolved_reference=True,
            drawing=str(drawing_path), reopened_over_mcp=True,
            workspace_root=str(workspace), part=str(part_path), assembly=str(assembly_path),
            in_active_project=part['in_active_project'], modelled_and_saved=True, assembled=True,
            outside_workspace_refused=True, referenced_close_refused=True, dirty_close_refused=True)), flush=True)
    finally:
        if client:
            client.close()
        if outside is not None:
            outside.Close(True)
        # Reverse creation order: a drawing must go before the assembly it references.
        for path in reversed(created):
            for document in list(app.Documents):
                try:
                    if document.InternalName not in original and document.FullFileName and Path(document.FullFileName) == path:
                        document.Close(True)
                except Exception:
                    pass
        if not args.keep:
            for path in created:
                Path(path).unlink(missing_ok=True)
        if previous:
            previous.Activate()
    assert {d.InternalName for d in app.Documents} == original, 'Original open-document set was not restored'
    print('Test-owned documents closed; original document set restored', flush=True)


if __name__ == '__main__':
    main()
