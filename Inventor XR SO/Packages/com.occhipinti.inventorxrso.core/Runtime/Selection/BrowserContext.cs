using System.Collections.Generic;
using System.Linq;
using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Selection
{
    /// <summary>Local inspection context. Never enters Inventor's edit mode or changes the CAD.</summary>
    public sealed class BrowserContext
    {
        private readonly List<SceneNode> _path = new List<SceneNode>();
        public IReadOnlyList<SceneNode> Path => _path;
        public SceneNode Current => _path.LastOrDefault();
        public SceneGraph Graph { get; private set; }
        public void SetGraph(SceneGraph graph)
        {
            var ids = Graph?.DocumentId == graph?.DocumentId ? _path.Skip(1).Select(n => n.OccurrenceId).ToArray() : new string[0];
            Graph = graph;
            _targetMap = null; _targetMapFor = null;
            _path.Clear();
            if (graph == null) return;
            _path.Add(graph.Root);
            foreach (var id in ids)
            {
                var child = Current.Children.FirstOrDefault(n => n.OccurrenceId == id);
                if (child == null) break;
                _path.Add(child);
            }
        }
        public bool Enter(SceneNode node)
        {
            if (Current == null || !Current.Children.Contains(node) || node.Suppressed) return false;
            _path.Add(node);
            return true;
        }
        public void Back() { if (_path.Count > 1) _path.RemoveAt(_path.Count - 1); }
        public void GoTo(int index) { if (index >= 0 && index < _path.Count) _path.RemoveRange(index + 1, _path.Count - index - 1); }
        public SceneNode Find(string occurrenceId) => Graph == null ? null : Descendants(Graph.Root).FirstOrDefault(n => n.OccurrenceId == occurrenceId);
        // Occurrence id -> the node SelectionTarget answers with, built once per (graph, Current): a pick must not
        // re-walk every child's subtree (O(children x depth) with nested iterators).
        private Dictionary<string, SceneNode> _targetMap;
        private SceneNode _targetMapFor;

        public SceneNode SelectionTarget(string leafId)
        {
            if (Current == null) return null;
            if (leafId == null)   // nodes without an id all "match" a null id: keep the exact legacy answer
            {
                if (Current.DefinitionKind == "part") return Contains(Current, leafId) ? Current : null;
                return Current.Children.FirstOrDefault(child => !child.Suppressed && Contains(child, leafId));
            }
            if (!ReferenceEquals(_targetMapFor, Current) || _targetMap == null)
            {
                _targetMap = BuildTargetMap(Current);
                _targetMapFor = Current;
            }
            return _targetMap.TryGetValue(leafId, out var target) ? target : null;
        }

        private static Dictionary<string, SceneNode> BuildTargetMap(SceneNode current)
        {
            var map = new Dictionary<string, SceneNode>();
            if (current.DefinitionKind == "part") { AddSubtree(map, current, current); return map; }
            // First unsuppressed child in order wins when an id appears under several children.
            foreach (var child in current.Children)
                if (!child.Suppressed) AddSubtree(map, child, child);
            return map;
        }

        private static void AddSubtree(Dictionary<string, SceneNode> map, SceneNode root, SceneNode target)
        {
            var stack = new Stack<SceneNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node.OccurrenceId != null && !map.ContainsKey(node.OccurrenceId)) map[node.OccurrenceId] = target;
                foreach (var child in node.Children) stack.Push(child);
            }
        }
        public static bool Contains(SceneNode node, string occurrenceId) => Descendants(node).Any(n => n.OccurrenceId == occurrenceId);
        public static IEnumerable<SceneNode> Descendants(SceneNode node)
        {
            yield return node;
            foreach (var child in node.Children)
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
