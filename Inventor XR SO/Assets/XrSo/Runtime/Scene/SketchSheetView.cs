using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    public enum SketchSheetState { None, Sheet, Model }

    /// <summary>
    /// Tavolo da disegno: mette il piano di schizzo sul tavolo muovendo solo la radice della scena.
    /// Vista modello e Foglio sono pose della radice: i dati CAD non cambiano mai.
    /// </summary>
    public sealed class SketchSheetView : MonoBehaviour
    {
        public const float PenSnapMeters = 0.02f;
        private readonly PoseTween _tween = new PoseTween();
        private Transform _root;

        public SketchSheetState State { get; private set; }
        public bool Tweening => _tween.Active;
        public Transform Root => _root;

        public void Bind(Transform sceneRoot) => _root = sceneRoot;

        /// <summary>Foglio: applica SketchSheetLayout (la specchiatura X e gia nel layout).</summary>
        public void Enter(SketchFrame frame, double extentWidthMm, double extentHeightMm, WorkbenchFrame bench)
        {
            if (_root == null) return;
            var pose = SketchSheetLayout.Compute(frame, extentWidthMm, extentHeightMm, bench);
            _tween.Start(_root, new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z),
                new Quaternion((float)pose.Qx, (float)pose.Qy, (float)pose.Qz, (float)pose.Qw), (float)pose.Scale);
            State = SketchSheetState.Sheet;
        }

        /// <summary>Vista modello: la posa di piano (Workbench) al posto del foglio.</summary>
        public void ShowModel(LayoutPose pose)
        {
            if (_root == null || State == SketchSheetState.None) return;
            _tween.Start(_root, new Vector3((float)pose.Position.X, (float)pose.Position.Y, (float)pose.Position.Z),
                Quaternion.Euler(0, (float)pose.YawDegrees, 0), (float)pose.Scale);
            State = SketchSheetState.Model;
        }

        public void ShowSheet(SketchFrame frame, double extentWidthMm, double extentHeightMm, WorkbenchFrame bench)
            => Enter(frame, extentWidthMm, extentHeightMm, bench);

        public void Exit() { _tween.Snap(); State = SketchSheetState.None; }

        public void Snap() => _tween.Snap();

        private void Update() => _tween.Tick(Time.unscaledDeltaTime);

        /// <summary>
        /// Penna: la punta entro 2 cm dal piano di schizzo si proietta sul piano; altrimenti vale il raggio.
        /// False se il raggio non incontra il piano.
        /// </summary>
        public bool ProjectPen(Vector3 tipWorld, Ray ray, SketchFrame frame, out Vector2 sketchMm)
        {
            sketchMm = default;
            if (_root == null) return false;
            var tipMm = CadCoordinates.FromWorld(_root, tipWorld);
            double planeMm = (tipMm - frame.OriginMm).Dot(frame.Normal);
            double planeWorld = System.Math.Abs(planeMm) * 0.001 * _root.lossyScale.x;
            if (planeWorld <= PenSnapMeters)
            {
                var s = frame.ToSketch(tipMm);
                sketchMm = new Vector2((float)s.X, (float)s.Y);
                return true;
            }
            if (!CadCoordinates.SketchRay(_root, frame, ray, out var hit)) return false;
            sketchMm = new Vector2((float)hit.X, (float)hit.Y);
            return true;
        }
    }
}
