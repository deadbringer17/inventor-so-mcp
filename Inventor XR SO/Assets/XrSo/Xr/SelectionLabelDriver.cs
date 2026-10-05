using System;
using InventorXrSo.Core.Navigation;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>What the app currently has selected, as the label needs it. Kind None means nothing to show.</summary>
    public struct SelectionTarget
    {
        public SelectionLabelKind Kind;
        public string Name;
        /// <summary>Face number (1-based) for <see cref="SelectionLabelKind.Face"/>; 0 when unknown.</summary>
        public int FaceOrdinal;
        /// <summary>World bounds of the highlight; without them the label stays hidden.</summary>
        public bool HasBounds;
        public Bounds Bounds;
    }

    /// <summary>
    /// Feeds <see cref="SelectionLabel"/> from whatever selection source is active (the app supplies it as a delegate): shows it
    /// when there is a selection with bounds and the model is on screen, hides it otherwise. Texts are rebuilt only when the
    /// target changes; while it is unchanged only the bounds follow (isolation tween, placement), so no per-frame allocation.
    /// </summary>
    public sealed class SelectionLabelDriver
    {
        private readonly SelectionLabel _label;
        private readonly NavigationStack _navigation;
        private readonly Func<SelectionTarget> _source;
        private SelectionLabelKind _kind; private string _name; private int _ordinal = -1, _levels = -1;

        public SelectionLabelDriver(SelectionLabel label, NavigationStack navigation, Func<SelectionTarget> source)
        {
            _label = label ?? throw new ArgumentNullException(nameof(label));
            _navigation = navigation;
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Level text: the active document's depth in the navigation path and the depth including the selected object when it is a
        /// component that can be entered («Livello 1/2» for a part selected in the root assembly, «Livello 3/3» for a face of the
        /// part opened below a sub-assembly). Empty without a path.
        /// </summary>
        public static string LevelFor(SelectionLabelKind kind, int pathLength)
        {
            if (pathLength < 1) return "";
            int total = pathLength + (kind == SelectionLabelKind.Part || kind == SelectionLabelKind.Subassembly ? 1 : 0);
            return SelectionLabelText.Level(pathLength, total);
        }

        public void Tick()
        {
            var target = _source();
            if (target.Kind == SelectionLabelKind.None || !target.HasBounds) { Reset(); _label.Hide(); return; }
            int levels = _navigation == null ? 0 : _navigation.Levels.Count;
            if (_label.Visible && target.Kind == _kind && target.Name == _name && target.FaceOrdinal == _ordinal && levels == _levels)
            {
                _label.SetBounds(target.Bounds);
                return;
            }
            _kind = target.Kind; _name = target.Name; _ordinal = target.FaceOrdinal; _levels = levels;
            _label.Show(target.Name, SelectionLabelText.Kind(target.Kind, target.FaceOrdinal), LevelFor(target.Kind, levels), target.Bounds);
        }

        private void Reset() { _kind = SelectionLabelKind.None; _name = null; _ordinal = -1; _levels = -1; }
    }
}
