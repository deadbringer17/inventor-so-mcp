"""Live checks for the shop-floor sheet-metal tranche: bend table, punch catalog rows, DXF layers,
per-bend unfold/refold, rip and lofted flange.

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
FLANGE_MM = 20.0


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    parser.add_argument('--workspace', default=str(DEFAULT_WORKSPACE))
    parser.add_argument('--keep', action='store_true')
    args = parser.parse_args()
    workspace = Path(args.workspace).resolve()
    app = win32com.client.GetActiveObject('Inventor.Application')
    original = {d.InternalName for d in app.Documents}
    previous = app.ActiveDocument
    stamp = time.strftime('%H%M%S')
    created = []
    client = None
    summary = {}
    try:
        client = Mcp(args.server)
        client.initialize()

        def state():
            data = client.send('resources/read', dict(uri='inventor://active-document'))
            return json.loads(data['contents'][0]['text'])

        def batch(*operations, expect_failure=False):
            current = state()
            result = client.tool('inventor_atomic_batch', document_id=current['id'],
                expected_revision=current['revision'], operations=list(operations))
            if expect_failure:
                assert result.get('ok') is False, result
            else:
                assert result.get('status') == 'committed', result
            return result

        def new_part(suffix):
            part = client.tool('inventor_new_document_safe', name='sotest sm ' + suffix + ' ' + stamp,
                kind='sheet_metal')
            assert part.get('status') == 'created' and part['sheet_metal'] is True, part
            created.append(Path(part['path']))
            return part

        def edges(**kwargs):
            return client.tool('inventor_list_topology', kind='edge', limit=200, **kwargs)['items']

        def faces(**kwargs):
            return client.tool('inventor_list_topology', kind='face', limit=200, **kwargs)['items']

        def panel(suffix):
            part = new_part(suffix)
            batch(dict(command='set_sheet_metal_rule', arguments=dict(thickness_mm=THICKNESS_MM)))
            sketch = batch(
                dict(command='create_sketch', arguments=dict(plane='XY')),
                dict(command='draw_rectangle', arguments=dict(x1=-PANEL_X_MM / 2, y1=-PANEL_Y_MM / 2,
                                                              x2=PANEL_X_MM / 2, y2=PANEL_Y_MM / 2)),
                dict(command='close_sketch', arguments={}))
            batch(dict(command='sheet_metal_face',
                arguments=dict(sketch_name=sketch['steps'][0]['data']['sketch_name'])))
            return part

        # --- bend table, per-bend unfold, DXF layers -----------------------------------------------
        panel('shop')
        top_edges = [e for e in edges()
                     if e['length_mm'] and abs(e['length_mm'] - PANEL_X_MM) < 1e-6
                     and e['midpoint_mm'] and abs(e['midpoint_mm'][2] - THICKNESS_MM) < 1e-6]
        back = max(top_edges, key=lambda e: e['midpoint_mm'][1])
        front = min(top_edges, key=lambda e: e['midpoint_mm'][1])
        for edge in (back, front):
            batch(dict(command='sheet_metal_flange',
                arguments=dict(edge_ids=[edge['id']], height_mm=FLANGE_MM, angle_degrees=90)))

        bend_faces = [f for f in faces() if f['surface_type'] == 'kCylinderSurface']
        assert len(bend_faces) >= 2, [f['surface_type'] for f in faces()]
        flat_faces = [f for f in faces() if f['surface_type'] == 'kPlaneSurface' and f['area_mm2']]
        stationary = max(flat_faces, key=lambda f: f['area_mm2'])

        refused_bend = batch(dict(command='sheet_metal_unfold',
            arguments=dict(stationary_face_id=stationary['id'], bend_face_ids=[stationary['id']])),
            expect_failure=True)
        assert 'NOT_A_BEND_FACE' in refused_bend['error']['message'], refused_bend

        one_bend = batch(dict(command='sheet_metal_unfold',
            arguments=dict(stationary_face_id=stationary['id'],
                           bend_face_ids=[bend_faces[0]['id']])))['steps'][0]['data']
        assert one_bend['all_bends'] is False and one_bend['bends_selected'] == 1, one_bend
        refolded = batch(dict(command='sheet_metal_refold',
            arguments=dict(stationary_face_id=stationary['id'],
                           bend_face_ids=[bend_faces[0]['id']])))['steps'][0]['data']
        assert refolded['bends_selected'] == 1, refolded
        summary['single_bend_unfold'] = one_bend['feature_name']
        summary['single_bend_refold'] = refolded['feature_name']
        print('Per-bend unfold/refold: ' + one_bend['feature_name'] + ' / ' + refolded['feature_name'], flush=True)

        flat = batch(dict(command='create_flat_pattern', arguments={}))['steps'][0]['data']
        assert flat['bends'], flat
        angles = [b['angle_degrees'] for b in flat['bends'] if b['angle_degrees'] is not None]
        assert angles and all(abs(a - 90) < 1e-6 for a in angles), flat['bends']
        assert all(b['inner_radius_mm'] is not None and b['k_factor'] is not None for b in flat['bends']), flat['bends']
        summary['bend_table'] = flat['bends']
        print('Bend table: ' + str(len(flat['bends'])) + ' bends, angles ' +
              str(sorted({round(a, 3) for a in angles})) + ', radii ' +
              str(sorted({round(b['inner_radius_mm'], 3) for b in flat['bends']})), flush=True)

        current = state()
        client.tool('inventor_save_document_safe', document_id=current['id'], expected_revision=current['revision'])
        current = state()
        layered = client.tool('inventor_save_artifact', document_id=current['id'],
            expected_revision=current['revision'], format='dxf', dxf_version='2013',
            dxf_layers_json=json.dumps({'OuterProfileLayer': 'SOTEST_OUTER', 'BendUpLayer': 'SOTEST_BEND_UP'}))
        assert layered.get('bytes', 0) > 100, layered
        assert 'OuterProfileLayer=SOTEST_OUTER' in layered['dxf_options'], layered
        text = Path(layered['path']).read_text(errors='ignore').upper()
        assert 'SOTEST_OUTER' in text, 'layer name is missing from the DXF'
        refused_layer = client.tool('inventor_save_artifact', document_id=current['id'],
            expected_revision=current['revision'], format='dxf',
            dxf_layers_json=json.dumps({'OuterProfileLayer': 'bad&AcadVersion=2000'}))
        assert refused_layer.get('ok') is False, refused_layer
        summary['dxf_layers'] = layered['dxf_options']
        print('DXF with named layers written (' + str(layered['bytes']) + ' bytes); injected value refused', flush=True)

        # --- punch catalog rows ---------------------------------------------------------------------
        panel('rows')
        punch_face = max((f for f in faces() if f['surface_type'] == 'kPlaneSurface' and f['area_mm2']),
                         key=lambda f: f['area_mm2'])
        punch_sketch = batch(
            dict(command='create_sketch', arguments=dict(plane=punch_face['id'])),
            dict(command='draw_point', arguments=dict(model_point_mm=[0, 0, punch_face['point_on_face_mm'][2]])),
            dict(command='close_sketch', arguments={}))['steps'][0]['data']['sketch_name']
        punched = batch(dict(command='sheet_metal_punch',
            arguments=dict(sketch_name=punch_sketch, punch='keyhole.ide')))['steps'][0]['data']
        assert isinstance(punched['table_driven'], bool), punched
        summary['punch_table_driven'] = punched['table_driven']
        summary['punch_table_rows'] = len(punched['table_rows'])
        if punched['table_driven']:
            row_probe = batch(dict(command='sheet_metal_punch',
                arguments=dict(sketch_name=punch_sketch, punch='keyhole.ide', table_row=1)),
                expect_failure=True)
            print('table-driven punch; row selection reported: ' + str(row_probe.get('error')), flush=True)
        else:
            refused_row = batch(dict(command='sheet_metal_punch',
                arguments=dict(sketch_name=punch_sketch, punch='keyhole.ide', table_row=1)), expect_failure=True)
            assert 'PUNCH_NOT_TABLE_DRIVEN' in refused_row['error']['message'], refused_row
        print('Punch catalog rows reported: table_driven=' + str(punched['table_driven']) +
              ', rows=' + str(len(punched['table_rows'])), flush=True)

        # --- rip: reachable behaviour only ------------------------------------------------------------
        # A positive rip needs a closed section. Neither route to one is available: contour roll has no
        # Add in the API at all, and a closed-profile contour flange is refused by Inventor (both probed
        # on 2027). What is verified here is that an unsuitable face fails with the explicit error
        # instead of a bare COM code.
        panel('rip')
        rip_edge = max((e for e in edges()
                        if e['length_mm'] and abs(e['length_mm'] - PANEL_X_MM) < 1e-6
                        and e['midpoint_mm'] and abs(e['midpoint_mm'][2] - THICKNESS_MM) < 1e-6),
                       key=lambda e: e['midpoint_mm'][1])
        batch(dict(command='sheet_metal_flange',
            arguments=dict(edge_ids=[rip_edge['id']], height_mm=FLANGE_MM, angle_degrees=90)))
        wall = min((f for f in faces() if f['surface_type'] == 'kPlaneSurface' and f['area_mm2']),
                   key=lambda f: f['area_mm2'])
        refused_rip = batch(dict(command='sheet_metal_rip',
            arguments=dict(face_id=wall['id'], rip_type='face_extents')), expect_failure=True)
        assert 'RIP_REJECTED' in refused_rip['error']['message'], refused_rip
        missing_points = batch(dict(command='sheet_metal_rip',
            arguments=dict(face_id=wall['id'], rip_type='point_to_point', sketch_name='nope', gap_mm=1)),
            expect_failure=True)
        assert 'No sketch named' in missing_points['error']['message'], missing_points
        summary['rip_open_section_refused'] = True
        print('Rip on an open section refused with RIP_REJECTED, as expected', flush=True)

        # --- lofted flange between two open profiles --------------------------------------------------
        new_part('loft')
        batch(dict(command='set_sheet_metal_rule', arguments=dict(thickness_mm=THICKNESS_MM)))
        lower = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_line', arguments=dict(x1=-40, y1=0, x2=40, y2=0)),
            dict(command='close_sketch', arguments={}))['steps'][0]['data']['sketch_name']
        plane = batch(dict(command='create_work_plane',
            arguments=dict(type='offset', refs=['XY'], offset_mm=60)))['steps'][0]['data']['work_plane_name']
        upper = batch(
            dict(command='create_sketch', arguments=dict(plane=plane)),
            dict(command='draw_line', arguments=dict(x1=-15, y1=0, x2=15, y2=0)),
            dict(command='close_sketch', arguments={}))['steps'][0]['data']['sketch_name']
        refused_same = batch(dict(command='sheet_metal_lofted_flange',
            arguments=dict(sketch_one=lower, sketch_two=lower)), expect_failure=True)
        assert 'different sketches' in refused_same['error']['message'], refused_same
        loft = batch(dict(command='sheet_metal_lofted_flange',
            arguments=dict(sketch_one=lower, sketch_two=upper, output='die_formed')))['steps'][0]['data']
        summary['lofted_flange'] = loft['feature_name']
        print('Lofted flange created: ' + loft['feature_name'], flush=True)

        summary['sheet_metal_shop'] = 'passed'
        print(json.dumps(summary), flush=True)
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
