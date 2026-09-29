using System;
using InventorXrSo.Core.Backend;

namespace InventorXrSo.Core.Ui
{
    /// <summary>Frame della postazione seduta: posizione e yaw della testa al Ricentra, altezza del piano.</summary>
    public sealed class WorkbenchFrame
    {
        public const double DefaultDeskDrop = 0.45;

        private WorkbenchFrame(CadPoint origin, double yaw, double deskY) { Origin = origin; YawDegrees = yaw; DeskY = deskY; }

        public CadPoint Origin { get; }
        public double YawDegrees { get; }
        public double DeskY { get; }
        public CadPoint Forward { get { double r = YawDegrees * Math.PI / 180; return new CadPoint(Math.Sin(r), 0, Math.Cos(r)); } }
        public CadPoint Right { get { double r = YawDegrees * Math.PI / 180; return new CadPoint(Math.Cos(r), 0, -Math.Sin(r)); } }

        /// <summary>Metri, Y verso l'alto. Pitch e roll della testa sono ignorati di proposito.</summary>
        public static WorkbenchFrame FromHead(CadPoint headPosition, double headYawDegrees, double? calibratedDeskY)
            => new WorkbenchFrame(headPosition, headYawDegrees, calibratedDeskY ?? headPosition.Y - DefaultDeskDrop);
    }

    public readonly struct LayoutPose
    {
        public LayoutPose(CadPoint position, double yawDegrees, double scale, bool clamped)
        { Position = position; YawDegrees = yawDegrees; Scale = scale; Clamped = clamped; }
        public CadPoint Position { get; }
        public double YawDegrees { get; }
        public double Scale { get; }
        /// <summary>True se la scala ideale era fuori da [MinScale, MaxScale]: l'HUD mostra la scala.</summary>
        public bool Clamped { get; }
    }

    /// <summary>Pose di piano, foglio, assieme e isolamento (spec M6, disposizione spaziale).</summary>
    public static class WorkbenchLayout
    {
        public const double PartDistance = 0.40, PartBox = 0.40;
        public const double AssemblyDistance = 0.65, AssemblyDrop = 0.15, AssemblyBox = 0.80;
        public const double SheetWidth = 0.45, SheetDepth = 0.30;
        public const double MinScale = 0.001, MaxScale = 10;

        public static double FitScale(double extentM, double boxM, out bool clamped)
        {
            clamped = false;
            if (!(extentM > 0)) return 1;
            double raw = boxM / extentM;
            double s = Math.Max(MinScale, Math.Min(MaxScale, raw));
            clamped = s != raw;
            return s;
        }

        public static LayoutPose Part(WorkbenchFrame f, double extentM)
        {
            double s = FitScale(extentM, PartBox, out bool c);
            return new LayoutPose(OnFloor(f, PartDistance, f.DeskY), f.YawDegrees, s, c);
        }

        public static LayoutPose Assembly(WorkbenchFrame f, double extentM)
        {
            double s = FitScale(extentM, AssemblyBox, out bool c);
            return new LayoutPose(OnFloor(f, AssemblyDistance, f.Origin.Y - AssemblyDrop), f.YawDegrees, s, c);
        }

        /// <summary>Solo visivo: il componente avanza a meta strada verso la testa, stessa scala dell'assieme.</summary>
        public static LayoutPose Isolated(WorkbenchFrame f, CadPoint componentPosition, double componentScale)
            => new LayoutPose((componentPosition + f.Origin) * 0.5, f.YawDegrees, componentScale, false);

        public static double SheetScale(double widthM, double depthM)
        {
            if (!(widthM > 0) || !(depthM > 0)) return 1;
            return Math.Max(MinScale, Math.Min(MaxScale, Math.Min(SheetWidth / widthM, SheetDepth / depthM)));
        }

        public static CadPoint CommitBarPosition(WorkbenchFrame f) => OnFloor(f, PartDistance - SheetDepth / 2 - 0.05, f.DeskY);

        private static CadPoint OnFloor(WorkbenchFrame f, double distance, double y)
        {
            var p = f.Origin + f.Forward * distance;
            return new CadPoint(p.X, y, p.Z);
        }
    }
}
