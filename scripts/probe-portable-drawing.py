"""Real CAD relocation probe; operates on unique copies, requires no open documents."""
from pathlib import Path
import hashlib
import json
import shutil
import uuid
import win32com.client


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    repo = Path(__file__).resolve().parent.parent
    source = repo / 'artifacts/live-fixtures/f8000325f30b4823bb3ec05b97f8ab97/reference-roundtrip.ipt'
    native = Path.home() / 'AppData/Local/InventorSO/artifacts/9e8388b3ad9544cd88344f5b597fd926/model.dwg'
    originals = {p: digest(p) for p in (source, native)}
    app = win32com.client.GetActiveObject('Inventor.Application')
    assert app.Documents.Count == 0, 'Refusing to close any existing document.'
    previous = app.DesignProjectManager.ActiveDesignProject
    previous_path = previous.FullFileName
    silent = app.SilentOperation
    root = repo / 'artifacts/live-fixtures' / uuid.uuid4().hex
    stage = root / 'staging'
    relocated = root / 'relocated'
    stage.mkdir(parents=True)
    shutil.copyfile(source, stage / source.name)
    shutil.copyfile(native, stage / native.name)
    project = drawing = model = None
    try:
        app.SilentOperation = True
        project = app.DesignProjectManager.DesignProjects.Add(36353, 'PortableDrawingTest', str(stage))
        project_file = Path(project.FullFileName)
        project.Activate(False)
        for directory in (stage, relocated):
            if directory == relocated:
                previous.Activate(False)
                project.Remove()  # unregister the test project, no disk deletion
                project = None
                shutil.copytree(stage, relocated)
                project = app.DesignProjectManager.DesignProjects.AddExisting(str(relocated / project_file.name))
                project.Activate(False)
            drawing = app.Documents.Open(str(directory / native.name), True)
            assert drawing.Sheets.Count == 1 and drawing.ActiveSheet.DrawingViews.Count == 4
            descriptors = list(drawing.ReferencedDocumentDescriptors)
            assert len(descriptors) == 1 and not descriptors[0].ReferenceMissing
            model = descriptors[0].ReferencedDocument
            resolved = Path(model.FullFileName).resolve()
            assert resolved == (directory / source.name).resolve(), (directory, resolved)
            assert not descriptors[0].ReferenceInternalNameDifferent
            assert model.ReferencedDocumentDescriptors.Count == 0
            print(json.dumps({'directory': str(directory), 'resolved_model': str(resolved), 'views': 4}), flush=True)
            drawing.Close(True)
            drawing = None
            model.Close(True)
            model = None
            assert app.Documents.Count == 0
        for directory in (stage, relocated):
            assert digest(directory / source.name) == originals[source]
            assert digest(directory / native.name) == originals[native]
    finally:
        if drawing:
            drawing.Close(True)
        if model:
            model.Close(True)
        assert app.Documents.Count == 0
        previous.Activate(False)
        if project:
            project.Remove()
        app.SilentOperation = silent
        assert app.DesignProjectManager.ActiveDesignProject.FullFileName == previous_path
        assert all(digest(path) == sha for path, sha in originals.items())
    print('Portable drawing relocation passed; original files and project preserved.', flush=True)


if __name__ == '__main__':
    main()
