using System.Collections.Generic;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>One placed part occurrence (or the part itself in a part document).</summary>
    public sealed class CadInstance : MonoBehaviour
    {
        private readonly List<CadBody> _bodies = new List<CadBody>();

        public string OccurrenceId { get; private set; }
        public string DefinitionId { get; private set; }
        public IReadOnlyList<CadBody> Bodies => _bodies;

        public void Init(string occurrenceId, string definitionId)
        {
            OccurrenceId = occurrenceId;
            DefinitionId = definitionId;
        }

        internal void Add(CadBody body) => _bodies.Add(body);
    }
}
