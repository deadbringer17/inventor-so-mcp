"""Real checkpoint lifecycle on a test-owned part; retains snapshot/recovery files."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import runpy
import win32com.client

Mcp = runpy.run_path(str(Path(__file__).with_name('smoke-atomic-mcp.py')))['Mcp']

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    args = parser.parse_args()
    fixture = (Path(__file__).resolve().parent.parent / 'artifacts/live-fixtures/f8000325f30b4823bb3ec05b97f8ab97/reference-roundtrip.ipt').resolve()
    digest = hashlib.sha256(fixture.read_bytes()).hexdigest()
    app = win32com.client.GetActiveObject('Inventor.Application')
    previous = app.ActiveDocument
    original = {d.InternalName for d in app.Documents}
    assert not any(d.FullFileName and Path(d.FullFileName).resolve() == fixture for d in app.Documents)
    part = recovered = None
    client = Mcp(args.server)
    reader = Mcp(args.server, readonly=True)
    try:
        client.initialize()
        reader.initialize()
        names = {t['name'] for t in reader.send('tools/list', {})['tools']}
        assert 'inventor_checkpoint_list' in names
        assert 'inventor_diff_checkpoint' in names
        assert not {'inventor_checkpoint_create', 'inventor_checkpoint_restore'} & names
        part = app.Documents.Open(str(fixture), True)
        part.ComponentDefinition.Parameters.UserParameters.AddByExpression('CheckpointWidth', '20 mm', 'mm')
        part.Update2()
        def state():
            return json.loads(client.send('resources/read', dict(uri='inventor://active-document'))['contents'][0]['text'])
        before = state()
        stale = client.tool('inventor_checkpoint_create', document_id=before['id'], expected_revision='stale', label='rejected stale test')
        assert stale.get('ok') is False and 'STALE_REVISION' in stale['error']['message'], stale
        created = client.tool('inventor_checkpoint_create', document_id=before['id'], expected_revision=before['revision'], label='before CheckpointWidth edit')
        assert created.get('ok') is not False and created['id'].startswith('cp_'), created
        assert part.FullFileName == str(fixture) and part.Dirty == before['dirty']
        checkpoint_id = created['id']
        def diff():
            current = state()
            return reader.tool('inventor_diff_checkpoint', document_id=current['id'], expected_revision=current['revision'], checkpoint_id=checkpoint_id)
        unchanged = diff()
        assert unchanged.get('ok') is not False, unchanged
        assert not unchanged['parameters']['changed'] and not any(q['changed'] for q in unchanged['physical']), unchanged
        stale_diff = reader.tool('inventor_diff_checkpoint', document_id=before['id'], expected_revision='stale', checkpoint_id=checkpoint_id)
        assert stale_diff.get('ok') is False and 'STALE_REVISION' in stale_diff['error']['message'], stale_diff
        listed = reader.tool('inventor_checkpoint_list', document_id=before['id'])
        assert checkpoint_id in {x['id'] for x in listed['checkpoints']}, listed
        current = state()
        extrusion = part.ComponentDefinition.Features.ExtrudeFeatures.Item(1)
        length_parameter = extrusion.Extent.Distance.Name
        original_feature_name = extrusion.Name
        changed = client.tool('inventor_atomic_batch', document_id=current['id'], expected_revision=current['revision'], preview=False,
            operations=[dict(command='set_parameter', arguments=dict(name='CheckpointWidth', value='30 mm')),
                dict(command='set_parameter', arguments=dict(name=length_parameter, value='40 mm'))])
        assert changed.get('ok') is not False, changed
        assert abs(part.ComponentDefinition.Parameters.Item('CheckpointWidth').Value-3)<1e-8
        extrusion.Name = 'CheckpointRenamedExtrusion'
        before_diff = (part.Dirty, part.ComponentDefinition.ModelGeometryVersion, app.Documents.Count)
        compared = diff()
        assert compared.get('ok') is not False, compared
        assert 'CheckpointWidth' in {p['name'] for p in compared['parameters']['changed']}
        assert original_feature_name in {f['name'] for f in compared['features']['removed']}
        assert 'CheckpointRenamedExtrusion' in {f['name'] for f in compared['features']['added']}
        volume = next(q for q in compared['physical'] if q['quantity'] == 'volume_mm3')
        assert volume['changed'] and abs(volume['delta']-math.pi*1000)<0.001, volume
        assert (part.Dirty, part.ComponentDefinition.ModelGeometryVersion, app.Documents.Count) == before_diff
        assert compared['geometry_equivalence_proven'] is False
        print('Read-only diff passed: parameter changes, feature rename as removal/addition, volume delta = pi*1000 mm3; document state unchanged.', flush=True)
        refused = client.tool('inventor_checkpoint_restore', checkpoint_id=checkpoint_id)
        assert refused.get('ok') is False and 'SOURCE_STILL_OPEN' in refused['error']['message'], refused
        assert abs(part.ComponentDefinition.Parameters.Item('CheckpointWidth').Value-3)<1e-8
        part.Close(True)
        part = None
        restored = client.tool('inventor_checkpoint_restore', checkpoint_id=checkpoint_id)
        assert restored.get('status') == 'recovery_copy_opened' and restored['source_overwritten'] is False, restored
        path = Path(restored['path']).resolve()
        assert path.is_relative_to((Path(os.environ['LOCALAPPDATA']) / 'InventorSO/recoveries').resolve())
        assert Path(app.ActiveDocument.FullFileName).resolve() == path
        recovered = app.ActiveDocument
        assert abs(recovered.ComponentDefinition.Parameters.Item('CheckpointWidth').Value-2)<1e-8
        clean_before = (recovered.Dirty, recovered.ComponentDefinition.ModelGeometryVersion, app.Documents.Count)
        assert recovered.Dirty is False
        restored_diff = diff()
        assert restored_diff.get('ok') is not False, restored_diff
        for category in ('parameters', 'features'):
            assert not any(restored_diff[category][kind] for kind in ('added', 'removed', 'changed')), restored_diff
        assert not any(q['changed'] for q in restored_diff['physical']), restored_diff
        assert (recovered.Dirty, recovered.ComponentDefinition.ModelGeometryVersion, app.Documents.Count) == clean_before
        print('Recovered clean part diff: no differences and no dirty/cache side effects.', flush=True)
        assert hashlib.sha256(fixture.read_bytes()).hexdigest() == digest
        print(json.dumps(dict(checkpoint=checkpoint_id, recovery=str(path), recovered_width_mm=20, source_unchanged=True)), flush=True)
    finally:
        reader.close()
        client.close()
        if recovered:
            recovered.Close(True)
        if part:
            part.Close(True)
        if previous:
            previous.Activate()
    assert {d.InternalName for d in app.Documents} == original
    assert hashlib.sha256(fixture.read_bytes()).hexdigest() == digest
    print('Checkpoint MCP passed: filtering/list, stale rejection, snapshot, edit, open-source refusal, recovery of original parameter.', flush=True)

if __name__ == '__main__':
    main()
