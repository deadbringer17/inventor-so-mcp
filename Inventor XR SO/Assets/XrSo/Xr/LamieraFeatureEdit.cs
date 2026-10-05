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
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// M9 §4 (Lamiera): same feature edit as Progettazione (double Trigger on a face, tab «Feature: nome», chips on the local draft).
    /// The draft is the armed command <see cref="SheetMetalCommand.FeatureEdit"/>: Anteprima, Applica and Annulla are the ordinary
    /// commit bar of Lamiera. NOT COVERED: the flange handle is not wired to the feature edit; chips only.
    /// </summary>
    public sealed partial class LamieraWorkspace : ITabStateSource
    {
        private FeatureEditModel _featureEdit;
        private readonly DoubleTriggerDetector _doubleTrigger = new DoubleTriggerDetector();

        /// <summary>Test seam: seconds clock of the double Trigger detector (default: unscaled game time).</summary>
        public Func<double> DoubleTriggerClock { get; set; }
        public FeatureEditModel FeatureEdit => _featureEdit;
        public string FeatureTabId => _featureEdit == null ? null : ContextTabs.FeatureEditPrefix + _featureEdit.Name;
        public TabState TabState => new TabState { FeatureEditOpen = Active && _featureEdit != null, FeatureName = _featureEdit?.Name };

        private const string IdFeaturePrefix = "lamiera.feature.", IdFeaturePrevious = "lamiera.feature.prev",
            IdFeatureDesktop = "lamiera.feature.desktop";

        private double DoubleClock => DoubleTriggerClock != null ? DoubleTriggerClock() : Time.unscaledTimeAsDouble;

        // ---------------------------------------------------------------- gesture

        private bool DoublePressOnFace(Ray ray, CadBody body, int triangle)
        {
            var range = body.Primitive.FaceMap?.FaceAtTriangle(triangle);
            string faceId = null, target = null;
            if (range != null)
            {
                var face = _designContext?.Faces.FirstOrDefault(f => f.BodyIndex > 0 && f.FaceOrdinal > 0
                        && f.BodyIndex == body.Primitive.BodyIndex && f.FaceOrdinal == range.Ordinal)
                    ?? _designContext?.Faces.FirstOrDefault(f => f.Id == range.FaceId);
                faceId = face?.Id ?? range.FaceId;
                target = ControllerRay.TargetId(body) + "#" + range.FaceId;
            }
            var d = ray.direction;
            if (!_doubleTrigger.Press(DoubleClock, target, d.x, d.y, d.z)) return false;
            if (_input != null && !_input.Dispatcher.InvokeSecondary(Key.Trigger)) return true;
            HideRing();
            _selection?.Show(new InventorXrSo.Core.Selection.Selection(InventorXrSo.Core.Selection.SelectionKind.Face,
                body.Instance.OccurrenceId, range.FaceId, faceId));
            _ = OpenFeatureForFace(faceId);
            return true;
        }

        // ---------------------------------------------------------------- open / close

        /// <summary>face_feature for the face, then the tab «Feature: nome» (highlight of the picked face only: no face list in the response).</summary>
        public async Task OpenFeatureForFace(string faceId)
        {
            if (!Active || _kind != "part" || string.IsNullOrEmpty(faceId)) return;
            if (!(_backend is IFaceFeatureBackend reader)) { SetNotice("Modifica feature non disponibile con questo backend."); Refresh(); return; }
            if (!Interactive || Locked || _session.Status != DesignStatus.Empty || _mode.Armed != SheetMetalCommand.None || _ask != null)
            { SetNotice(Interactive && !Locked ? "Chiudi o annulla prima il comando in corso." : CommandReason()); Refresh(); return; }
            int generation = _generation; var state = _state; bool reload = false;
            _busy = true; Refresh();
            try
            {
                var info = await reader.GetFaceFeatureAsync(state, faceId, _reads.Token);
                if (generation != _generation) return;
                FoldedView();
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
                { ResetDraft(); _designContext = null; reload = true; SetNotice("Il documento è cambiato dal desktop: rileggo i riferimenti."); }
            }
            catch (Exception ex) { if (generation == _generation) SetNotice(ex.Message); }
            finally { if (generation == _generation) { _busy = false; Refresh(); } }
            if (reload && generation == _generation) LoadContext();
            else if (_featureEdit != null && generation == _generation) _shell?.Palette.ShowTab(FeatureTabId);
        }

        private IEnumerable<string> KnownParameterNames() =>
            (_designContext?.Parameters ?? new JArray()).OfType<JObject>().Select(p => (string)p["name"]).Where(n => !string.IsNullOrEmpty(n));

        private void CloseFeatureEdit()
        {
            if (_featureEdit == null) return;
            _featureEdit = null; _selection?.Clear();
            Refresh();
            _shell?.Palette.ShowTab(TabLamiera);
        }

        // ---------------------------------------------------------------- draft

        private static QuantityUnit QuantityOf(string unit) => unit == "deg" ? QuantityUnit.Degrees : unit == "mm" ? QuantityUnit.Millimeters : QuantityUnit.None;

        private void AskFeature(string title, string label, double current, string unit, Action<double> done)
        {
            var entry = new NumericEntry("lamiera.feature.ask", QuantityOf(unit), current, -100000, 100000);
            _ask = entry;
            entry.Committed += () =>
            {
                if (_ask != entry) return;
                _ask = null;
                try { _notice = ""; done(entry.Value); } catch (Exception ex) { SetNotice(ex.Message); }
                Refresh();
            };
            if (_shell != null) _shell.Palette.ShowKeypad(entry, title + " • " + label); else entry.BeginEdit();
        }

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

        /// <summary>Arms the feature edit and replaces the session draft with N set_parameter; nothing reaches CAD until Applica.</summary>
        private void RebuildFeatureDraft()
        {
            var model = _featureEdit;
            if (!model.IsDirty)
            {
                // Back to the original values: no draft left, the tab stays open.
                DiscardDraft(); _featureEdit = model; Refresh(); return;
            }
            if (_mode.Armed != SheetMetalCommand.FeatureEdit && !_mode.Arm(SheetMetalCommand.FeatureEdit, out var reason)) { SetNotice(reason); Refresh(); return; }
            _screen = "feature";
            if (!_mode.SubmitFeatureEdit(_session, model)) { if (!string.IsNullOrEmpty(_mode.LastError)) SetNotice(_mode.LastError); Refresh(); return; }
            PreviewDraft();
        }

        // ---------------------------------------------------------------- actions

        private bool FeatureTabOpen => Active && _featureEdit != null;
        private bool FeatureChipsOn => Interactive && !Locked && _session.CanEdit && (_mode.Armed == SheetMetalCommand.None || _mode.Armed == SheetMetalCommand.FeatureEdit);

        private IEnumerable<XrAction> BuildFeatureEditActions()
        {
            var model = _featureEdit;
            string tab = FeatureTabId;
            var fixedActions = new List<XrAction>();
            if (!string.IsNullOrEmpty(model.Info.PreviousFeature))
                fixedActions.Add(new XrAction(IdFeaturePrevious, "Feature precedente: " + model.Info.PreviousFeature, tab, () => FeatureTabOpen, () =>
                {
                    SetNotice("Feature precedente: «" + model.Info.PreviousFeature + "». Per aprirla, doppio Trigger su una sua faccia: il nome non si risolve da solo in una faccia.");
                    Refresh();
                }, () => "Nessuna feature aperta."));
            if (!model.Info.Supported)
                fixedActions.Add(new XrAction(IdFeatureDesktop, "Modifica dal desktop", tab, () => FeatureTabOpen, () =>
                {
                    SetNotice("«" + model.Name + "» (" + model.Info.FeatureType + ") non si modifica in XR: modificala dal desktop in Inventor.");
                    Refresh();
                }, () => "Nessuna feature aperta."));

            var list = new List<XrAction>();
            int room = ActionCatalog.MaxPalette - fixedActions.Count;
            for (int i = 0; i < model.Chips.Count && list.Count < room; i++)
            {
                var chip = model.Chips[i];
                string label = chip.Editable || chip.Expression == null ? chip.Name + " = " + Fmt(chip.Value) + " " + (chip.Unit == "deg" ? "°" : chip.Unit == "mm" ? "mm" : "")
                    : chip.Name + " = " + chip.Expression + " (espressione)";
                list.Add(new XrAction(IdFeaturePrefix + "p." + i, label, tab, () => _featureEdit == model && chip.Editable && FeatureChipsOn,
                    () => AskFeature("Feature " + model.Name, chip.Name + " (" + chip.Unit + ")", chip.Value, chip.Unit, v => SetFeatureValue(chip.Name, v)),
                    () => chip.ReadOnlyReason ?? CommandReason(), kind: XrActionKind.Numeric));
            }
            for (int i = 0; i < model.Chips.Count && list.Count < room; i++)
            {
                var chip = model.Chips[i];
                if (chip.SourceParameter == null || model.BlockReason != null) continue;
                string source = chip.SourceParameter;
                list.Add(new XrAction(IdFeaturePrefix + "src." + i, "Modifica " + source, tab, () => _featureEdit == model && FeatureChipsOn,
                    () => EditSourceParameter(source), CommandReason));
            }
            list.AddRange(fixedActions);
            return list;
        }

        private void EditSourceParameter(string name)
        {
            var parameter = _designContext?.Parameters.OfType<JObject>().FirstOrDefault(p => (string)p["name"] == name);
            if (parameter == null) { SetNotice("Parametro «" + name + "» non trovato."); Refresh(); return; }
            string unit = parameter["value_mm"] != null ? "mm" : parameter["value_deg"] != null ? "deg" : "ul";
            double value = (double?)(parameter["value_mm"] ?? parameter["value_deg"] ?? parameter["value_unitless"]) ?? 0;
            AskFeature("Parametro " + name, unit, value, unit, v => SetFeatureSource(name, v, unit));
        }
    }
}
