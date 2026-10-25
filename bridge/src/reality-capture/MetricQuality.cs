namespace Inventor.So.RealityCapture;

public sealed record ConstraintResidual(string Id, string Role, string Source, double? MeasuredMm, double KnownMm,
    double UncertaintyMm, double? ResidualMm, double? NormalizedResidual, string Status)
{
    public double? AbsoluteErrorMm=>ResidualMm.HasValue?Math.Abs(ResidualMm.Value):null;
    public double? RelativeError=>ResidualMm/KnownMm;
}
public sealed record MetricQuality(string AccuracyStatus, double? CalibrationRmsMm, double? CalibrationMaxMm,
    double? ValidationRmsMm, double? ValidationP95Mm, int IndependentValidationCount, bool Conflicts,
    ConstraintResidual[] Constraints, string AccuracyClaim);

public static class MetricQualityEvaluator
{
    public static double Measure(MetricConstraint constraint,IReadOnlyDictionary<string,V3> pointsMm)
    {
        var p = constraint.PointIds.Select(id => pointsMm.TryGetValue(id,out var value) ? value : throw new CaptureSolveException("CAPTURE_REFERENCE_NOT_FOUND")).ToArray();
        if (p.Any(v=>!double.IsFinite(v.X)||!double.IsFinite(v.Y)||!double.IsFinite(v.Z))) throw new CaptureSolveException("CAPTURE_DEGENERATE_GEOMETRY");
        if (constraint.Type == "distance")
        {
            double distance=(p[1]-p[0]).Norm;
            return double.IsFinite(distance) && distance>1e-9 ? distance : throw new CaptureSolveException("CAPTURE_DEGENERATE_GEOMETRY");
        }
        double a = (p[1]-p[0]).Norm, b = (p[2]-p[1]).Norm, c = (p[0]-p[2]).Norm;
        double cross = V3.Cross(p[1]-p[0],p[2]-p[0]).Norm;
        if (cross < 1e-6 || cross/(a*c) < 0.01) throw new CaptureSolveException("CAPTURE_DEGENERATE_GEOMETRY");
        return a*b*c/cross; // circumdiameter, never a cylinder inferred from three arbitrary surface points
    }

    public static MetricQuality Evaluate(IEnumerable<MetricConstraint> constraints,IReadOnlyDictionary<string,V3> pointsMm,
        bool synthetic, bool converged, bool physicalEvidence = false)
    {
        var all = constraints.ToArray();
        foreach (var c in all) c.Validate();
        var residuals = all.Select(c =>
        {
            if (!c.Enabled) return new ConstraintResidual(c.Id,c.Role,c.Source,null,c.ValueMm,c.UncertaintyMm,null,null,"disabled");
            try
            {
                double measured = Measure(c,pointsMm), residual = measured-c.ValueMm;
                return new ConstraintResidual(c.Id,c.Role,c.Source,measured,c.ValueMm,c.UncertaintyMm,residual,residual/c.UncertaintyMm,
                    Math.Abs(residual/c.UncertaintyMm)>3 ? "conflict" : "consistent");
            }
            catch (CaptureSolveException) { return new ConstraintResidual(c.Id,c.Role,c.Source,null,c.ValueMm,c.UncertaintyMm,null,null,"unresolved"); }
        }).ToArray();
        var calibration = residuals.Where(r => r.Role == "calibration" && r.Status != "disabled").ToArray();
        bool conflicts = calibration.Any(r => r.Status is "conflict" or "unresolved");
        // Measurements used for calibration can never be counted a second time as independent validation.
        string GeometryKey(MetricConstraint c) => c.Type + ":" + string.Join("|",c.PointIds.OrderBy(p => p,StringComparer.Ordinal));
        var calibrationKeys = all.Where(c => c.Enabled && c.Role == "calibration").Select(GeometryKey).ToHashSet(StringComparer.Ordinal);
        var calibrationMeasurements=all.Where(c=>c.Enabled && c.Role=="calibration" && c.MeasurementId!=null).Select(c=>c.MeasurementId).ToHashSet(StringComparer.Ordinal);
        var independent = all.Where(c => c.Enabled && c.Role == "validation" && c.Independent && c.MeasurementId!=null &&
                !calibrationMeasurements.Contains(c.MeasurementId) && !calibrationKeys.Contains(GeometryKey(c)))
            .GroupBy(GeometryKey).Select(g => g.First().Id).ToHashSet(StringComparer.Ordinal);
        var validation = residuals.Where(r => independent.Contains(r.Id) && r.ResidualMm.HasValue).Select(r => r.ResidualMm!.Value).ToArray();
        int independentCount=all.Where(c=>independent.Contains(c.Id) && residuals.Any(r=>r.Id==c.Id && r.ResidualMm.HasValue))
            .Select(c=>c.MeasurementId).Distinct(StringComparer.Ordinal).Count();
        var calValues = calibration.Where(r => r.ResidualMm.HasValue).Select(r => r.ResidualMm!.Value).ToArray();
        string status = "METRIC";
        if (converged && calibration.Length > 0 && !conflicts) status = "CALIBRATED";
        if (physicalEvidence && !synthetic && status == "CALIBRATED" && independentCount >= 3 && validation.All(double.IsFinite)) status = "VALIDATED";
        return new MetricQuality(status,Rms(calValues),calValues.Length == 0 ? null : calValues.Max(Math.Abs),Rms(validation),
            Percentile95(validation.Select(Math.Abs).ToArray()),independentCount,conflicts,residuals,
            synthetic ? "SYNTHETIC: mathematical/software evidence only; no physical accuracy claim" :
            status == "VALIDATED" ? "Compared with declared independent control measurements; not metrological certification" : "No independently measured dimensional accuracy claim");
    }
    private static double? Rms(double[] values) => values.Length == 0 ? null : Math.Sqrt(values.Select(v => v*v).Average());
    private static double? Percentile95(double[] values)
    {
        if (values.Length == 0) return null;
        Array.Sort(values); double index = (values.Length-1)*0.95; int low=(int)index;
        return values[low]+(values[Math.Min(low+1,values.Length-1)]-values[low])*(index-low);
    }
}
