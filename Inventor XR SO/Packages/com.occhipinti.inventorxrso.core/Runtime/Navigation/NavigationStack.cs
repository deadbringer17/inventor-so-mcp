using System;
using System.Collections.Generic;
using System.Linq;

namespace InventorXrSo.Core.Navigation
{
    public enum DocContext { Assembly, Part, SheetMetal }

    /// <summary>Un livello della pila di navigazione: il documento in cui si sta lavorando.</summary>
    public sealed class NavLevel
    {
        public NavLevel(string documentId, DocContext context, string name,
            string fromOccurrenceId = null, float[] occurrencePose = null)
        {
            if (string.IsNullOrWhiteSpace(documentId)) throw new ArgumentException("id documento vuoto");
            DocumentId = documentId; Context = context; Name = name ?? documentId;
            FromOccurrenceId = fromOccurrenceId; OccurrencePose = occurrencePose;
        }

        public string DocumentId { get; }
        public DocContext Context { get; }
        public string Name { get; }
        /// <summary>Occorrenza dell'assieme padre da cui si è entrati; null alla radice.</summary>
        public string FromOccurrenceId { get; }
        /// <summary>16 float, posa nell'assieme padre; null alla radice.</summary>
        public float[] OccurrencePose { get; }
        /// <summary>Modifiche non salvate («●» nel percorso).</summary>
        public bool Dirty { get; set; }
    }

    /// <summary>Pila Assieme › Sub › Parte. Si ricarica dal Quest a ogni cambio di documento.</summary>
    public sealed class NavigationStack
    {
        private readonly List<NavLevel> _levels = new List<NavLevel>();

        public IReadOnlyList<NavLevel> Levels => _levels;
        public NavLevel Top => _levels.Count > 0 ? _levels[_levels.Count - 1] : null;
        public NavLevel Parent => _levels.Count > 1 ? _levels[_levels.Count - 2] : null;
        public bool CanPop => _levels.Count > 1;

        public string Breadcrumb =>
            string.Join(" › ", _levels.Select(l => l.Dirty ? l.Name + " ●" : l.Name));

        public event Action Changed;

        public void Reset(NavLevel root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            _levels.Clear();
            _levels.Add(root);
            Changed?.Invoke();
        }

        public void Push(NavLevel level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            _levels.Add(level);
            Changed?.Invoke();
        }

        public NavLevel Pop()
        {
            if (!CanPop) throw new InvalidOperationException("la pila ha solo la radice");
            var top = _levels[_levels.Count - 1];
            _levels.RemoveAt(_levels.Count - 1);
            Changed?.Invoke();
            return top;
        }

        /// <summary>Tiene Levels[0..index].</summary>
        public void PopTo(int index)
        {
            if (index < 0 || index >= _levels.Count) throw new ArgumentOutOfRangeException(nameof(index));
            _levels.RemoveRange(index + 1, _levels.Count - index - 1);
            Changed?.Invoke();
        }
    }
}
