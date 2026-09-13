"""Real MCP fillet/chamfer checks on a known test-owned cylinder, never saved."""
import argparse
import json
from pathlib import Path
import runpy
import win32com.client

Mcp = runpy.run_path(str(Path(__file__).with_name('smoke-atomic-mcp.py')))['Mcp']


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server', required=True)
    parser.add_argument('--fixture', required=True)
    parser.add_argument('--faces', action='store_true', help='Test face sketch and four drilled holes instead of edge features')
    parser.add_argument('--parametric', action='store_true', help='With --faces, test driving hole-center dimensions')
    args = parser.parse_args()
    if args.parametric and not args.faces:
        parser.error('--parametric requires --faces')
    fixture = Path(args.fixture).resolve()
    owned = Path(__file__).resolve().parent.parent / 'artifacts' / 'live-fixtures'
    if not fixture.is_relative_to(owned.resolve()) or not fixture.is_file():
        raise ValueError('Only an existing test-owned artifacts/live-fixtures file is allowed')
    app = win32com.client.GetActiveObject('Inventor.Application')
    count = app.Documents.Count
    active = app.ActiveDocument
    for doc in app.Documents:
        if doc.FullFileName and Path(doc.FullFileName).resolve() == fixture:
            raise ValueError('Fixture already open; refusing to reuse an existing document')
    part = None
    client = Mcp(args.server)
    try:
        client.initialize()
        for command in (('create_sketch', 'hole') if args.faces else ('fillet', 'chamfer')):
            part = app.Documents.Open(str(fixture), True)
            baseline = part.ComponentDefinition.Features.Count
            baseline_sketches = part.ComponentDefinition.Sketches.Count
            volume = part.ComponentDefinition.MassProperties.Volume
            part.SelectSet.Clear()
            body = part.ComponentDefinition.SurfaceBodies.Item(1)
            if args.faces:
                # The fixture is an origin-centered +Z cylinder. This is not a general face selector.
                selected = max(list(body.Faces), key=lambda face: face.PointOnFace.Z)
                z_mm = selected.PointOnFace.Z * 10
            else:
                selected = body.Edges.Item(1)
            part.SelectSet.Select(selected)
            selection = client.tool('inventor_get_selection')
            edge_id = selection['selection'][0]['id']
            assert edge_id.startswith('ent_'), selection
            def batch(preview, invalid=False):
                resource = client.send('resources/read', dict(uri='inventor://active-document'))
                state = json.loads(resource['contents'][0]['text'])
                assert state['id'] == 'doc_' + part.InternalName
                if command == 'create_sketch':
                    arguments = dict(plane=edge_id)
                elif command == 'hole':
                    points = [[-4,-4,z_mm],[4,-4,z_mm],[4,4,z_mm],[-4,4,z_mm]]
                    if invalid:
                        points[1] = [1000,0,z_mm]
                    arguments = dict(face_id=edge_id, kind='drilled', diameter_mm=2, through=True, points_mm=points)
                    arguments['parametric_positioning'] = args.parametric
                else:
                    arguments = dict(edge_ids=[edge_id])
                    arguments['radius_mm' if command == 'fillet' else 'distance_mm'] = 1.0
                return client.tool('inventor_atomic_batch', document_id=state['id'],
                    expected_revision=state['revision'], preview=preview,
                    operations=[dict(command=command, arguments=arguments)])
            preview = batch(True)
            assert preview.get('status') == 'preview_rolled_back', preview
            assert part.ComponentDefinition.Features.Count == baseline
            assert part.ComponentDefinition.Sketches.Count == baseline_sketches
            assert abs(part.ComponentDefinition.MassProperties.Volume - volume) < 1e-8
            if command == 'hole':
                rejected = batch(False, invalid=True)
                assert rejected.get('ok') is False and 'ROLLED_BACK' in rejected['error']['message'], rejected
                assert part.ComponentDefinition.Sketches.Count == baseline_sketches
                assert part.ComponentDefinition.Features.Count == baseline
                assert abs(part.ComponentDefinition.MassProperties.Volume - volume) < 1e-8
            committed = batch(False)
            assert committed.get('status') == 'committed', committed
            if command == 'create_sketch':
                assert part.ComponentDefinition.Sketches.Count == baseline_sketches + 1
                assert part.ComponentDefinition.Features.Count == baseline
            else:
                assert part.ComponentDefinition.Features.Count == baseline + 1
                assert part.ComponentDefinition.MassProperties.Volume < volume
            if command == 'hole':
                import math
                assert abs(volume - part.ComponentDefinition.MassProperties.Volume - 4 * math.pi * 0.1**2 * z_mm/10) < 1e-5
                if args.parametric:
                    data = committed['steps'][0]['data']
                    assert len(data['position_parameters']) == 4, data
                    name = data['position_parameters'][0]['x_parameter']
                    def center_x():
                        sketch = part.ComponentDefinition.Sketches.Item(data['sketch_name'])
                        dimension = next(d for d in sketch.DimensionConstraints if d.Parameter.Name == name)
                        return dimension.PointTwo.Geometry.X
                    before = center_x()
                    original = part.ComponentDefinition.Parameters.Item(name).Value
                    def move(preview):
                        resource = client.send('resources/read', dict(uri='inventor://active-document'))
                        state = json.loads(resource['contents'][0]['text'])
                        assert state['id'] == 'doc_' + part.InternalName
                        response = client.tool('inventor_atomic_batch', document_id=state['id'],
                            expected_revision=state['revision'], preview=preview,
                            operations=[dict(command='set_parameter', arguments=dict(name=name, value='5 mm'))])
                        assert response.get('status') == ('preview_rolled_back' if preview else 'committed'), response
                    move(True)
                    assert abs(part.ComponentDefinition.Parameters.Item(name).Value - original) < 1e-8
                    assert abs(center_x() - before) < 1e-8
                    move(False)
                    assert abs(abs(center_x() - before) - 0.1) < 1e-7
                    print('Parametric center: preview restored, 1 mm movement committed over MCP', flush=True)
            print(json.dumps(dict(command=command, persistent_selection=True,
                preview_restored=True, committed=True, geometry_verified=True)), flush=True)
            part.Close(True)
            part = None
    finally:
        client.close()
        if part:
            part.Close(True)
        if active:
            active.Activate()
    assert app.Documents.Count == count
    print('Both fixture instances closed without saving; original document count restored', flush=True)


if __name__ == '__main__':
    main()
