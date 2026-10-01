using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>Postazione seduta: frame dalla testa (solo yaw) e posa del modello sul piano di lavoro con transizione ~250 ms.</summary>
    public sealed class Workbench : MonoBehaviour
    {
        private readonly PoseTween _tween = new PoseTween();
        private Transform _root;
        private double _extentM;
        private double? _deskY;

        public WorkbenchFrame Frame { get; private set; }
        public LayoutPose PartPose { get; private set; }
        /// <summary>True se la scala ideale e fuori da [0,001; 10]: l'HUD mostra la scala.</summary>
        public bool Clamped => PartPose.Clamped;
        public bool Tweening => _tween.Active;

        /// <summary>Altezza del piano calibrata (metri); null = testa - 0,45 m. Vale dal prossimo Recenter.</summary>
        public void SetDeskHeight(double? deskY) => _deskY = deskY;

        public WorkbenchFrame Recenter(Transform head)
        {
            Frame = WorkbenchFrame.FromHead(new CadPoint(head.position.x, head.position.y, head.position.z), head.eulerAngles.y, _deskY);
            if (_root != null) Apply();
            return Frame;
        }

        /// <summary>Porta il modello sul piano alla scala che entra nel riquadro.</summary>
        public void ApplyPart(Transform sceneRoot, double extentM)
        {
            _root = sceneRoot;
            _extentM = extentM;
            if (Frame != null && _root != null) Apply();
        }

        public void Fit() { if (Frame != null && _root != null) Apply(); }

        public void Snap() => _tween.Snap();

        /// <summary>Lascia la radice della scena: nessun'altra transizione la sposta (uscita dal workspace).</summary>
        public void Release()
        {
            _tween.Cancel();
            _root = null;
        }

        private void Apply()
        {
            PartPose = WorkbenchLayout.Part(Frame, _extentM);
            _tween.Start(_root, new Vector3((float)PartPose.Position.X, (float)PartPose.Position.Y, (float)PartPose.Position.Z),
                Quaternion.Euler(0, (float)PartPose.YawDegrees, 0), (float)PartPose.Scale);
        }

        private void Update() => _tween.Tick(Time.unscaledDeltaTime);
    }
}
