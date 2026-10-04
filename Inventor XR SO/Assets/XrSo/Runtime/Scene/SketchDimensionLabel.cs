using TMPro;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Ui;
using UnityEngine;

namespace InventorXrSo.Unity.Scene
{
    /// <summary>Model-anchored CAD text with constant display size and no input interception.</summary>
    public sealed class SketchDimensionLabel : MonoBehaviour
    {
        private Transform _head, _model;
        public static SketchDimensionLabel Create(Transform parent,Transform model,Transform head,CadPoint anchor,string text)
        {
            var canvas=UiFactory.WorldCanvas(parent,"Sketch dimension",new Vector2(320,44));
            canvas.transform.localPosition=CadCoordinates.ToLocal(anchor);
            var label=UiFactory.Label(canvas.transform,text.Length>128 ? text.Substring(0,125)+"…" : text,24);
            label.alignment=TextAlignmentOptions.Center; label.raycastTarget=false; UiFactory.Stretch(label.rectTransform);
            var view=canvas.gameObject.AddComponent<SketchDimensionLabel>(); view._head=head; view._model=model;
            view.Refresh(); return view;
        }
        public void Refresh()
        {
            if (_model==null) return;
            float scale=Mathf.Max(0.0001f,Mathf.Abs(_model.lossyScale.x));
            transform.localScale=Vector3.one*(0.001f/scale);
            if (_head!=null) transform.rotation=_head.rotation;
        }
        private void LateUpdate() => Refresh();
    }
}
