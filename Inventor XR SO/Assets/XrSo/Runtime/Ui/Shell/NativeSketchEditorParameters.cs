using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Unity.Ui
{
    public sealed partial class NativeSketchEditorUi
    {
        public const string ParametersTab = "_sketch.parameters", ParameterEditTab = "_sketch.parameter.edit";
        private readonly IDesignWorkspaceBackend _parameterBackend;
        private CancellationTokenSource _parameterRead;
        private NativeSketchSnapshot _parameterSnapshot;
        private DesignContext _parameterContext;
        private JObject _parameterSelection;
        private bool _parametersLoading, _parameterInput;
        private int _parameterGeneration;
        public TextEntry ActiveParameterTextEntry => _parameterInput ? ActiveTextEntry : null;
        private bool ParameterIdle => !_disposed && _ready() && _presented() && IsOpen
            && Session.CanEdit && !Session.HasDraft && Session.Mutation.Status == DesignStatus.Empty && !WindowActive;
        private bool ParameterContextCurrent => ParameterIdle && !_parametersLoading && _parameterContext != null
            && ReferenceEquals(_parameterSnapshot, Session.Snapshot)
            && _parameterContext.State.DocumentId == Session.Snapshot.State.DocumentId
            && _parameterContext.State.Revision == Session.Snapshot.State.Revision;

        private void CancelParameterInput()
        {
            if (!_parameterInput) return;
            _parameterInput = false; ++_promptGeneration;
            _parameterSelection = null;
            _textPrompt?.CancelEdit(); _textPrompt = null; _palette()?.HideKeypad();
        }
        private void ResetParameterResources()
        {
            ++_parameterGeneration;
            _parameterRead?.Cancel(); _parameterRead?.Dispose(); _parameterRead = null;
            CancelParameterInput();
            _parametersLoading = false; _parameterSnapshot = null; _parameterContext = null; _parameterSelection = null;
        }
        private void RefreshParameterContextState()
        {
            if (_parameterSnapshot != null && (!ParameterIdle || !ReferenceEquals(_parameterSnapshot, Session.Snapshot)))
                ResetParameterResources();
        }

        /// <summary>Read the complete current parameter context; no preview or mutation is requested.</summary>
        public async Task OpenParametersAsync()
        {
            if (_parameterBackend == null || !CanCreate || _parametersLoading) return;
            ResetParameterResources(); CancelPrompt(); CancelCandidates(); ClearSelected();
            var snapshot = Session.Snapshot;
            _parameterSnapshot = snapshot; _parametersLoading = true;
            _parameterRead = new CancellationTokenSource();
            var token = _parameterRead.Token; int generation = _parameterGeneration;
            _notice("Leggo i parametri nativi della revisione corrente."); _changed();
            try
            {
                var context = await _parameterBackend.GetDesignContextAsync(snapshot.State, token);
                if (_disposed || token.IsCancellationRequested || generation != _parameterGeneration
                    || !ParameterIdle || !ReferenceEquals(snapshot, Session.Snapshot)) return;
                if (context == null || context.Truncated || context.State.DocumentId != snapshot.State.DocumentId
                    || context.State.Revision != snapshot.State.Revision)
                    throw new InvalidOperationException("Elenco parametri incompleto o riferito a un'altra revisione.");
                _parameterContext = context;
                _palette()?.ShowTabGroup(new[] { ParametersTab }, true);
                _notice("Parametri nativi: seleziona un parametro oppure creane uno utente. Anteprima e Applica restano espliciti.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!_disposed && generation == _parameterGeneration) _notice(ex.Message);
            }
            finally
            {
                if (!_disposed && generation == _parameterGeneration) { _parametersLoading = false; _changed(); }
            }
        }

        private IEnumerable<XrAction> ParameterActions()
        {
            RefreshParameterContextState();
            string reason() => _parameterBackend == null ? "Il backend non espone il contesto parametri."
                : _parametersLoading ? "Lettura dei parametri in corso."
                : !ParameterIdle ? "Apri uno schizzo attuale e completa o annulla la bozza corrente."
                : "Rileggi i parametri della revisione corrente.";
            yield return new XrAction("sketch.native.AppParametersCmd", "Parametri", SketchCommandCatalog.TabPrefix + "Parametri",
                () => _parameterBackend != null && CanCreate && !_parametersLoading,
                async () => await OpenParametersAsync(), reason, icon: "parameters");
            var context = _parameterContext;
            bool Current() => ReferenceEquals(context, _parameterContext) && ParameterContextCurrent && !PromptOpen;
            yield return new XrAction("sketch.parameters.refresh", "Rileggi parametri", ParametersTab,
                () => _parameterBackend != null && CanCreate && !_parametersLoading,
                async () => await OpenParametersAsync(), reason);
            yield return new XrAction("sketch.parameter.create", "Crea parametro utente", ParametersTab, Current,
                () => PromptParameterFields("Nuovo parametro utente", new[] { "Nome", "Espressione Inventor", "Unità nativa (mm, deg, ul, Boolean, Text…)" },
                    new[] { "", "", "" }, new[] { NativeParameterOperations.MaxNameLength, NativeParameterOperations.MaxExpressionLength, NativeParameterOperations.MaxUnitLength },
                    values => Session.CreateParameter(values[0], values[1], values[2])), reason);
            if (context != null)
            {
                int index = 0;
                foreach (var parameter in context.Parameters.OfType<JObject>().Where(p => p["name"]?.Type == JTokenType.String))
                {
                    var frozen = (JObject)parameter.DeepClone();
                    yield return new XrAction("sketch.parameter.pick." + index++, DescribeParameter(frozen), ParametersTab, Current,
                        () => { _parameterSelection = frozen; _palette()?.ShowTabGroup(new[] { ParameterEditTab }, true); _changed(); }, reason);
                }
            }
            var selected = _parameterSelection;
            bool SelectedCurrent() => Current() && selected != null && ReferenceEquals(selected, _parameterSelection);
            yield return new XrAction("sketch.parameter.info", "Valore ed espressione correnti", ParameterEditTab, SelectedCurrent,
                () => _notice(DescribeParameter(selected)), reason);
            yield return new XrAction("sketch.parameter.expression", "Modifica espressione nativa", ParameterEditTab, SelectedCurrent,
                () => PromptParameterFields((string)selected["name"], new[] { "Espressione Inventor con unità" },
                    new[] { (string)selected["expression"] ?? "" }, new[] { NativeParameterOperations.MaxExpressionLength },
                    values => Session.SetParameterExpression((string)selected["name"], values[0])), reason);
            yield return new XrAction("sketch.parameter.rename", "Rinomina parametro", ParameterEditTab, SelectedCurrent,
                () => PromptParameterFields("Rinomina parametro", new[] { "Nuovo nome" }, new[] { (string)selected["name"] },
                    new[] { NativeParameterOperations.MaxNameLength }, values => Session.RenameParameter((string)selected["name"], values[0])), reason);
            yield return new XrAction("sketch.parameter.delete", "Prepara eliminazione", ParameterEditTab, SelectedCurrent,
                () => { Session.DeleteParameter((string)selected["name"]); _notice("Eliminazione pronta: Anteprima verifica le dipendenze native; poi Applica."); _changed(); }, reason);
            yield return new XrAction("sketch.parameter.input.cancel", "Annulla input parametro", ParameterEditTab,
                () => _parameterInput && !_disposed && _presented(),
                () => { CancelPrompt(); _changed(); }, () => "Nessun input parametro aperto.");
        }

        private void PromptParameterFields(string title, string[] labels, string[] initial, int[] limits, Action<string[]> done)
        {
            if (!ParameterContextCurrent || PromptOpen) return;
            CancelPrompt(); CancelCandidates();
            var snapshot = Session.Snapshot; var context = _parameterContext;
            int generation = ++_promptGeneration, index = 0;
            var values = (string[])initial.Clone();
            bool Current() => generation == _promptGeneration && ParameterContextCurrent
                && ReferenceEquals(snapshot, Session.Snapshot) && ReferenceEquals(context, _parameterContext) && _parameterInput;
            void Next()
            {
                string original = values[index] ?? "";
                bool editable = original.Length <= limits[index] && !original.Any(char.IsControl);
                var entry = new TextEntry("sketch.parameter.input." + index, editable ? original : "", limits[index]);
                _textPrompt = entry; _parameterInput = true;
                entry.Committed += () =>
                {
                    if (!Current() || _textPrompt != entry) return;
                    _textPrompt = null; values[index++] = entry.Value;
                    if (index < labels.Length) { Next(); return; }
                    _parameterInput = false;
                    try { done(values); _notice("Operazione parametro pronta: Anteprima verifica espressione, unità e dipendenze; poi Applica."); }
                    catch (Exception ex) { _notice(ex.Message); }
                    _changed();
                };
                if (!editable) _notice("Espressione multilinea o troppo lunga: inserisci esplicitamente una sostituzione; il CAD resta invariato.");
                if (_palette() != null) _palette().ShowTextKeypad(entry, title + " • " + labels[index]); else entry.BeginEdit();
                _changed();
            }
            Next();
        }

        private static string DescribeParameter(JObject parameter)
        {
            string value;
            if (parameter["value_bool"]?.Type == JTokenType.Boolean) value = (bool)parameter["value_bool"] ? "True" : "False";
            else if (parameter["value_text"]?.Type == JTokenType.String) value = "\"" + (string)parameter["value_text"] + "\"";
            else
            {
                var token = new[] { "value_mm", "value_deg", "value_mm2", "value_mm3", "value_unitless", "value_internal" }
                    .Select(key => parameter[key]).FirstOrDefault(v => v != null && (v.Type == JTokenType.Float || v.Type == JTokenType.Integer)
                        && !double.IsNaN((double)v) && !double.IsInfinity((double)v));
                value = token == null ? "valore non disponibile" : ((double)token).ToString("0.###", CultureInfo.InvariantCulture);
            }
            return (string)parameter["name"] + " = " + value + " • " + ((string)parameter["unit"] ?? "unità non disponibile")
                + " • " + ((string)parameter["expression"] ?? "espressione non disponibile");
        }
    }
}
