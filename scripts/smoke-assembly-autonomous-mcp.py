"""Live check that an assembly can be built and constrained without a person selecting geometry.

Two parts are modelled from scratch, inserted, then mated using face proxies discovered through
inventor_list_topology. Creates and removes only its own host-workspace documents, never saves,
closes or touches a user document, and restores the original open-document set before exiting.
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
BASE = (80.0, 60.0, 10.0)
TOP = (40.0, 30.0, 8.0)


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

        def plate(suffix, size):
            width, depth, height = size
            part = client.tool('inventor_new_document_safe', name='sotest asmpart ' + suffix + ' ' + stamp,
                kind='part')
            assert part.get('status') == 'created', part
            created.append(Path(part['path']))
            sketch = batch(
                dict(command='create_sketch', arguments=dict(plane='XY')),
                dict(command='draw_rectangle', arguments=dict(x1=-width / 2, y1=-depth / 2,
                                                              x2=width / 2, y2=depth / 2)),
                dict(command='close_sketch', arguments={}))
            batch(dict(command='extrude', arguments=dict(
                sketch_name=sketch['steps'][0]['data']['sketch_name'], distance_mm=height, operation='join')))
            current = state()
            saved = client.tool('inventor_save_document_safe', document_id=current['id'],
                expected_revision=current['revision'])
            assert saved.get('status') == 'saved_in_place', saved
            return part

        base = plate('base', BASE)
        top = plate('top', TOP)
        print('Two plates modelled and saved from scratch', flush=True)

        assembly = client.tool('inventor_new_document_safe', name='sotest asm auto ' + stamp, kind='assembly')
        assert assembly.get('status') == 'created', assembly
        created.append(Path(assembly['path']))
        for source, offset in ((base, [0, 0, 0]), (top, [0, 0, 60])):
            inserted = client.tool('inventor_insert_component_safe', document_id=state()['id'],
                expected_revision=state()['revision'], source_document_id=source['document_id'],
                translation_mm=offset, minimum_clearance_mm=0, preview=False)
            assert inserted.get('status') == 'committed', inserted

        # Components and their faces are discovered, not selected by a person.
        components = client.tool('inventor_list_topology', kind='occurrence')
        assert components['document_type'] == 'assembly' and components['returned'] == 2, components
        assert all(c['id'].startswith('ent_') for c in components['items']), components
        summary['components'] = [c['name'] for c in components['items']]
        print('Occurrences discovered: ' + ', '.join(summary['components']), flush=True)

        def planar_faces(component_id):
            listed = client.tool('inventor_list_topology', kind='face', component_id=component_id,
                geometry='Plane', limit=200)
            assert listed['scoped_to_component'] is True, listed
            return [f for f in listed['items'] if f['constrainable'] and f['area_mm2']]

        by_name = {c['name']: c for c in components['items']}
        base_component = min(components['items'], key=lambda c: c['position_mm'][2])
        top_component = max(components['items'], key=lambda c: c['position_mm'][2])

        base_faces = planar_faces(base_component['id'])
        top_faces = planar_faces(top_component['id'])
        assert len(base_faces) >= 6 and len(top_faces) >= 6, (len(base_faces), len(top_faces))

        # Top face of the lower plate, bottom face of the upper one: chosen by outward normal and
        # height, entirely from the listing. Nothing is selected in Inventor.
        def horizontal(items):
            return [f for f in items if f['outward_normal'] and abs(abs(f['outward_normal'][2]) - 1) < 1e-6]
        base_top = max(horizontal(base_faces), key=lambda f: f['point_on_face_mm'][2])
        top_bottom = min(horizontal(top_faces), key=lambda f: f['point_on_face_mm'][2])
        assert base_top['outward_normal'][2] > 0, base_top
        assert top_bottom['outward_normal'][2] < 0, top_bottom
        summary['mated_faces'] = [round(base_top['point_on_face_mm'][2], 3),
                                  round(top_bottom['point_on_face_mm'][2], 3)]

        current = state()
        preview = client.tool('inventor_create_constraint_safe', document_id=current['id'],
            expected_revision=current['revision'], type='mate', face_a_id=base_top['id'],
            face_b_id=top_bottom['id'], offset_mm=0, minimum_clearance_mm=0, preview=True)
        assert preview.get('status') == 'preview_rolled_back', preview

        current = state()
        constraint = client.tool('inventor_create_constraint_safe', document_id=current['id'],
            expected_revision=current['revision'], type='mate', face_a_id=base_top['id'],
            face_b_id=top_bottom['id'], offset_mm=0, minimum_clearance_mm=0, preview=False)
        assert constraint.get('status') == 'committed', constraint
        summary['constraint'] = constraint.get('constraint_id') or constraint.get('status')

        listed = client.tool('inventor_list_constraints')['constraints']
        assert len(listed) == 1 and listed[0]['type'] == 'mate', listed
        summary['constraint_health'] = listed[0].get('health')
        document = app.Documents.ItemByName(str(Path(assembly['path'])))
        occurrences = document.ComponentDefinition.Occurrences
        stacked = [o.Transformation.Translation.Z * 10 for o in occurrences]
        assert abs(max(stacked) - BASE[2]) < 1e-6, 'the upper plate did not land on the lower one: ' + str(stacked)
        print('Mate constraint created autonomously; upper plate now sits at z=' +
              str(round(max(stacked), 3)) + ' mm', flush=True)

        # A face of the same component cannot be mated to itself, and a part face id is not a proxy.
        current = state()
        same = client.tool('inventor_create_constraint_safe', document_id=current['id'],
            expected_revision=current['revision'], type='mate', face_a_id=base_top['id'],
            face_b_id=base_faces[0]['id'], offset_mm=0, minimum_clearance_mm=0, preview=False)
        assert same.get('ok') is False, same

        current = state()
        saved_assembly = client.tool('inventor_save_document_safe', document_id=current['id'],
            expected_revision=current['revision'])
        assert saved_assembly.get('status') == 'saved_in_place', saved_assembly
        summary['assembly_bytes'] = saved_assembly['bytes']

        bom = client.tool('inventor_get_assembly_bom')
        assert len(bom['bom']) == 2 and len(bom['occurrences']) == 2, bom
        summary['bom_rows'] = len(bom['bom'])
        summary['bom_parts'] = sorted(row['part_number'] for row in bom['bom'])
        print('Assembly saved (' + str(saved_assembly['bytes']) + ' bytes); BOM rows: ' +
              str(summary['bom_rows']), flush=True)

        # --- insert constraint: a pin into a hole, both found by geometry ----------------------------
        pin = client.tool('inventor_new_document_safe', name='sotest pin ' + stamp, kind='part')
        created.append(Path(pin['path']))
        pin_sketch = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_circle', arguments=dict(cx=0, cy=0, radius=5)),
            dict(command='close_sketch', arguments={}))['steps'][0]['data']['sketch_name']
        batch(dict(command='extrude', arguments=dict(sketch_name=pin_sketch, distance_mm=40, operation='join')))
        current = state()
        client.tool('inventor_save_document_safe', document_id=current['id'], expected_revision=current['revision'])

        block = client.tool('inventor_new_document_safe', name='sotest block ' + stamp, kind='part')
        created.append(Path(block['path']))
        block_sketch = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_rectangle', arguments=dict(x1=-30, y1=-30, x2=30, y2=30)),
            dict(command='close_sketch', arguments={}))['steps'][0]['data']['sketch_name']
        batch(dict(command='extrude', arguments=dict(sketch_name=block_sketch, distance_mm=12, operation='join')))
        hole_sketch = batch(
            dict(command='create_sketch', arguments=dict(plane='XY')),
            dict(command='draw_circle', arguments=dict(cx=12, cy=0, radius=5.2)),
            dict(command='close_sketch', arguments={}))['steps'][0]['data']['sketch_name']
        batch(dict(command='extrude', arguments=dict(sketch_name=hole_sketch, distance_mm=12, operation='cut')))
        current = state()
        client.tool('inventor_save_document_safe', document_id=current['id'], expected_revision=current['revision'])

        fit = client.tool('inventor_new_document_safe', name='sotest asm fit ' + stamp, kind='assembly')
        created.append(Path(fit['path']))
        for source, offset in ((block, [0, 0, 0]), (pin, [0, 0, 80])):
            inserted = client.tool('inventor_insert_component_safe', document_id=state()['id'],
                expected_revision=state()['revision'], source_document_id=source['document_id'],
                translation_mm=offset, minimum_clearance_mm=0, preview=False)
            assert inserted.get('status') == 'committed', inserted

        parts = client.tool('inventor_list_topology', kind='occurrence')['items']
        block_component = min(parts, key=lambda c: c['position_mm'][2])
        pin_component = max(parts, key=lambda c: c['position_mm'][2])

        def circles(component_id):
            listed = client.tool('inventor_list_topology', kind='edge', component_id=component_id,
                geometry='Circle', limit=200)['items']
            return [e for e in listed if e['length_mm']]

        # The hole rim is the small circle on the block's top face; the pin rim is its lower circle.
        hole_rim = min(circles(block_component['id']), key=lambda e: e['length_mm'])
        pin_rim = min(circles(pin_component['id']), key=lambda e: e['start_mm'][2])

        current = state()
        wrong_kind = client.tool('inventor_create_constraint_safe', document_id=current['id'],
            expected_revision=current['revision'], type='insert', face_a_id=base_top['id'],
            face_b_id=top_bottom['id'], offset_mm=0, minimum_clearance_mm=0, preview=False)
        assert wrong_kind.get('ok') is False, wrong_kind

        current = state()
        fitted = client.tool('inventor_create_constraint_safe', document_id=current['id'],
            expected_revision=current['revision'], type='insert', face_a_id=pin_rim['id'],
            face_b_id=hole_rim['id'], offset_mm=0, minimum_clearance_mm=0, axes_opposed=True, preview=False)
        assert fitted.get('status') == 'committed', fitted
        fit_document = app.Documents.ItemByName(str(Path(fit['path'])))
        placed = [o for o in fit_document.ComponentDefinition.Occurrences
                  if 'pin' in o.Name.lower()][0].Transformation.Translation
        assert abs(placed.X * 10 - 12) < 1e-3 and abs(placed.Y * 10) < 1e-3,             'the pin did not centre in the hole: ' + str((placed.X * 10, placed.Y * 10, placed.Z * 10))
        summary['insert_pin_xy_mm'] = [round(placed.X * 10, 4), round(placed.Y * 10, 4)]
        print('Insert constraint centred the pin in the hole at x=' + str(round(placed.X * 10, 3)) +
              ', y=' + str(round(placed.Y * 10, 3)), flush=True)

        # --- angle constraint on two planar faces -----------------------------------------------------
        block_faces = [f for f in client.tool('inventor_list_topology', kind='face',
                                              component_id=block_component['id'], geometry='Plane',
                                              limit=200)['items'] if f['outward_normal']]
        side = next(f for f in block_faces if abs(abs(f['outward_normal'][0]) - 1) < 1e-6)
        pin_faces = [f for f in client.tool('inventor_list_topology', kind='face',
                                            component_id=pin_component['id'], geometry='Plane',
                                            limit=200)['items'] if f['outward_normal']]
        pin_flat = max(pin_faces, key=lambda f: f['point_on_face_mm'][2])
        current = state()
        angled = client.tool('inventor_create_constraint_safe', document_id=current['id'],
            expected_revision=current['revision'], type='angle', face_a_id=side['id'],
            face_b_id=pin_flat['id'], angle_degrees=90, minimum_clearance_mm=0, preview=True)
        summary['angle_preview'] = angled.get('status') or angled.get('error', {}).get('message', '')[:80]
        print('Angle constraint preview: ' + str(summary['angle_preview']), flush=True)

        current = state()
        client.tool('inventor_save_document_safe', document_id=current['id'], expected_revision=current['revision'])

        summary['assembly_autonomous'] = 'passed'
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
