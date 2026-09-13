"""Live checks for the second sheet-metal tranche: hem, corners, fold, contour flange,
unfold/refold, unfold rules and DXF version selection.

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

        def panel(part_suffix):
            part = new_part(part_suffix)
            batch(dict(command='set_sheet_metal_rule', arguments=dict(thickness_mm=THICKNESS_MM)))
            sketch = batch(
                dict(command='create_sketch', arguments=dict(plane='XY')),
                dict(command='draw_rectangle', arguments=dict(x1=-PANEL_X_MM / 2, y1=-PANEL_Y_MM / 2,
                                                              x2=PANEL_X_MM / 2, y2=PANEL_Y_MM / 2)),
                dict(command='close_sketch', arguments={}))
            name = sketch['steps'][0]['data']['sketch_name']
            batch(dict(command='sheet_metal_face', arguments=dict(sketch_name=name)))
            return part

        # --- unfold rules ------------------------------------------------------------------------
        part = panel('rules')
        rules = client.tool('inventor_get_sheet_metal_info')['available_unfold_rules']
        assert rules and any(r['active'] for r in rules), rules
        assert all('k_factor' in r for r in rules), rules
        target = next((r for r in rules if not r['active']), rules[0])
        applied = batch(dict(command='set_sheet_metal_rule',
            arguments=dict(unfold_rule=target['unfold_rule'])))['steps'][0]['data']
        assert applied['unfold_rule'] == target['unfold_rule'], applied
        refused = batch(dict(command='set_sheet_metal_rule', arguments=dict(unfold_rule='no such rule')),
            expect_failure=True)
        assert 'No unfold rule named' in refused['error']['message'], refused
        summary['unfold_rule'] = applied['unfold_rule']
        summary['k_factor'] = applied.get('k_factor')
        print('Unfold rule applied: ' + str(applied['unfold_rule']) +
              ' (k=' + str(applied.get('k_factor')) + ')', flush=True)

        # --- hem, second flange, corner round and chamfer, unfold/refold, DXF 2000 ---------------
        long_edges = [e for e in edges()
                      if e['length_mm'] and abs(e['length_mm'] - PANEL_X_MM) < 1e-6
                      and e['midpoint_mm'] and abs(e['midpoint_mm'][2] - THICKNESS_MM) < 1e-6]
        short_edges = [e for e in edges()
                       if e['length_mm'] and abs(e['length_mm'] - PANEL_Y_MM) < 1e-6
                       and e['midpoint_mm'] and abs(e['midpoint_mm'][2] - THICKNESS_MM) < 1e-6]
        assert long_edges and short_edges, 'panel edges not found'
        front = min(long_edges, key=lambda e: e['midpoint_mm'][1])
        back = max(long_edges, key=lambda e: e['midpoint_mm'][1])
        right = max(short_edges, key=lambda e: e['midpoint_mm'][0])

        flange = batch(dict(command='sheet_metal_flange',
            arguments=dict(edge_ids=[back['id']], height_mm=FLANGE_MM)))['steps'][0]['data']
        assert flange['height_confirmed'] is True, flange
        flange2 = batch(dict(command='sheet_metal_flange',
            arguments=dict(edge_ids=[right['id']], height_mm=FLANGE_MM)))['steps'][0]['data']
        assert flange2['height_confirmed'] is True, flange2
        print('Two flanges created for the corner: ' + flange['feature_name'] + ', ' + flange2['feature_name'],
              flush=True)

        # Corner round first: a hem or flange consumes the corner it touches, so the free corner has
        # to be rounded while it is still free.
        vertical = [e for e in edges()
                    if e['length_mm'] and e['start_mm'] and e['end_mm']
                    and abs(e['start_mm'][0] - e['end_mm'][0]) < 1e-6
                    and abs(e['start_mm'][1] - e['end_mm'][1]) < 1e-6]
        corner_candidates = [e for e in vertical if abs(e['length_mm'] - THICKNESS_MM) < 0.2]
        assert corner_candidates, 'no corner edge found; vertical edges were ' +             str([(round(e['length_mm'], 2), [round(v, 2) for v in e['midpoint_mm']]) for e in vertical])
        corner = min(corner_candidates, key=lambda e: e['midpoint_mm'][0] + e['midpoint_mm'][1])
        tall = next((e for e in vertical if e['length_mm'] > THICKNESS_MM * 2), None)
        if tall is not None:
            refused_corner = batch(dict(command='sheet_metal_corner_round',
                arguments=dict(edge_ids=[tall['id']], radius_mm=4)), expect_failure=True)
            assert 'NOT_A_CORNER_EDGE' in refused_corner['error']['message'], refused_corner
        rounded = batch(dict(command='sheet_metal_corner_round',
            arguments=dict(edge_ids=[corner['id']], radius_mm=4)))['steps'][0]['data']
        assert rounded['radius_mm'] == 4, rounded
        summary['corner_round'] = rounded['feature_name']
        print('Corner round created: ' + rounded['feature_name'], flush=True)

        # The front edge moved when the corner was rounded, so it is picked again.
        front = max((e for e in edges()
                     if e['length_mm'] and e['midpoint_mm']
                     and abs(e['midpoint_mm'][2] - THICKNESS_MM) < 1e-6
                     and abs(e['midpoint_mm'][1] + PANEL_Y_MM / 2) < 1e-6),
                    key=lambda e: e['length_mm'])
        bad_hem = batch(dict(command='sheet_metal_hem',
            arguments=dict(edge_ids=[front['id']], hem_type='rolled')), expect_failure=True)
        assert 'radius_mm is required' in bad_hem['error']['message'], bad_hem
        hem = batch(dict(command='sheet_metal_hem',
            arguments=dict(edge_ids=[front['id']], hem_type='single', gap_mm=1, length_mm=6)))['steps'][0]['data']
        assert hem['hem_type'] == 'single' and hem['edge_count'] == 1, hem
        summary['hem'] = hem['feature_name']
        print('Hem created: ' + hem['feature_name'], flush=True)

        top = max((f for f in faces() if f['surface_type'] == 'kPlaneSurface' and f['area_mm2']),
                  key=lambda f: f['area_mm2'])
        unfolded = batch(dict(command='sheet_metal_unfold',
            arguments=dict(stationary_face_id=top['id'])))['steps'][0]['data']
        refolded = batch(dict(command='sheet_metal_refold',
            arguments=dict(stationary_face_id=top['id'])))['steps'][0]['data']
        summary['unfold'] = unfolded['feature_name']
        summary['refold'] = refolded['feature_name']
        print('Unfold/refold features: ' + unfolded['feature_name'] + ' / ' + refolded['feature_name'], flush=True)

        # Align the blank to a model edge: this is what decides how it sits on the sheet.
        align_edge = max((e for e in edges() if e['length_mm']), key=lambda e: e['length_mm'])
        flat = batch(dict(command='create_flat_pattern',
            arguments=dict(align_to_edge_id=align_edge['id'], alignment='horizontal')))['steps'][0]['data']
        assert flat['exists'] is True and flat['bend_count'] >= 2, flat
        assert flat['alignment_applied'] is True and flat['alignment'], flat
        summary['flat_alignment'] = flat['alignment']
        manufacturing = client.tool('inventor_get_sheet_metal_info')['manufacturing']
        assert manufacturing and manufacturing['minimum_remnant'], manufacturing
        summary['minimum_remnant'] = manufacturing['minimum_remnant']
        summary['material'] = manufacturing['material']
        current = state()
        client.tool('inventor_save_document_safe', document_id=current['id'],
            expected_revision=current['revision'])
        current = state()
        dxf = client.tool('inventor_save_artifact', document_id=current['id'],
            expected_revision=current['revision'], format='dxf', dxf_version='2000')
        assert dxf['dxf_version'] == '2000' and dxf['bytes'] > 100, dxf
        bad_version = client.tool('inventor_save_artifact', document_id=current['id'],
            expected_revision=current['revision'], format='dxf', dxf_version='2019')
        assert bad_version.get('ok') is False and 'dxf_version' in bad_version['error']['message'], bad_version
        summary['dxf_2000_bytes'] = dxf['bytes']
        print('Flat pattern with ' + str(flat['bend_count']) + ' bends; DXF 2000 written (' +
              str(dxf['bytes']) + ' bytes); version 2019 refused', flush=True)

        # --- corner chamfer and fold on their own part ---------------------------------------------
        panel('fold')
        free_corner = min((e for e in edges()
                           if e['length_mm'] and abs(e['length_mm'] - THICKNESS_MM) < 0.2
                           and e['start_mm'] and e['end_mm']
                           and abs(e['start_mm'][0] - e['end_mm'][0]) < 1e-6
                           and abs(e['start_mm'][1] - e['end_mm'][1]) < 1e-6),
                          key=lambda e: e['midpoint_mm'][0] + e['midpoint_mm'][1])
        chamfered = batch(dict(command='sheet_metal_corner_chamfer',
            arguments=dict(edge_ids=[free_corner['id']], distance_mm=5)))['steps'][0]['data']
        assert chamfered['distance_mm'] == 5, chamfered
        summary['corner_chamfer'] = chamfered['feature_name']
        print('Corner chamfer created: ' + chamfered['feature_name'], flush=True)
        # The bend line must end exactly on the panel edges: shorter or longer is refused.
        line = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_line', arguments=dict(x1=-PANEL_X_MM / 2, y1=10, x2=PANEL_X_MM / 2, y2=10)),
            dict(command='close_sketch', arguments={}))
        line_sketch = line['steps'][0]['data']['sketch_name']
        short_line = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_line', arguments=dict(x1=-PANEL_X_MM / 4, y1=-10, x2=PANEL_X_MM / 4, y2=-10)),
            dict(command='close_sketch', arguments={}))['steps'][0]['data']['sketch_name']
        refused_fold = batch(dict(command='sheet_metal_fold',
            arguments=dict(sketch_name=short_line, line_index=1, angle_degrees=90)), expect_failure=True)
        assert 'BEND_LINE_REJECTED' in refused_fold['error']['message'], refused_fold

        folded = batch(dict(command='sheet_metal_fold',
            arguments=dict(sketch_name=line_sketch, line_index=1, angle_degrees=90,
                           bend_location='centerline')))['steps'][0]['data']
        assert folded['bend_count'] and folded['bend_count'] >= 1, folded
        summary['fold'] = folded['feature_name']
        summary['fold_bends'] = folded['bend_count']
        print('Fold created: ' + folded['feature_name'] + ' (' + str(folded['bend_count']) + ' bend)', flush=True)

        # --- punch from Inventor's own catalog ------------------------------------------------------
        panel('punch')
        # Punch centres live on the sheet face, and their sketch coordinates are mapped from model space.
        punch_face = max((f for f in faces() if f['surface_type'] == 'kPlaneSurface' and f['area_mm2']),
                         key=lambda f: f['area_mm2'])
        punch_z = punch_face['point_on_face_mm'][2]
        punch_steps = batch(
            dict(command='create_sketch', arguments=dict(plane=punch_face['id'])),
            dict(command='draw_point', arguments=dict(model_point_mm=[-30, 0, punch_z])),
            dict(command='draw_point', arguments=dict(model_point_mm=[30, 0, punch_z])),
            dict(command='close_sketch', arguments={}))
        punch_sketch = punch_steps['steps'][0]['data']['sketch_name']
        assert punch_steps['steps'][1]['data']['from_model_point'] is True, punch_steps
        plane_sketch = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_point', arguments=dict(x=0, y=0)),
            dict(command='close_sketch', arguments={}))['steps'][0]['data']['sketch_name']
        refused_plane = batch(dict(command='sheet_metal_punch',
            arguments=dict(sketch_name=plane_sketch, punch='obround.ide')), expect_failure=True)
        assert 'PUNCH_NEEDS_FACE_SKETCH' in refused_plane['error']['message'], refused_plane
        refused_path = batch(dict(command='sheet_metal_punch',
            arguments=dict(sketch_name=punch_sketch, punch='..\obround.ide')), expect_failure=True)
        assert 'catalog file name' in refused_path['error']['message'], refused_path
        refused_missing = batch(dict(command='sheet_metal_punch',
            arguments=dict(sketch_name=punch_sketch, punch='no such punch')), expect_failure=True)
        assert 'PUNCH_NOT_IN_CATALOG' in refused_missing['error']['message'], refused_missing
        punched = batch(dict(command='sheet_metal_punch',
            arguments=dict(sketch_name=punch_sketch, punch='obround.ide')))['steps'][0]['data']
        assert punched['punch_count'] == 2 and punched['punch'] == 'obround.ide', punched
        summary['punch'] = punched['feature_name']
        summary['punch_count'] = punched['punch_count']
        print('Punch placed: ' + punched['feature_name'] + ' x' + str(punched['punch_count']) +
              ' from ' + punched['punch_path'], flush=True)

        # --- contour flange on its own part --------------------------------------------------------
        panel('contour')
        edge = max((e for e in edges()
                    if e['length_mm'] and abs(e['length_mm'] - PANEL_X_MM) < 1e-6
                    and e['midpoint_mm'] and abs(e['midpoint_mm'][2] - THICKNESS_MM) < 1e-6),
                   key=lambda e: e['midpoint_mm'][1])
        profile = batch(
            dict(command='create_sketch', arguments=dict(plane='YZ')),
            dict(command='draw_line', arguments=dict(x1=PANEL_Y_MM / 2, y1=THICKNESS_MM,
                                                     x2=PANEL_Y_MM / 2 + 15, y2=THICKNESS_MM + 15)),
            dict(command='close_sketch', arguments={}))
        profile_sketch = profile['steps'][0]['data']['sketch_name']
        contour = batch(dict(command='sheet_metal_contour_flange',
            arguments=dict(sketch_name=profile_sketch, edge_ids=[edge['id']])))['steps'][0]['data']
        assert contour['edge_count'] == 1, contour
        summary['contour_flange'] = contour['feature_name']
        print('Contour flange created: ' + contour['feature_name'], flush=True)

        summary['sheet_metal_features'] = 'passed'
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
