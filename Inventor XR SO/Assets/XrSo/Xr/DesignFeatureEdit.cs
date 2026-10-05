using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Input;
using InventorXrSo.Core.Mcp;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Xr.Input;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// M9 §4 (Parte): double Trigger on a face opens the tab «Feature: nome» with one chip per driving parameter. Chips only change the
    /// local draft; Anteprima/Applica go through the SAME session and commit bar as the Parametri tab (one batch, N set_parameter).
    /// NOT COVERED: the handle on the geometry (extrusion distance along the normal) is not wired for the feature edit; chips only.
    /// </summary>
    public sealed partial class DesignWorkspace
    {
        private FeatureEditModel _featureEdit;
        private readonly DoubleTriggerDetector _doubleTrigger = new DoubleTriggerDetector();

        /// <summary>Test seam: seconds clock of the double Trigger detector (default: unscaled game time).</summary>
        public Func<double> DoubleTriggerClock { get; set; }
        /// <summary>The open feature edit (chips, draft values), or null.</summary>
        public FeatureEditModel FeatureEdit => _featureEdit;
        /// <summary>Id of the dynamic tab «Feature: nome», or null when no feature is open.</summary>
        public string FeatureTabId => _featureEdit == null ? null : ContextTabs.FeatureEditPrefix + _featureEdit.Name;

        private const string IdFeaturePrefix = "design.feature.", IdFeaturePrevious = "design.feature.prev",
            IdFeatureDesktop = "design.feature.desktop", IdFeatureParameters = "design.feature.parameters";

        private double DoubleClock => DoubleTriggerClock != null ? DoubleTriggerClock() : Time.unscaledTimeAsDouble;

        // ---------------------------------------------------------------- gesture

        private void ResolveFace(CadBody body, int triangle, out string target, out string faceId, out string meshFaceId)
        {
            var range = body.Primitive.FaceMap?.FaceAtTriangle(triangle);
            meshFaceId = range?.FaceId;
            if (range == null) { target = ControllerRay.TargetId(body); faceId = null; return; }
            var face = _context?.Faces.FirstOrDefault(f => f.BodyIndex > 0 && f.FaceOrdinal > 0
                    && f.BodyIndex == body.Primitive.BodyIndex && f.FaceOrdinal == range.Ordinal)
                ?? _context?.Faces.FirstOrDefault(f => f.Id == range.FaceId);
            faceId = face?.Id ?? range.FaceId;
            target = ControllerRay.TargetId(body) + "#" + range.FaceId;
        }

        /// <summary>Second press on the same face opens the feature edit. False: it was a first press (the caller selects as today).</summary>
        private bool DoublePressOnFace(Ray ray, CadBody body, int triangle)
        {
            ResolveFace(body, triangle, out var target, out var faceId, out var meshFaceId);
            var d = ray.direction;
            if (!_doubleTrigger.Press(DoubleClock, faceId == null ? null : target, d.x, d.y, d.z)) return false;
            // M9: the dispatcher says whether the current input state binds the double Trigger.
            if (_input != null && !_input.Dispatcher.InvokeSecondary(Key.Trigger)) return true;
            HideRing();
            if (meshFaceId != null && _selection != null)
                _selection.Show(new InventorXrSo.Core.Selection.Selection(InventorXrSo.Core.Selection.SelectionKind.Face,
                    body.Instance.OccurrenceId, meshFaceId, faceId));
            _ = OpenFeatureForFace(faceId);
            return true;
        }

        // ---------------------------------------------------------------- open / close

        /// <summary>
        /// face_feature for the face, then the tab «Feature: nome». Highlight is best effort: the response carries no face list, so
        /// only the picked face is highlighted (and the HUD says so).
        /// </summary>
        public async Task OpenFeatureForFace(string faceId)
        {
            if (!Active || _kind != "part" || string.IsNullOrEmpty(faceId)) return;
            if (!(_backend is IFaceFeatureBackend reader)) { SetNotice("Modifica feature non disponibile con questo backend."); Refresh(); return; }
            if (!_online || _state == null) { SetNotice("Offline: la feature si legge solo con Inventor collegato."); Refresh(); return; }
            if (_busy || _pendingMutations > 0 || _session == null || _session.Status != DesignStatus.Empty || _ask != null)
            { SetNotice("Chiudi o annulla prima il comando in corso."); Refresh(); return; }
            int generation = _generation; var state = _state; bool reload = false;
            _busy = true; Refresh();
            try
            {
                var info = await reader.GetFaceFeatureAsync(state, faceId, _reads.Token);
                if (generation != _generation) return;
                _featureEdit = new FeatureEditModel(info, faceId, KnownParameterNames());
                _notice = "";
                string hud = "Feature «" + info.FeatureName + "» (" + info.FeatureType + "). Evidenziata la faccia scelta: l'elenco delle facce della feature non è disponibile.";
                if (_featureEdit.BlockReason != null) hud += "\n" + _featureEdit.BlockReason + " Lettura consentita.";
                else if (_featureEdit.Chips.Any(c => !c.Editable)) hud += "\nI parametri guidati da un'espressione sono in sola lettura.";
                SetNotice(hud);
            }
            catch (OperationCanceledException) { }
            catch (McpToolException ex) when (ex.Code == FaceFeatureInfo.NoOwningFeatureCode)
            { if (generation == _generation) SetNotice("La faccia non appartiene a una feature modificabile (corpo base, derivato o importato)."); }
            catch (McpToolException ex) when (ex.Code == "STALE_REVISION" || ex.Code == "DOCUMENT_CHANGED")
            {
                if (generation == _generation)
                { ResetDraft(); _context = null; reload = true; SetNotice("Il documento è cambiato dal desktop: rileggo i riferimenti."); }
            }
            catch (Exception ex) { if (generation == _generation) SetNotice(ex.Message); }
            finally { if (generation == _generation) { _busy = false; Refresh(); } }
            if (reload && generation == _generation) LoadContext();
            else if (_featureEdit != null && generation == _generation) _shell?.Palette.ShowTab(FeatureTabId);
        }

        private IEnumerable<string> KnownParameterNames() =>
            (_context?.Parameters ?? new Newtonsoft.Json.Linq.JArray()).OfType<Newtonsoft.Json.Linq.JObject>()
                .Select(p => (string)p["name"]).Where(n => !string.IsNullOrEmpty(n));

        private void CloseFeatureEdit()
        {
            if (_featureEdit == null) return;
            _featureEdit = null; _selection?.Clear();
            Refresh();
            _shell?.Palette.ShowTab(TabFeature);
        }

        // ---------------------------------------------------------------- draft

        private static QuantityUnit QuantityOf(string unit) => unit == "deg" ? QuantityUnit.Degrees : unit == "mm" ? QuantityUnit.Millimeters : QuantityUnit.None;

        private void AskFeatureValue(FeatureChip chip)
        {
            string title = "Feature " + _featureEdit.Name;
            Ask(title, new[] { chip.Name + " (" + chip.Unit + ")" }, new[] { chip.Value }, v => SetFeatureValue(chip.Name, v[0]), QuantityOf(chip.Unit));
        }

        /// <summary>A chip changed: the local draft is rebuilt (N set_parameter) and previewed like a Parametri edit. Nothing reaches CAD here.</summary>
        private void SetFeatureValue(string name, double value)
        {
            if (_featureEdit == null) return;
            if (!_featureEdit.TrySetValue(name, value, out var reason)) { SetNotice(reason); Refresh(); return; }
            RebuildFeatureDraft();
        }

        private void SetFeatureSource(string name, double value, string unit)
        {
            if (_featureEdit == null) return;
            if (!_featureEdit.TrySetSource(name, value, unit, out var reason)) { SetNotice(reason); Refresh(); return; }
            RebuildFeatureDraft();
        }

        private void RebuildFeatureDraft()
        {
            if (!_featureEdit.IsDirty)
            {
                // Back to the original values: no draft left.
                if (_session.Status != DesignStatus.Empty) _session.Cancel();
                _screen = "tools"; _parameterName = null; Refresh(); return;
            }
            _feature = null; _sketch = null; _existingSketch = null; _parameterName = _featureEdit.Name; _screen = "parameter";
            try
            {
                _session.SetDraft(_featureEdit.BuildOperations());
                PreviewParameter();
            }
            catch (Exception ex) { SetNotice(ex.Message); Refresh(); }
        }

        // ---------------------------------------------------------------- actions

        private bool FeatureTabOpen => Active && _featureEdit != null;

        private IEnumerable<XrAction> BuildFeatureEditActions()
        {
            var model = _featureEdit;
            string tab = FeatureTabId;
            bool unsupported = !model.Info.Supported;
            var fixedActions = new List<XrAction>();
            if (!string.IsNullOrEmpty(model.Info.PreviousFeature))
                fixedActions.Add(new XrAction(IdFeaturePrevious, "Feature precedente: " + model.Info.PreviousFeature, tab, () => FeatureTabOpen, () =>
                {
                    SetNotice("Feature precedente: «" + model.Info.PreviousFeature + "». Per aprirla, doppio Trigger su una sua faccia: il nome non si risolve da solo in una faccia.");
                    Refresh();
                }, () => "Nessuna feature aperta."));
            if (unsupported)
                fixedActions.Add(new XrAction(IdFeatureDesktop, "Modifica dal desktop", tab, () => FeatureTabOpen, () =>
                {
                    SetNotice("«" + model.Name + "» (" + model.Info.FeatureType + ") non si modifica in XR: modificala dal desktop in Inventor.");
                    Refresh();
                }, () => "Nessuna feature aperta."));
            fixedActions.Add(new XrAction(IdFeatureParameters, "Parametri", tab, () => FeatureTabOpen, () => _shell?.Palette.ShowTab(TabParameters),
                () => "Nessuna feature aperta."));

            var list = new List<XrAction>();
            int room = ActionCatalog.MaxPalette - fixedActions.Count;
            for (int i = 0; i < model.Chips.Count && list.Count < room; i++)
            {
                var chip = model.Chips[i];
                string label = chip.Editable || chip.Expression == null ? chip.Name + " = " + Format(chip.Value) + " " + UnitSuffix(chip.Unit)
                    : chip.Name + " = " + chip.Expression + " (espressione)";
                list.Add(new XrAction(IdFeaturePrefix + "p." + i, label, tab, () => EditableNow && _featureEdit == model && chip.Editable,
                    () => AskFeatureValue(chip), () => chip.ReadOnlyReason ?? EditReason(), kind: XrActionKind.Numeric));
            }
            for (int i = 0; i < model.Chips.Count && list.Count < room; i++)
            {
                var chip = model.Chips[i];
                if (chip.SourceParameter == null || model.BlockReason != null) continue;
                string source = chip.SourceParameter;
                list.Add(new XrAction(IdFeaturePrefix + "src." + i, "Modifica " + source, tab, () => EditableNow && _featureEdit == model,
                    () => EditSourceParameter(source), EditReason));
            }
            list.AddRange(fixedActions);
            return list;
        }

        private static string UnitSuffix(string unit) => unit == "deg" ? "°" : unit == "mm" ? "mm" : "";

        /// <summary>«Modifica sorgente»: the source parameter of a plain-reference expression gets the value, in the same batch.</summary>
        private void EditSourceParameter(string name)
        {
            var parameter = _context?.Parameters.OfType<Newtonsoft.Json.Linq.JObject>().FirstOrDefault(p => (string)p["name"] == name);
            if (parameter == null) { SetNotice("Parametro «" + name + "» non trovato."); Refresh(); return; }
            string unit = parameter["value_mm"] != null ? "mm" : parameter["value_deg"] != null ? "deg" : "ul";
            double value = (double?)(parameter["value_mm"] ?? parameter["value_deg"] ?? parameter["value_unitless"]) ?? 0;
            Ask("Parametro " + name, new[] { unit }, new[] { value }, values => SetFeatureSource(name, values[0], unit), QuantityOf(unit));
        }
    }
}
