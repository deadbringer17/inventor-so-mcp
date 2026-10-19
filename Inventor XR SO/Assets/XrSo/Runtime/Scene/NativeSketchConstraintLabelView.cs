using System;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>A bounded label on the sketch plane. View state only; not a desktop-equivalent glyph.</summary>
    public sealed class NativeSketchConstraintLabelView : MonoBehaviour, IDisposable
    {
        private Transform _model;
        private SketchFrame _frame;
        private CadPoint _anchor;
        private Canvas _canvas;
        private RectTransform _rect;
        private TextMeshProUGUI _text;
        private readonly Vector3[] _corners = new Vector3[4];
        private bool _disposed, _usable;
        public string FullText { get; private set; }
        public Vector2 PhysicalSizeMm { get; private set; }
        public float PaddingMm { get; private set; }
        public bool IsUsable => !_disposed && _usable && _canvas != null && _canvas.enabled && gameObject.activeInHierarchy;

        public static NativeSketchConstraintLabelView Create(Transform parent, Transform model, SketchFrame frame,
            CadPoint anchor, string text, Vector2? physicalSizeMm = null, float paddingMm = 2, float capHeightMm = 7)
        {
            if(parent == null) throw new ArgumentNullException(nameof(parent));
            if(model == null) throw new ArgumentNullException(nameof(model));
            if(frame == null) throw new ArgumentNullException(nameof(frame));
            Validate(text,physicalSizeMm ?? new Vector2(48,12),paddingMm,capHeightMm);
            var canvas = UiFactory.WorldCanvas(parent,"Native constraint label",physicalSizeMm ?? new Vector2(48,12));
            var view = canvas.gameObject.AddComponent<NativeSketchConstraintLabelView>();
            view._canvas = canvas; view._rect = (RectTransform)canvas.transform; view._model = model; view._frame = frame; view._anchor = anchor;
            var background = UiFactory.Panel(canvas.transform,"Constraint label background",UiTheme.Navy);
            // A fully filled rectangle makes the measurable hit rectangle match the visible background.
            var backgroundImage = background.GetComponent<Image>(); backgroundImage.sprite = null; backgroundImage.type = Image.Type.Simple;
            UiFactory.Stretch(background);
            canvas.gameObject.AddComponent<RectMask2D>();
            view._text = UiFactory.Text(canvas.transform,"",capHeightMm);
            view._text.alignment = TextAlignmentOptions.Center;
            view._text.textWrappingMode = TextWrappingModes.NoWrap;
            view._text.overflowMode = TextOverflowModes.Ellipsis;
            view._text.raycastTarget = false; view._text.richText = false;
            UiFactory.Stretch(view._text.rectTransform);
            view.SetContent(text,physicalSizeMm ?? new Vector2(48,12),paddingMm,capHeightMm);
            return view;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static void Validate(string text,Vector2 size,float padding,float capHeight)
        {
            if(text == null || text.Length > 2048) throw new ArgumentException("Constraint label text must contain at most 2048 characters.",nameof(text));
            if(!Finite(size.x) || !Finite(size.y) || size.x <= 0 || size.y <= 0 || size.x > 500 || size.y > 500)
                throw new ArgumentOutOfRangeException(nameof(size));
            if(!Finite(padding) || padding < 0 || 2*padding >= Mathf.Min(size.x,size.y)) throw new ArgumentOutOfRangeException(nameof(padding));
            if(!Finite(capHeight) || capHeight <= 0 || capHeight > size.y-2*padding) throw new ArgumentOutOfRangeException(nameof(capHeight));
        }

        public void SetContent(string text,Vector2 physicalSizeMm,float paddingMm = 2,float capHeightMm = 7)
        {
            if(_disposed) throw new ObjectDisposedException(nameof(NativeSketchConstraintLabelView));
            Validate(text,physicalSizeMm,paddingMm,capHeightMm);
            FullText = text; PhysicalSizeMm = physicalSizeMm; PaddingMm = paddingMm;
            _rect.sizeDelta = physicalSizeMm;
            // Preserve the complete label separately; embedded newlines cannot escape the one-line footprint.
            _text.text = text.Replace('\r',' ').Replace('\n',' ');
            _text.fontSize = capHeightMm/UiTypography.CapRatio(_text.font);
            _text.rectTransform.offsetMin = Vector2.one*paddingMm;
            _text.rectTransform.offsetMax = -Vector2.one*paddingMm;
            Refresh();
        }

        public void SetAnchor(SketchFrame frame,CadPoint anchor)
        {
            if(_disposed) throw new ObjectDisposedException(nameof(NativeSketchConstraintLabelView));
            _frame = frame ?? throw new ArgumentNullException(nameof(frame)); _anchor = anchor; Refresh();
        }

        /// <summary>Updates transforms only; never follows a head pose or rebuilds UI objects.</summary>
        public void Refresh()
        {
            _usable = false;
            if(_disposed || _model == null || transform.parent == null) { if(_canvas != null) _canvas.enabled = false; return; }
            var origin = CadCoordinates.ToLocal(_frame.ToModel(_anchor));
            var right = _model.TransformVector(CadCoordinates.ToLocal(_frame.XAxis));
            var up = _model.TransformVector(CadCoordinates.ToLocal(_frame.YAxis));
            if(!Finite(right) || !Finite(up) || !Finite(right.sqrMagnitude) || !Finite(up.sqrMagnitude)
                || right.sqrMagnitude < 1e-16f || up.sqrMagnitude < 1e-16f)
            { _canvas.enabled = false; return; }
            // Unity's Vector3.normalized returns zero for short but valid vectors. CAD axes
            // converted from mm can be below that threshold, especially after model scaling.
            right /= Mathf.Sqrt(right.sqrMagnitude); up /= Mathf.Sqrt(up.sqrMagnitude);
            var normal = Vector3.Cross(right,up);
            if(!Finite(normal) || normal.sqrMagnitude < 1e-12f) { _canvas.enabled = false; return; }
            normal /= Mathf.Sqrt(normal.sqrMagnitude);
            transform.SetPositionAndRotation(_model.TransformPoint(origin),Quaternion.LookRotation(normal,up));
            var parent = transform.parent;
            float xScale = parent.TransformVector(transform.localRotation*Vector3.right).magnitude;
            float yScale = parent.TransformVector(transform.localRotation*Vector3.up).magnitude;
            float zScale = parent.TransformVector(transform.localRotation*Vector3.forward).magnitude;
            if(!Finite(xScale) || !Finite(yScale) || !Finite(zScale) || Mathf.Min(xScale,Mathf.Min(yScale,zScale)) < 1e-8f)
            { _canvas.enabled = false; return; }
            transform.localScale = new Vector3(.001f/xScale,.001f/yScale,.001f/zScale);
            _rect.GetWorldCorners(_corners);
            right = _corners[3]-_corners[0]; up = _corners[1]-_corners[0];
            // A sheared parent can turn the rect into a parallelogram: refuse instead of claiming a rectangular hit target.
            _usable = Finite(_corners[0]) && Finite(_corners[2]) && right.sqrMagnitude > 1e-12f && up.sqrMagnitude > 1e-12f
                && Mathf.Abs(Vector3.Dot(right/Mathf.Sqrt(right.sqrMagnitude),up/Mathf.Sqrt(up.sqrMagnitude))) < .0001f;
            _canvas.enabled = _usable;
        }

        /// <summary>Actual displayed rectangle projected into sketch coordinates; conservative AABB in CAD mm.</summary>
        public bool TryGetSketchFootprint(out CadPoint minimum,out CadPoint maximum)
        {
            minimum = maximum = default; Refresh(); if(!IsUsable) return false;
            _rect.GetWorldCorners(_corners);
            var first = _frame.ToSketch(CadCoordinates.FromWorld(_model,_corners[0]));
            double minX = first.X, maxX = first.X, minY = first.Y, maxY = first.Y;
            for(int i=1;i<4;i++)
            {
                var point = _frame.ToSketch(CadCoordinates.FromWorld(_model,_corners[i]));
                minX = Math.Min(minX,point.X); maxX = Math.Max(maxX,point.X); minY = Math.Min(minY,point.Y); maxY = Math.Max(maxY,point.Y);
            }
            minimum = new CadPoint(minX,minY); maximum = new CadPoint(maxX,maxY); return true;
        }

        /// <summary>Explicit query only. No collider, event handler or raycast target intercepts input.</summary>
        public bool TryHit(Ray ray,out float distanceMetres,out Vector3 worldPoint)
        {
            distanceMetres = 0; worldPoint = default; Refresh();
            if(!IsUsable || !Finite(ray.origin) || !Finite(ray.direction) || ray.direction.sqrMagnitude < 1e-12f) return false;
            _rect.GetWorldCorners(_corners);
            var right = _corners[3]-_corners[0]; var up = _corners[1]-_corners[0];
            var normal = Vector3.Cross(right/Mathf.Sqrt(right.sqrMagnitude),up/Mathf.Sqrt(up.sqrMagnitude));
            normal /= Mathf.Sqrt(normal.sqrMagnitude);
            var direction = ray.direction/Mathf.Sqrt(ray.direction.sqrMagnitude);
            float denominator = Vector3.Dot(direction,normal);
            if(Mathf.Abs(denominator) < .000001f) return false;
            float distance = Vector3.Dot(_corners[0]-ray.origin,normal)/denominator;
            if(!Finite(distance) || distance <= .000001f) return false;
            var point = ray.origin+direction*distance; var offset = point-_corners[0];
            float x = Vector3.Dot(offset,right)/right.sqrMagnitude, y = Vector3.Dot(offset,up)/up.sqrMagnitude;
            if(x < 0 || x > 1 || y < 0 || y > 1) return false;
            distanceMetres = distance; worldPoint = point; return true;
        }

        private void LateUpdate() => Refresh();
        public void Dispose()
        {
            if(_disposed) return; _disposed = true; _usable = false; _canvas.enabled = false;
            gameObject.SetActive(false);
            if(Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }
    }
}
