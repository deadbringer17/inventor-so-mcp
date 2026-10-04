using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Selection;
using InventorXrSo.Core.Verify;
using InventorXrSo.Unity.Scene;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// M7 part of Ispeziona: visibility (X-Ray, isolate, hide) and the Inventor verifications (interference, minimum distance,
    /// health and BOM). Read-only: nothing here changes the CAD. Results are bound to the revision they were computed on.
    /// </summary>
    public sealed partial class InspectWorkspace
    {
        private IVerifyBackend _verify;
        private readonly VerifySession _verifySession = new VerifySession();
        private CancellationTokenSource _verifyRequests = new CancellationTokenSource();
        private ComponentVisibility _visibility;
        private VerifyOverlay _overlay;
        private IReadOnlyList<VerifyFinding> _findings = Array.Empty<VerifyFinding>();
        private IVerifyJob _findingsJob;
        private bool _findingsStale, _scopeSelection;
        private SceneNode _distanceA;
        private IReadOnlyDictionary<string, OccurrenceVisibility> _focusSnapshot;
        private float _runningSince;
        private int _runningShown = -1;

        private void InitializeVerify(Material lineMaterial)
        {
            _visibility = _view.gameObject.AddComponent<ComponentVisibility>();
            _visibility.Initialize(_view);
            _overlay = new GameObject("Verifiche").AddComponent<VerifyOverlay>();
            _overlay.transform.SetParent(_view.transform, false);
            _overlay.Initialize(lineMaterial, _head);
            _verifySession.Interference.Changed += () => OnFindingsJob(_verifySession.Interference, VerifyFindings.FromInterference, VerifyFindings.Summary);
            _verifySession.Health.Changed += () => OnFindingsJob(_verifySession.Health, VerifyFindings.FromHealth, VerifyFindings.Summary);
            _verifySession.Distance.Changed += OnDistanceChanged;
        }

        private void BindVerify(IInspectionBackend backend)
        {
            _verify = backend as IVerifyBackend;
            ResetVerify();
        }

        /// <summary>New document or new backend: in-flight answers are ignored, results and drawings go away.</summary>
        private void ResetVerify()
        {
            _verifySession.Reset();
            _findings = Array.Empty<VerifyFinding>(); _findingsJob = null; _findingsStale = false;
            _distanceA = null; _focusSnapshot = null;
            _overlay?.Clear(); _visibility?.ShowAll();
        }

        /// <summary>Leaving Ispeziona: the scene goes back to normal; results stay listed.</summary>
        private void LeaveVerifyView()
        {
            _focusSnapshot = null; _distanceA = null;
            _overlay?.Clear(); _visibility?.ShowAll();
        }

        // ---------------------------------------------------------------- availability

        private bool IsAssembly => _scene?.Graph.Kind == "assembly";

        private bool VerifyReady => Backend && _verify != null && IsAssembly && _documentState != null && _verifySession.Gate.CanStart;

        private string VerifyReason()
        {
            if (!Active) return "Ispeziona non è aperto.";
            if (!_online) return "Offline — verifiche di Inventor non disponibili.";
            if (_scene == null) return "Nessun documento disponibile.";
            if (!IsAssembly) return "Serve un assieme.";
            if (_verify == null) return "Verifiche non disponibili su questo server.";
            return _verifySession.Gate.Reason ?? BackendReason();
        }

        /// <summary>A direct occurrence of the root, selected at the root context: what the M7 tools accept.</summary>
        private bool DirectSelection => _selected != null && _context.Path.Count == 1 && _context.Current != null
            && _context.Current.Children.Contains(_selected) && !_selected.Suppressed;

        private IEnumerable<string> LeafIds(SceneNode node) =>
            node == null ? Enumerable.Empty<string>() : BrowserContext.Descendants(node).Where(n => n.DefinitionKind == "part").Select(n => n.OccurrenceId);

        private IEnumerable<string> LeafIds(string occurrenceId) => LeafIds(_context.Find(occurrenceId));

        private IEnumerable<CadInstance> Instances(IEnumerable<string> leafIds) => leafIds.Select(_view.Find).Where(i => i != null);

        // ---------------------------------------------------------------- visibility

        private void VisibilityOnSelection(Action<IEnumerable<string>> apply, string notice)
        {
            ClearFocus();
            apply(LeafIds(_selected).ToArray());
            SetNotice(notice + ": " + _selected.Name);
            Refresh();
        }

        private void ShowAllComponents()
        {
            ClearFocus();
            _visibility.ShowAll();
            SetNotice("Tutti i componenti visibili.");
            Refresh();
        }

        // ---------------------------------------------------------------- verifications

        private void BeginRun()
        {
            ClearFocus(); ClosePicker(false);
            _runningSince = Time.unscaledTime; _runningShown = -1;
        }

        private async void RunInterference()
        {
            var state = _documentState;
            IReadOnlyList<string> scope = _scopeSelection && DirectSelection ? new[] { _selected.OccurrenceId } : null;
            BeginRun();
            await _verifySession.Interference.RunAsync(ct => _verify.CheckInterferenceAsync(state, scope, ct), _verifyRequests.Token);
        }

        private async void RunHealth()
        {
            var state = _documentState;
            BeginRun();
            await _verifySession.Health.RunAsync(ct => _verify.GetAssemblyHealthAsync(state, ct), _verifyRequests.Token);
        }

        private async void Distance()
        {
            if (_distanceA == null)
            {
                _distanceA = _selected;
                _overlay.ClearDistance();
                SetNotice("Distanza minima da " + _distanceA.Name + ": seleziona il secondo componente e premi di nuovo.");
                Refresh();
                return;
            }
            if (_selected == _distanceA) { SetNotice("Scegli un componente diverso da " + _distanceA.Name + "."); Refresh(); return; }
            var state = _documentState; string a = _distanceA.OccurrenceId, b = _selected.OccurrenceId;
            BeginRun();
            await _verifySession.Distance.RunAsync(ct => _verify.MeasureMinDistanceAsync(state, a, b, ct), _verifyRequests.Token);
        }

        private void IgnoreRunning()
        {
            _verifySession.Running?.Ignore();
            SetNotice("Risultato ignorato. Inventor completa comunque il calcolo in corso.");
            Refresh();
        }

        private void OnFindingsJob<T>(VerifyJob<T> job, Func<T, IReadOnlyList<VerifyFinding>> rows, Func<T, string> summary) where T : class, IVerifyResult
        {
            switch (job.Status)
            {
                case VerifyStatus.Done:
                    _findings = rows(job.Result); _findingsJob = job; _findingsStale = false;
                    SetNotice(summary(job.Result) + (_findings.Count > 0 ? "\nApri Risultati per vederli uno a uno." : ""));
                    break;
                case VerifyStatus.Failed: SetNotice(job.ErrorMessage); break;
                case VerifyStatus.Stale:
                    if (_findingsJob == job) _findingsStale = true;
                    SetNotice("Modello cambiato: rilancia la verifica.");
                    break;
            }
            Refresh();
        }

        private void OnDistanceChanged()
        {
            var job = _verifySession.Distance;
            switch (job.Status)
            {
                case VerifyStatus.Done:
                {
                    var report = job.Result;
                    var a = _context.Find(_distanceA?.OccurrenceId ?? ""); var b = _selected;
                    string value = VerifyFindings.Millimetres(report.DistanceMm) + " mm";
                    if (report.HasPoints) _overlay.ShowDistance(VerifyOverlay.ToLocal(report.PointAMm), VerifyOverlay.ToLocal(report.PointBMm), value);
                    else if (VerifyOverlay.ClosestVertices(Instances(LeafIds(a)), Instances(LeafIds(b)), _view.transform, out var pa, out var pb))
                        _overlay.ShowDistance(pa, pb, value + " (linea indicativa)");
                    SetNotice("Distanza minima (Inventor): " + value + (report.HasPoints ? "" : "\nLinea indicativa: punti calcolati sulle mesh del visore."));
                    _distanceA = null;
                    break;
                }
                case VerifyStatus.Failed: SetNotice(job.ErrorMessage); _distanceA = null; break;
                case VerifyStatus.Stale: _overlay.ClearDistance(); SetNotice("Modello cambiato: rilancia la distanza minima."); break;
            }
            Refresh();
        }

        /// <summary>HUD clock while Inventor computes.</summary>
        private void TickVerify()
        {
            if (_verifySession.Running == null) return;
            int seconds = (int)(Time.unscaledTime - _runningSince);
            if (seconds == _runningShown) return;
            _runningShown = seconds;
            SetNotice("Verifica in corso in Inventor… " + seconds + " s");
        }

        // ---------------------------------------------------------------- results

        private void OpenResults()
        {
            OpenPicker("Risultati", _findings.Select(f =>
            {
                var finding = f;
                string label = (_findingsStale ? "[obsoleto] " : "") + (finding.Severity == FindingSeverity.Error ? "● " : "○ ") + finding.Title;
                return new PickerItem(label, () => FocusFinding(finding));
            }));
        }

        private void FocusFinding(VerifyFinding finding)
        {
            if (_focusSnapshot == null) _focusSnapshot = _visibility.Snapshot();
            _overlay.Clear();
            var leaves = finding.OccurrenceIds.SelectMany(LeafIds).Distinct().ToArray();
            if (leaves.Length > 0)
            {
                _visibility.Isolate(leaves);
                var instances = Instances(leaves).ToArray();
                _overlay.Tint(instances);
                var bounds = InspectionGeometry.InstancesBounds(_view.transform, instances);
                if (bounds.HasValue) FocusOn(bounds.Value);
            }
            _overlay.ShowBoxes(finding.Boxes);
            SetNotice(finding.Title + "\n" + finding.Detail + (_findingsStale ? "\nRisultato obsoleto: rilancia la verifica." : "") + "\nIndietro per tornare alla vista.");
            Refresh();
        }

        /// <summary>True when a row was in focus and the view went back to its previous state.</summary>
        private bool ClearFocus()
        {
            if (_focusSnapshot == null) return false;
            _visibility.Restore(_focusSnapshot);
            _focusSnapshot = null;
            _overlay.Clear();
            return true;
        }

        /// <summary>Brings the given model-local bounds in front of the user. View only.</summary>
        private void FocusOn(Bounds bounds)
        {
            if (_head == null) return;
            var root = _view.transform;
            var scaled = new Bounds(bounds.center * root.localScale.x, bounds.size * root.localScale.x);
            var pose = ScenePlacement.InFront(scaled, _head.position, _head.forward);
            root.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void DisposeVerify()
        {
            _verifyRequests.Cancel(); _verifyRequests.Dispose();
        }
    }
}
