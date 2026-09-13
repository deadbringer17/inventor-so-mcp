"""MCP artifact writes from a test-owned part. Retains outputs; never saves source."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import runpy
import win32com.client

Mcp = runpy.run_path(str(Path(__file__).with_name('smoke-atomic-mcp.py')))['Mcp']


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    parser.add_argument('--fixture', required=True)
    args = parser.parse_args()
    fixture = Path(args.fixture).resolve()
    allowed = Path(__file__).resolve().parent.parent / 'artifacts' / 'live-fixtures'
    assert fixture.is_relative_to(allowed.resolve()) and fixture.is_file()
    original_digest = digest(fixture)
    app = win32com.client.GetActiveObject('Inventor.Application')
    previous = app.ActiveDocument
    original_ids = {d.InternalName for d in app.Documents}
    assert not any(d.FullFileName and Path(d.FullFileName).resolve() == fixture for d in app.Documents)
    part = None
    client = Mcp(args.server)
    output_root = Path(os.environ['LOCALAPPDATA']) / 'InventorSO' / 'artifacts'
    paths = []
    try:
        client.initialize()
        for readonly, flags in ((True, []), (False, ['--toolsets', 'query'])):
            restricted = Mcp(args.server, readonly=readonly, extra_flags=flags)
            try:
                restricted.initialize()
                names = {t['name'] for t in restricted.send('tools/list', {})['tools']}
                assert 'inventor_save_artifact' not in names
            finally:
                restricted.close()
        part = app.Documents.Open(str(fixture), True)
        source_path = part.FullFileName
        source_dirty = part.Dirty
        source_geometry = part.ComponentDefinition.ModelGeometryVersion
        def state():
            value = client.send('resources/read', dict(uri='inventor://active-document'))
            doc = json.loads(value['contents'][0]['text'])
            assert doc['id'] == 'doc_' + part.InternalName
            return doc
        initial = state()
        rejected = client.tool('inventor_save_artifact', document_id=initial['id'], expected_revision='stale', format='native')
        assert rejected.get('ok') is False and 'STALE_REVISION' in rejected['error']['message'], rejected
        for format in ('native', 'step', 'native'):
            doc = state()
            result = client.tool('inventor_save_artifact', document_id=doc['id'], expected_revision=doc['revision'], format=format)
            assert result.get('ok') is not False, result
            path = Path(result['path']).resolve()
            assert path.is_relative_to(output_root.resolve()) and path.is_file()
            assert path.stat().st_size == result['bytes'] > 0
            assert result['overwritten'] is False and result['source_saved_in_place'] is False
            paths.append(path)
            assert len(set(paths)) == len(paths)
            assert part.FullFileName == source_path and part.Dirty == source_dirty
            assert part.ComponentDefinition.ModelGeometryVersion == source_geometry
            assert digest(fixture) == original_digest
            if len(paths) == 1:
                first_digest = digest(path)
            assert digest(paths[0]) == first_digest
            print(json.dumps(dict(format=format, path=str(path), bytes=result['bytes'], source_unchanged=True)), flush=True)
        assert paths[1].read_text(errors='replace').lstrip().startswith('ISO-10303-21;')
    finally:
        client.close()
        if part:
            part.Close(True)
        if previous:
            previous.Activate()
    assert {d.InternalName for d in app.Documents} == original_ids
    assert digest(fixture) == original_digest
    print('Artifact MCP passed: filtering, stale rejection, unique outputs, source/file preservation. Outputs retained.', flush=True)


if __name__ == '__main__':
    main()
