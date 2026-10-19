using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Inventor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using File = System.IO.File;
using Path = System.IO.Path;

internal static class Program
{
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    private sealed class ResearchFailure : InvalidOperationException { internal ResearchFailure(string message) : base(message) { } }
    private const string FixturePrefix = "S1_Skeleton57_";
    private static JObject? _activeReport;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] != "--allow-existing-inventor") return 64;
        var report = new JObject { ["captured_utc"] = DateTime.UtcNow.ToString("O"), ["schema"] = 1,
            ["evidence"] = "Standalone Inventor 2027 native API research on exclusively owned Part/Sheet documents. No product tool, HTTP, desktop-command equivalence, Unity, Quest or physical proof.",
            ["make_part"] = "NOT COVERED PartMakePartCmd", ["make_components"] = "NOT COVERED PartMakeComponentsCmd",
            ["desktop_defaults"] = "NOT COVERED; all research options are explicit inputs, not inferred UI defaults.",
            ["interop"] = InteropEvidence() };
        _activeReport = report;
        var existing = ExistingProcesses(); report["existing_processes_before"] = ProcessRows(existing);
        Inventor.Application? app = null; PartDocument? doc = null; int ownedPid = 0, exit = 1; bool canQuit = false;
        string scratch = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "s1-skeleton57-" + Guid.NewGuid().ToString("N")));
        try
        {
            if (existing.Count != 0 && args.Length != 2) throw new ResearchFailure("Existing Inventor requires explicit isolated-process mode.");
            Directory.CreateDirectory(scratch);
            Phase("create_owned_process");
            app = (Inventor.Application)Activator.CreateInstance(Type.GetTypeFromProgID("Inventor.Application")!)!;
            if (GetWindowThreadProcessId(new IntPtr(unchecked((long)(uint)app.MainFrameHWND)), out uint pid) == 0
                || pid == 0 || existing.ContainsKey(checked((int)pid)))
                throw new ResearchFailure("COM did not return a provably distinct process; existing process left untouched.");
            ownedPid = checked((int)pid); report["owned_pid"] = ownedPid;
            // No Documents access, Visible write, fixture operation or Quit before distinct PID proof.
            if (app.Documents.Count != 0) throw new ResearchFailure("New process is not empty; its documents will not be modified or closed.");
            canQuit = true; app.Visible = true; report["application_version"] = app.SoftwareVersion.DisplayVersion;
            var contexts = new JArray(); report["contexts"] = contexts;
            foreach (bool sheet in new[] { false, true })
            {
                string kind = sheet ? "sheetmetal" : "part"; Phase(kind + "_create_fixture");
                if (app.Documents.Count != 0) throw new ResearchFailure("Owned process contains unexpected documents before fixture creation.");
                string template = sheet ? app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject,
                    SystemOfMeasureEnum.kDefaultSystemOfMeasure, DraftingStandardEnum.kDefault_DraftingStandard,
                    "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}") : app.FileManager.GetTemplateFile(DocumentTypeEnum.kPartDocumentObject);
                doc = (PartDocument)app.Documents.Add(DocumentTypeEnum.kPartDocumentObject, template, true);
                doc.DisplayName = FixturePrefix + kind + ".ipt";
                if ((doc.ComponentDefinition is SheetMetalComponentDefinition) != sheet) throw new ResearchFailure("Owned fixture subtype differs from requested context.");
                var context = new JObject { ["kind"] = kind }; contexts.Add(context);
                RunContext(app, doc, context, scratch);
                GuardFixture(app, doc); doc.Close(true); doc = null;
                if (app.Documents.Count != 0) throw new ResearchFailure("Owned fixture closure incomplete.");
            }
            report["outcome"] = "PASS native sketch-block creation, parameter updates and lifecycle observations; no desktop or product parity claim"; exit = 0;
        }
        catch (Exception error)
        {
            report["outcome"] = "FAIL"; SafeError(report, error);
        }
        finally
        {
            try
            {
                if (canQuit && app != null)
                {
                    if (doc != null) { GuardFixture(app, doc); doc.Close(true); doc = null; }
                    if (app.Documents.Count != 0) throw new ResearchFailure("Unverified documents prevent automatic shutdown.");
                    app.Quit(); report["owned_quit_requested"] = true;
                }
            }
            catch (Exception error) { report["native_cleanup_error_type"] = error.GetType().FullName; report["native_cleanup_hresult"] = error.HResult; exit = 1; }
            if (ownedPid > 0)
            {
                var watch = Stopwatch.StartNew(); bool gone = false;
                while (watch.Elapsed < TimeSpan.FromSeconds(30))
                {
                    try { using var process = Process.GetProcessById(ownedPid); if (process.HasExited) { gone = true; break; } }
                    catch (ArgumentException) { gone = true; break; }
                    Thread.Sleep(100);
                }
                report["owned_pid_exited"] = gone; if (!gone) exit = 1;
            }
            bool unchanged = true;
            foreach (var pair in existing)
            {
                try { using var process = Process.GetProcessById(pair.Key); if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != pair.Value) unchanged = false; }
                catch { unchanged = false; }
            }
            report["existing_process_identity_preserved"] = unchanged; if (!unchanged) exit = 1;
            if ((bool?)report["owned_pid_exited"] == true)
            {
                try
                {
                    string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
                    if (Path.GetDirectoryName(scratch) != root || !Path.GetFileName(scratch).StartsWith("s1-skeleton57-", StringComparison.Ordinal))
                        throw new ResearchFailure("Scratch cleanup escaped the exclusively owned temporary directory.");
                    Directory.Delete(scratch, true); report["scratch_cleanup_complete"] = true;
                }
                catch { report["scratch_cleanup_complete"] = false; exit = 1; }
            }
            if (exit != 0) report["outcome"] = "FAIL";
            File.WriteAllText(Path.GetFullPath(args[0]), report.ToString(Formatting.Indented));
        }
        return exit;
    }

    private sealed record BlockFixture(SketchBlockDefinition Definition, SketchLine Line, TwoPointDistanceDimConstraint Dimension,
        UserParameter User, PlanarSketch Sketch, SketchBlock First, SketchBlock Second);

    private static BlockFixture CreateParametric(Inventor.Application app, PartDocument doc, PlanarSketch sketch)
    {
        var component = doc.ComponentDefinition; var g = app.TransientGeometry;
        var user = component.Parameters.UserParameters.AddByExpression("S1_BlockLength57", "20 mm", UnitsTypeEnum.kMillimeterLengthUnits);
        var definition = component.SketchBlockDefinitions.Add("S1_Block57_Definition", g.CreatePoint2d(0, 0));
        var line = definition.SketchLines.AddByTwoPoints(g.CreatePoint2d(0, 0), g.CreatePoint2d(2, 0));
        definition.GeometricConstraints.AddGround((SketchEntity)line.StartSketchPoint);
        definition.GeometricConstraints.AddHorizontal((SketchEntity)line, true);
        var dimension = definition.DimensionConstraints.AddTwoPointDistance(line.StartSketchPoint, line.EndSketchPoint,
            DimensionOrientationEnum.kHorizontalDim, g.CreatePoint2d(1, -0.5), false);
        dimension.Parameter.Expression = user.Name;
        var first = sketch.SketchBlocks.AddByDefinition(definition, g.CreatePoint2d(10, 20)); first.Name = "S1_Block57_InstanceA"; first.Flexible = false;
        var second = sketch.SketchBlocks.AddByDefinition(definition, g.CreatePoint2d(35, -5)); second.Name = "S1_Block57_InstanceB"; second.Flexible = false;
        Update(doc);
        return new(definition, line, dimension, user, sketch, first, second);
    }

    private static void RunContext(Inventor.Application app, PartDocument doc, JObject context, string scratch)
    {
        GuardFixture(app, doc); var component = doc.ComponentDefinition;
        var sketch = component.Sketches.Add(component.WorkPlanes[3]); sketch.Name = "S1_Block57_Host";
        var cases = new JArray(); context["cases"] = cases;
        JObject TransactionCase(string name, bool commit, Func<JObject> action)
        {
            GuardFixture(app, doc); Phase((string)context["kind"]! + "_" + name);
            var before = Snapshot(doc); var row = new JObject { ["case"] = name, ["transaction"] = commit ? "commit" : "preview_abort", ["before"] = before.DeepClone() };
            cases.Add(row); var transaction = app.TransactionManager.StartTransaction((_Document)doc, "S1 Skeleton57 " + name); bool ended = false;
            try
            {
                row["observations"] = action(); Update(doc); row["tentative"] = Snapshot(doc);
                RequireOwnedTransaction(app, transaction);
                if (commit) transaction.End(); else transaction.Abort(); ended = true;
                row["after"] = Snapshot(doc);
                if (!commit)
                {
                    row["exact_restoration"] = JToken.DeepEquals(before, row["after"]);
                    if ((bool?)row["exact_restoration"] != true) throw new ResearchFailure("Owned block preview did not restore exact persistent native state.");
                }
                row["outcome"] = "PASS"; return row;
            }
            catch (Exception error) { row["outcome"] = "FAIL"; SafeError(row, error); throw; }
            finally { if (!ended) { RequireOwnedTransaction(app, transaction); transaction.Abort(); row["abort_after_failure"] = true; row["after_failure_abort"] = Snapshot(doc); } }
        }
        TransactionCase("empty_definition_creation_preview", false, () => VerifyParametric(CreateParametric(app, doc, sketch), 20));
        BlockFixture? fixture = null;
        TransactionCase("empty_definition_creation_commit", true, () => VerifyParametric(fixture = CreateParametric(app, doc, sketch), 20));
        var current = fixture!;
        TransactionCase("linked_parameter_update_preview", false, () => { current.User.Expression = "35 mm"; Update(doc); return VerifyParametric(current, 35); });
        TransactionCase("linked_parameter_update_commit", true, () => { current.User.Expression = "35 mm"; Update(doc); return VerifyParametric(current, 35); });
        TransactionCase("rename_and_reposition_preview", false, () =>
        {
            current.Definition.Name = "S1_Block57_Renamed"; current.First.Name = "S1_Block57_RenamedInstance";
            current.First.Position = app.TransientGeometry.CreatePoint2d(11, 22); Update(doc);
            Near(current.First.Position.X * 10, 110); Near(current.First.Position.Y * 10, 220);
            return new JObject { ["definition_name"] = current.Definition.Name, ["instance_name"] = current.First.Name,
                ["position_mm"] = P(current.First.Position), ["same_definition_key"] = Key(current.First.Definition) == Key(current.Definition) };
        });
        TransactionCase("delete_one_instance_preview", false, () =>
        {
            current.Second.Delete(); Update(doc);
            if (sketch.SketchBlocks.Count != 1 || component.SketchBlockDefinitions.Count != 1 || !current.Definition.IsReferenced)
                throw new ResearchFailure("Deleting one instance did not preserve the used definition and remaining instance.");
            return new JObject { ["instances"] = sketch.SketchBlocks.Count, ["definitions"] = component.SketchBlockDefinitions.Count,
                ["definition_still_referenced"] = current.Definition.IsReferenced };
        });
        current = Reacquire(doc, sketch);
        TransactionCase("explode_and_update_observation_preview", false, () =>
        {
            var freeBefore = sketch.SketchLines.Cast<SketchLine>().Where(line => line.ContainingSketchBlock == null).Select(Key).ToHashSet(StringComparer.Ordinal);
            current.First.Explode(); Update(doc);
            if (sketch.SketchBlocks.Count != 1 || component.SketchBlockDefinitions.Count != 1) throw new ResearchFailure("Explode did not remove exactly one block instance while preserving the definition.");
            var loose = sketch.SketchLines.Cast<SketchLine>().Where(line => line.ContainingSketchBlock == null && !freeBefore.Contains(Key(line))).ToArray();
            if (loose.Length != 1) throw new ResearchFailure("Exploded single-line block did not retain exactly one free native line.");
            Near(Length(loose[0]), 35); current.User.Expression = "40 mm"; Update(doc);
            Near(Length(current.Second.GetObject(current.Line)), 40);
            double explodedLength = Length(loose[0]);
            return new JObject { ["free_line_created"] = true, ["remaining_instance_updated_mm"] = 40,
                ["exploded_line_after_parameter_mm"] = explodedLength,
                ["exploded_parameter_link_observed"] = Math.Abs(explodedLength - 40) <= 1e-5,
                ["scope"] = "Observed native explode parameter behavior; no inferred desktop default." };
        });
        current = Reacquire(doc, sketch);
        TransactionCase("selected_geometry_conversion_preview", false, () =>
        {
            var conversion = component.Sketches.Add(component.WorkPlanes[3]); conversion.Name = "S1_Block57_Conversion"; var g = app.TransientGeometry;
            var line = conversion.SketchLines.AddByTwoPoints(g.CreatePoint2d(3, 4), g.CreatePoint2d(6.5, 4));
            conversion.GeometricConstraints.AddGround((SketchEntity)line.StartSketchPoint); conversion.GeometricConstraints.AddHorizontal((SketchEntity)line, true);
            var dimension = conversion.DimensionConstraints.AddTwoPointDistance(line.StartSketchPoint, line.EndSketchPoint,
                DimensionOrientationEnum.kHorizontalDim, g.CreatePoint2d(4, 3), false); dimension.Parameter.Expression = current.User.Name; Update(doc);
            var selection = app.TransientObjects.CreateObjectCollection(); selection.Add(line);
            var block = conversion.SketchBlocks.Add(selection, "S1_Block57_ConvertedDefinition", g.CreatePoint2d(3, 4)); block.Flexible = false; Update(doc);
            var def = block.Definition;
            if (conversion.SketchBlocks.Count != 1 || component.SketchBlockDefinitions.Count != 2 || def.SketchLines.Count != 1)
                throw new ResearchFailure("Selected-geometry block conversion did not create the native definition and instance.");
            var copiedLine = def.SketchLines[1]; Near(Length(block.GetObject(copiedLine)), 35);
            var second = conversion.SketchBlocks.AddByDefinition(def, g.CreatePoint2d(-5, 8)); second.Flexible = false;
            current.User.Expression = "40 mm"; Update(doc);
            double firstLength = Length(block.GetObject(copiedLine)), secondLength = Length(second.GetObject(copiedLine));
            return new JObject { ["native_definition_created"] = true, ["native_instances"] = conversion.SketchBlocks.Count,
                ["definition_dimensions"] = def.DimensionConstraints.Count, ["source_user_parameter_mm"] = Numeric(current.User.Value) * 10,
                ["first_length_mm"] = firstLength, ["second_length_mm"] = secondLength,
                ["parameter_link_observed"] = Math.Abs(firstLength - 40) <= 1e-5 && Math.Abs(secondLength - 40) <= 1e-5,
                ["scope"] = "Native conversion and linkage observation; preservation is not assumed from visual geometry." };
        });
        current = Reacquire(doc, sketch);
        TransactionCase("delete_referenced_definition_observation_preview", false, () =>
        {
            var result = new JObject { ["is_referenced_before"] = current.Definition.IsReferenced };
            try { current.Definition.Delete(); Update(doc); result["native_result"] = "deleted"; }
            catch (COMException error) { result["native_result"] = "refused"; result["hresult"] = error.HResult; }
            result["definitions_after_attempt"] = component.SketchBlockDefinitions.Count; result["instances_after_attempt"] = sketch.SketchBlocks.Count;
            return result;
        });
        current = Reacquire(doc, sketch); VerifyParametric(current, 35);
        Phase((string)context["kind"]! + "_save_copy_reopen");
        var original = Snapshot(doc); string copyPath = Path.GetFullPath(Path.Combine(scratch, "block57-" + (string)context["kind"]! + ".ipt"));
        if (Path.GetDirectoryName(copyPath) != scratch || File.Exists(copyPath)) throw new ResearchFailure("SaveCopy path is not an exclusively owned new file.");
        PartDocument? copy = null; bool ownedCopy = false;
        try
        {
            GuardFixture(app, doc); doc.SaveAs(copyPath, true);
            if (!File.Exists(copyPath) || !JToken.DeepEquals(original, Snapshot(doc))) throw new ResearchFailure("SaveCopy changed the original or produced no owned file.");
            copy = app.Documents.Open(copyPath, true) as PartDocument;
            ownedCopy = copy != null && KeyDocument(copy) != KeyDocument(doc) && string.Equals(Path.GetFullPath(copy.FullFileName), copyPath, StringComparison.OrdinalIgnoreCase);
            if (!ownedCopy || app.Documents.Count != 2) throw new ResearchFailure("Reopened copy ownership is unverified.");
            copy!.Activate(); var reopened = Reacquire(copy, copy.ComponentDefinition.Sketches.Cast<PlanarSketch>().Single(s => s.Name == "S1_Block57_Host"));
            var reopenedEvidence = VerifyParametric(reopened, 35);
            if (Key(reopened.Definition) != Key(current.Definition) || Key(reopened.First) != Key(current.First) || Key(reopened.Second) != Key(current.Second))
                throw new ResearchFailure("Native block/definition reference keys did not persist through SaveCopy and reopen.");
            context["save_copy"] = new JObject { ["outcome"] = "PASS", ["parameter_and_geometry"] = reopenedEvidence,
                ["definition_and_instance_keys_exact"] = true, ["original_preserved_exact"] = true };
        }
        finally
        {
            if (copy != null && ownedCopy)
            {
                if (!string.Equals(Path.GetFullPath(copy.FullFileName), copyPath, StringComparison.OrdinalIgnoreCase)) throw new ResearchFailure("Copy ownership changed before closure.");
                copy.Close(true); copy = null;
            }
            if (copy != null) throw new ResearchFailure("Unverified copy retained; refusing original restoration or scratch deletion.");
            doc.Activate(); GuardFixture(app, doc);
            if (!JToken.DeepEquals(original, Snapshot(doc))) throw new ResearchFailure("Original changed after reopened copy closure.");
        }
        context["completed_cases"] = cases.OfType<JObject>().Count(row => (string?)row["outcome"] == "PASS");
        context["outcome"] = "PASS";
    }

    private static BlockFixture Reacquire(PartDocument doc, PlanarSketch sketch)
    {
        var definition = doc.ComponentDefinition.SketchBlockDefinitions["S1_Block57_Definition"];
        return new(definition, definition.SketchLines[1], (TwoPointDistanceDimConstraint)definition.DimensionConstraints[1],
            doc.ComponentDefinition.Parameters.UserParameters["S1_BlockLength57"], sketch,
            sketch.SketchBlocks["S1_Block57_InstanceA"], sketch.SketchBlocks["S1_Block57_InstanceB"]);
    }

    private static JObject VerifyParametric(BlockFixture fixture, double length)
    {
        Near(Numeric(fixture.User.Value) * 10, length); Near(Numeric(fixture.Dimension.Parameter.Value) * 10, length); Near(Length(fixture.Line), length);
        if (fixture.Dimension.Parameter.Expression != fixture.User.Name) throw new ResearchFailure("Definition dimension lost its explicit user-parameter expression.");
        if (Key(fixture.First.Definition) != Key(fixture.Definition) || Key(fixture.Second.Definition) != Key(fixture.Definition))
            throw new ResearchFailure("Block instances no longer reference the exact shared definition.");
        object first = fixture.First.GetObject(fixture.Line), second = fixture.Second.GetObject(fixture.Line);
        Near(Length(first), length); Near(Length(second), length);
        return new JObject { ["definition_key"] = Key(fixture.Definition), ["source_line_key"] = Key(fixture.Line),
            ["user_parameter"] = fixture.User.Name, ["user_expression"] = fixture.User.Expression,
            ["dimension_expression"] = fixture.Dimension.Parameter.Expression, ["expected_length_mm"] = length,
            ["source_geometry"] = Geometry(fixture.Line), ["first_geometry"] = Geometry(first), ["second_geometry"] = Geometry(second),
            ["first_position_mm"] = P(fixture.First.Position), ["second_position_mm"] = P(fixture.Second.Position),
            ["definition_constraint_status"] = fixture.Definition.ConstraintStatus.ToString(), ["definition_health"] = fixture.Definition.HealthStatus.ToString(),
            ["first_constraint_status"] = fixture.First.ConstraintStatus.ToString(), ["second_constraint_status"] = fixture.Second.ConstraintStatus.ToString(),
            ["two_instances_share_parametric_definition"] = true };
    }

    private static JObject Snapshot(PartDocument doc)
    {
        var component = doc.ComponentDefinition;
        JObject SketchState(object native)
        {
            dynamic sketch = native;
            return new JObject { ["key"] = Key(native), ["name"] = (string)sketch.Name, ["native_type"] = ((ObjectTypeEnum)sketch.Type).ToString(),
                ["lines"] = new JArray(((SketchLines)sketch.SketchLines).Cast<SketchLine>().Select(line => new JObject { ["key"] = Key(line),
                    ["geometry"] = Geometry(line), ["construction"] = line.Construction,
                    ["containing_block"] = line.ContainingSketchBlock == null ? null : Key(line.ContainingSketchBlock) })),
                ["points"] = new JArray(((SketchPoints)sketch.SketchPoints).Cast<SketchPoint>().Select(point => new JObject { ["key"] = Key(point), ["point_mm"] = P(point.Geometry) })),
                ["constraints"] = new JArray(((GeometricConstraints)sketch.GeometricConstraints).Cast<GeometricConstraint>().Select(c => new JObject { ["key"] = Key(c), ["type"] = c.Type.ToString() })),
                ["dimensions"] = new JArray(((DimensionConstraints)sketch.DimensionConstraints).Cast<DimensionConstraint>().Select(d => new JObject { ["key"] = Key(d),
                    ["name"] = d.Parameter.Name, ["expression"] = d.Parameter.Expression, ["value_internal"] = Numeric(d.Parameter.Value), ["driven"] = d.Driven })),
                ["blocks"] = new JArray(((SketchBlocks)sketch.SketchBlocks).Cast<SketchBlock>().Select(block => new JObject { ["key"] = Key(block), ["name"] = block.Name,
                    ["definition"] = Key(block.Definition), ["position_mm"] = P(block.Position), ["flexible"] = block.Flexible,
                    ["transformation"] = MatrixData(block.Transformation) })) };
        }
        return new JObject { ["internal_name"] = doc.InternalName, ["path"] = doc.FullFileName,
            ["database_revision"] = doc.DatabaseRevisionId, ["dirty"] = doc.Dirty,
            ["definitions"] = new JArray(component.SketchBlockDefinitions.Cast<SketchBlockDefinition>().Select(definition => SketchState(definition))),
            ["sketches"] = new JArray(component.Sketches.Cast<PlanarSketch>().Select(sketch => SketchState(sketch))),
            ["user_parameters"] = new JArray(component.Parameters.UserParameters.Cast<UserParameter>().Select(p => new JObject { ["key"] = Key(p), ["name"] = p.Name, ["expression"] = p.Expression, ["value_internal"] = Numeric(p.Value) })),
            ["model_parameters"] = new JArray(component.Parameters.ModelParameters.Cast<ModelParameter>().Select(p => new JObject { ["key"] = Key(p), ["name"] = p.Name, ["expression"] = p.Expression, ["value_internal"] = Numeric(p.Value) })) };
    }

    private static JArray MatrixData(Matrix2d matrix) { double[] values = new double[9]; matrix.GetMatrixData(ref values); return new JArray(values); }
    private static JArray P(Point2d point) => new(point.X * 10, point.Y * 10);
    private static JObject Geometry(object entity)
    {
        LineSegment2d line = entity switch { SketchLine native => native.Geometry, SketchLineProxy proxy => proxy.Geometry,
            _ => throw new ResearchFailure("Definition object mapping did not return a native line or line proxy.") };
        return new JObject { ["start_mm"] = P(line.StartPoint), ["end_mm"] = P(line.EndPoint), ["length_mm"] = line.StartPoint.DistanceTo(line.EndPoint) * 10,
            ["native_type"] = ((dynamic)entity).Type.ToString() };
    }
    private static double Length(object entity) => (double)Geometry(entity)["length_mm"]!;
    private static double Numeric(object value) => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    private static string Key(object entity) { byte[] key = Array.Empty<byte>(); ((dynamic)entity).GetReferenceKey(ref key, 0); if (key.Length == 0) throw new ResearchFailure("Native object has no reference key."); return Convert.ToBase64String(key); }
    private static string KeyDocument(PartDocument doc)
    { IntPtr identity = Marshal.GetIUnknownForObject(doc); try { return identity.ToInt64().ToString("x"); } finally { Marshal.Release(identity); } }
    private static void Update(PartDocument doc) { if (!doc.Update2(true)) throw new ResearchFailure("Native document update failed."); }
    private static void Near(double actual, double expected) { if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-5) throw new ResearchFailure("Native block geometry/parameter differs from independent expected value."); }
    private static void RequireOwnedTransaction(Inventor.Application app, Transaction transaction)
    { if (app.TransactionManager.CurrentTransaction == null || Identity(app.TransactionManager.CurrentTransaction) != Identity(transaction)) throw new ResearchFailure("The active native transaction is not owned by this case."); }
    private static long Identity(object value) { IntPtr pointer = Marshal.GetIUnknownForObject(value); try { return pointer.ToInt64(); } finally { Marshal.Release(pointer); } }
    private static void GuardFixture(Inventor.Application app, PartDocument doc)
    {
        if (app.Documents.Count != 1 || !doc.DisplayName.StartsWith(FixturePrefix, StringComparison.Ordinal) || !string.IsNullOrEmpty(doc.FullFileName)
            || app.ActiveDocument is not PartDocument active || Identity(active) != Identity(doc)) throw new ResearchFailure("Exact single exclusively owned unsaved fixture required.");
    }
    private static void Phase(string phase) { if (_activeReport != null) _activeReport["phase"] = phase; }
    private static void SafeError(JObject destination, Exception error) { destination["error_type"] = error.GetType().FullName; destination["error_hresult"] = error.HResult; if (error is ResearchFailure) destination["reason"] = error.Message; }
    private static Dictionary<int, long> ExistingProcesses() { var rows = new Dictionary<int, long>(); foreach (var process in Process.GetProcessesByName("Inventor")) using (process) rows.Add(process.Id, process.StartTime.ToUniversalTime().Ticks); return rows; }
    private static JArray ProcessRows(Dictionary<int, long> rows) => new(rows.Select(p => new JObject { ["pid"] = p.Key, ["start_utc_ticks"] = p.Value }));
    private static JObject InteropEvidence()
    {
        var assembly = typeof(SketchBlocks).Assembly;
        var signatures = new JArray();
        foreach (var specification in new[] { (typeof(SketchBlockDefinitions), "Add"), (typeof(SketchBlocks), "Add"), (typeof(SketchBlocks), "AddByDefinition"),
            (typeof(SketchBlock), "GetObject"), (typeof(SketchBlock), "Explode"), (typeof(SketchBlockDefinition), "Delete"), (typeof(DimensionConstraints), "AddTwoPointDistance") })
        {
            var method = specification.Item1.GetMethod(specification.Item2)!;
            signatures.Add(new JObject { ["type"] = specification.Item1.FullName, ["signature"] = method.ToString(),
                ["parameters"] = new JArray(method.GetParameters().Select(p => new JObject { ["name"] = p.Name, ["type"] = p.ParameterType.FullName,
                    ["optional"] = p.IsOptional, ["default"] = p.DefaultValue == null || p.DefaultValue is DBNull or Missing ? null : JToken.FromObject(p.DefaultValue) })) });
        }
        return new JObject { ["assembly"] = assembly.FullName, ["signatures"] = signatures };
    }
}
