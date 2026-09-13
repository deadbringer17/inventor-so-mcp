"""Live sheet-metal workflow over MCP: rule, face, flange, cut, flat pattern, DXF.

Creates and removes only its own host-workspace documents. Never saves, closes or touches a user
document, and restores the original open-document set before exiting.
"""
import argparse
import json
import os
from pathlib import Path
import runpy
import time
import win32com.client

Mcp = runpy.run_path(str(Path(__file__).with_name('smoke-atomic-mcp.py')))['Mcp']

DEFAULT_WORKSPACE = Path(os.environ['LOCALAPPDATA']) / 'InventorSO' / 'workspace'
THICKNESS_MM = 2.0
PANEL_X_MM = 120.0
PANEL_Y_MM = 80.0
FLANGE_MM = 25.0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    parser.add_argument('--workspace', default=str(DEFAULT_WORKSPACE))
    parser.add_argument('--keep', action='store_true', help='Retain the generated workspace documents.')
    args = parser.parse_args()
    workspace = Path(args.workspace).resolve()
    app = win32com.client.GetActiveObject('Inventor.Application')
    original = {d.InternalName for d in app.Documents}
    previous = app.ActiveDocument
    stamp = time.strftime('%H%M%S')
    created = []
    client = None
    plain = None
    try:
        client = Mcp(args.server)
        client.initialize()
        names = {t['name'] for t in client.send('tools/list', {})['tools']}
        assert {'inventor_get_sheet_metal_info', 'inventor_list_topology'} <= names, sorted(names)

        def state():
            data = client.send('resources/read', dict(uri='inventor://active-document'))
            return json.loads(data['contents'][0]['text'])

        def batch(*operations):
            current = state()
            result = client.tool('inventor_atomic_batch', document_id=current['id'],
                expected_revision=current['revision'], operations=list(operations))
            assert result.get('status') == 'committed', result
            return result

        part = client.tool('inventor_new_document_safe', name='sotest sm ' + stamp, kind='sheet_metal')
        assert part.get('status') == 'created' and part['sheet_metal'] is True, part
        part_path = Path(part['path'])
        created.append(part_path)
        info = client.tool('inventor_get_sheet_metal_info')
        assert info.get('is_sheet_metal') is True and info['rule'], info
        print('Created sheet-metal part with rule ' + str(info['rule']) +
              ' thickness ' + str(info['thickness_mm']) + ' mm', flush=True)

        rule = batch(dict(command='set_sheet_metal_rule', arguments=dict(thickness_mm=THICKNESS_MM)))
        applied = rule['steps'][0]['data']
        assert abs(applied['thickness_mm'] - THICKNESS_MM) < 1e-6, applied
        assert applied['library_styles_modified'] is False, applied

        sketch = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_rectangle', arguments=dict(x1=-PANEL_X_MM / 2, y1=-PANEL_Y_MM / 2,
                                                          x2=PANEL_X_MM / 2, y2=PANEL_Y_MM / 2)),
            dict(command='close_sketch', arguments={}))
        panel_sketch = sketch['steps'][0]['data']['sketch_name']
        face = batch(dict(command='sheet_metal_face', arguments=dict(sketch_name=panel_sketch)))
        face_data = face['steps'][0]['data']
        assert face_data['body_count'] == 1 and abs(face_data['thickness_mm'] - THICKNESS_MM) < 1e-6, face_data
        print('Base panel created: ' + face_data['feature_name'], flush=True)

        edges = client.tool('inventor_list_topology', kind='edge', limit=200)
        assert edges['returned'] == edges['matched'] and not edges['truncated'], edges
        # Top long edge of the panel: full length, at thickness height, both endpoints at the same Z.
        candidates = [e for e in edges['items']
                      if e['length_mm'] is not None and abs(e['length_mm'] - PANEL_X_MM) < 1e-6
                      and e['midpoint_mm'] is not None and abs(e['midpoint_mm'][2] - THICKNESS_MM) < 1e-6]
        assert candidates, edges['items']
        flange_edge = max(candidates, key=lambda e: e['midpoint_mm'][1])
        assert flange_edge['id'].startswith('ent_'), flange_edge

        bad_edge = client.tool('inventor_atomic_batch', document_id=state()['id'],
            expected_revision=state()['revision'],
            operations=[dict(command='sheet_metal_flange', arguments=dict(edge_ids=['1'], height_mm=FLANGE_MM))])
        assert bad_edge.get('ok') is False and 'portable entity ids' in bad_edge['error']['message'], bad_edge

        flange = batch(dict(command='sheet_metal_flange',
            arguments=dict(edge_ids=[flange_edge['id']], height_mm=FLANGE_MM, angle_degrees=90)))
        flange_data = flange['steps'][0]['data']
        assert flange_data['height_confirmed'] is True, flange_data
        assert abs(flange_data['height_mm'] - FLANGE_MM) < 1e-4, flange_data
        document = app.Documents.ItemByName(str(part_path))
        box = document.ComponentDefinition.RangeBox
        height_mm = (box.MaxPoint.Z - box.MinPoint.Z) * 10
        assert abs(height_mm - FLANGE_MM) < 0.5, 'Flange did not reach the requested height: ' + str(height_mm)
        print('Flange ' + str(flange_data['height_mm']) + ' mm at 90 degrees; model height ' +
              str(round(height_mm, 3)) + ' mm', flush=True)

        cut = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_circle', arguments=dict(cx=-30, cy=0, radius=8)),
            dict(command='close_sketch', arguments={}))
        cut_sketch = cut['steps'][0]['data']['sketch_name']
        # A sketch on a work plane needs an explicit through-all extent; the thickness-driven default
        # applies to a sketch on the panel face and leaves a driverless feature here.
        driverless = client.tool('inventor_atomic_batch', document_id=state()['id'],
            expected_revision=state()['revision'],
            operations=[dict(command='sheet_metal_cut', arguments=dict(sketch_name=cut_sketch))])
        assert driverless.get('ok') is False and 'not healthy' in driverless['error']['message'], driverless
        cut_result = batch(dict(command='sheet_metal_cut',
            arguments=dict(sketch_name=cut_sketch, extent='through_all')))
        cut_data = cut_result['steps'][0]['data']
        assert cut_data['across_bends'] is False and cut_data['extent'] == 'through_all', cut_data
        print('Sheet-metal cut created: ' + cut_data['feature_name'], flush=True)

        flat = batch(dict(command='create_flat_pattern', arguments={}))
        flat_data = flat['steps'][0]['data']
        assert flat_data['created'] is True and flat_data['exists'] is True, flat_data
        assert flat_data['bend_count'] and flat_data['bend_count'] >= 1, flat_data
        assert flat_data['length_mm'] and flat_data['length_mm'] > PANEL_X_MM - 1, flat_data
        print('Flat pattern: ' + str(round(flat_data['length_mm'], 2)) + ' x ' +
              str(round(flat_data['width_mm'], 2)) + ' mm, ' + str(flat_data['bend_count']) + ' bend(s)', flush=True)

        info = client.tool('inventor_get_sheet_metal_info')
        assert info['flat_pattern']['exists'] is True and info['bend_count'] >= 1, info
        assert abs(info['thickness_mm'] - THICKNESS_MM) < 1e-6, info

        current = state()
        saved = client.tool('inventor_save_document_safe', document_id=current['id'],
            expected_revision=current['revision'])
        assert saved.get('status') == 'saved_in_place', saved

        current = state()
        dxf = client.tool('inventor_save_artifact', document_id=current['id'],
            expected_revision=current['revision'], format='dxf')
        assert dxf.get('path', '').endswith('.dxf') and dxf['bytes'] > 0, dxf
        assert dxf['flat_pattern_source'] is True and dxf['source_saved_in_place'] is False, dxf
        dxf_path = Path(dxf['path'])
        assert dxf_path.exists() and dxf_path.stat().st_size > 100, dxf
        head = dxf_path.read_bytes()[:4096].upper()
        assert b'SECTION' in head and b'ENTITIES' in dxf_path.read_bytes().upper(), 'DXF has no drawing sections'
        print('Flat-pattern DXF written: ' + str(dxf_path) + ' (' + str(dxf['bytes']) + ' bytes)', flush=True)

        # An ordinary part is never treated as sheet metal, and never silently converted.
        plain = client.tool('inventor_new_document_safe', name='sotest plain ' + stamp, kind='part')
        created.append(Path(plain['path']))
        assert plain['sheet_metal'] is False, plain
        plain_info = client.tool('inventor_get_sheet_metal_info')
        assert plain_info.get('is_sheet_metal') is False, plain_info
        current = state()
        refused_dxf = client.tool('inventor_save_artifact', document_id=current['id'],
            expected_revision=current['revision'], format='dxf')
        assert refused_dxf.get('ok') is False, refused_dxf
        refused_face = client.tool('inventor_atomic_batch', document_id=current['id'],
            expected_revision=current['revision'],
            operations=[dict(command='sheet_metal_face', arguments=dict(sketch_name='Sketch1'))])
        assert refused_face.get('ok') is False and 'sheet-metal part' in refused_face['error']['message'], refused_face
        print('Ordinary part refused sheet-metal face and flat-pattern DXF', flush=True)

        print(json.dumps(dict(sheet_metal='passed', part=str(part_path), thickness_mm=THICKNESS_MM,
            flange_mm=flange_data['height_mm'], flat_length_mm=flat_data['length_mm'],
            flat_width_mm=flat_data['width_mm'], bends=flat_data['bend_count'],
            dxf=str(dxf_path), dxf_bytes=dxf['bytes'], plain_part_refused=True)), flush=True)
    finally:
        if client:
            client.close()
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
