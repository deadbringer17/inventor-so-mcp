using System;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    public sealed partial class NativeSketchEditorUi
    {
        private Action<CadPoint> _coordinatePick;
        private Action<CadPoint> _coordinateMove;
        private Action _coordinatePause;
        private NativeSketchSnapshot _coordinateSnapshot;
        private int _coordinateGeneration;
        public bool CoordinatePickActive=>_coordinatePick!=null && PromptOpen && !_disposed && _ready() && Session.CanEdit
            && _coordinateGeneration==_promptGeneration && ReferenceEquals(_coordinateSnapshot,Session.Snapshot);
        private void ArmCoordinatePick(NativeSketchSnapshot snapshot,int generation,Action<CadPoint> accept,Action<CadPoint> move=null,Action pause=null)
        {_coordinateSnapshot=snapshot;_coordinateGeneration=generation;_coordinatePick=accept;_coordinateMove=move;_coordinatePause=pause;}
        private void ResetCoordinatePick()
        {_coordinatePick=null;_coordinateMove=null;_coordinatePause=null;_coordinateSnapshot=null;HideCoordinateCursor();}
        private void CloseCoordinateField()
        {_palette()?.HideKeypad();_prompt?.CancelEdit();_prompt=null;ResetCoordinatePick();}
        public void HideCoordinateCursor() {_coordinatePause?.Invoke();_geometry()?.Cursor(null,default,null,SketchShape.Line);}
        public void CoordinateRay(Transform model,Ray ray,bool overUi)
        {
            if(!CoordinatePickActive || overUi || !CadCoordinates.SketchRay(model,_coordinateSnapshot.Frame,ray,out var point)) {HideCoordinateCursor();return;}
            if(Math.Abs(point.X)>100000 || Math.Abs(point.Y)>100000) {HideCoordinateCursor();return;}
            if(_coordinateMove!=null) _coordinateMove(point);
            else _geometry()?.Cursor(_coordinateSnapshot.Frame,point,null,SketchShape.Line);
        }
        private bool PickCoordinate(CadPoint point)
        {
            if(!CoordinatePickActive) return false;
            if(Math.Abs(point.X)>100000 || Math.Abs(point.Y)>100000) {_notice("Il punto supera il limite di ±100.000 mm; usa un punto nel campo consentito.");return true;}
            var accept=_coordinatePick;accept(point);return true;
        }
    }
}
