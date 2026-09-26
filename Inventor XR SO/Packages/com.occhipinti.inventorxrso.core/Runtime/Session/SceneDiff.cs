using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Session
{
    public sealed class SceneDiff
    {
        private SceneDiff(bool documentChanged, bool geometryChanged)
        {
            DocumentChanged = documentChanged;
            GeometryChanged = geometryChanged;
        }

        public bool DocumentChanged { get; }
        public bool GeometryChanged { get; }
        public bool NeedsReload => DocumentChanged || GeometryChanged;

        public static SceneDiff Compare(DocumentState before, DocumentState after)
        {
            if (before == null) return new SceneDiff(true, true);
            bool document = before.DocumentId != after.DocumentId;
            return new SceneDiff(document, document || before.VisualRevision != after.VisualRevision);
        }
    }
}
