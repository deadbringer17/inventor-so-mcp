namespace InventorXrSo.Core.Selection
{
    public enum SelectionKind { None, Occurrence, Face }

    public sealed class Selection
    {
        public static readonly Selection None = new Selection(SelectionKind.None, null, null, null);

        public Selection(SelectionKind kind, string occurrenceId, string faceId, string entityId)
        {
            Kind = kind;
            OccurrenceId = occurrenceId;
            FaceId = faceId;
            EntityId = entityId;
        }

        public SelectionKind Kind { get; }
        /// <summary>Occurrence picked (null in a part document).</summary>
        public string OccurrenceId { get; }
        /// <summary>Definition-local face id from the GLB (for the local highlight).</summary>
        public string FaceId { get; }
        /// <summary>Portable id in the active document (for Inventor); null until resolved.</summary>
        public string EntityId { get; }
    }
}
