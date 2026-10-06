#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace InventorXrSo.Xr
{
    /// <summary>Native M6 fixture regression plus offline icon, real-provider presentation and rebuild checks.</summary>
    internal sealed class M10QuestAcceptance : M6QuestAcceptance
    {
        internal new static readonly string[] ReflectedMembers = {
            "AppController._design", "AppController._lamiera", "AppController._assembly",
            "AppController._inspect", "AppController._document", "AppController._view", "AppController._shell"
        };
        protected override string Milestone => "m10";
        protected override string FixtureMilestone => "m6";
        protected override int TimeoutSeconds => 900;
        protected override string CompletionNote => "SYNTHETIC UI/input on dedicated M6 Inventor fixture; see NOT COVERED for device performance baseline and physical MR/VR";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M10QuestAcceptance>("xr_m10_acceptance");

        private sealed class Provider : IActionProvider
        {
            public IReadOnlyList<XrTab> Tabs { get; } = new[] { new XrTab("icons", "Icone Inventor") };
            public readonly List<XrAction> Items = new List<XrAction>();
            public IEnumerable<XrAction> Actions => Items;
            public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Items;
            public CommitBarState CommitBar => null;
        }

        protected override async Task Run(CancellationToken ct)
        {
            await WaitForFixture(ct);
            RequireFixture();
            var sprites = Resources.LoadAll<Sprite>("InventorIcons");
            Check(sprites.Length == 34, "all 34 selected native sprites load offline");
            foreach (var sprite in sprites)
            {
                Check(sprite.rect.size == new Vector2(32, 32), "native 32px icon: " + sprite.name);
                Check(InventorIcons.TryGet(sprite.name, out var cached) && ReferenceEquals(sprite, cached), "shared resource: " + sprite.name);
            }
            Pass("M10-02", "34 local dark-theme native sprites, dimensions and shared cache verified");
            CheckIconStates();
            await CheckProviders(ct);
            // Existing runner exercises native preview/apply/undo and the shared voice/controller routing.
            await base.Run(ct);
            Pass("M10-05/07-native", "M6 four-workspace native regression finished; individual inherited PASS/NOT COVERED entries retain their exact scope");
            await CheckProviders(ct);
            await CheckRebuilds(sprites, ct);
            var shell = Read<UiShell>(App, "_shell");
            var report = await M10UiPerformance.Run(shell.Palette, shell.Catalog, ct);
            foreach (var sample in report.samples)
                Record("Performance " + sample.presentation + ": frames=" + sample.frames + ", frameP95Ms=" + sample.frameP95Ms +
                    ", gpuP95Ms=" + sample.gpuP95Ms + " (samples=" + sample.gpuSamples + "), gcP95Bytes=" + sample.gcP95Bytes +
                    " (samples=" + sample.gcSamples + "), cpuP95Ms=" + sample.cpuP95Ms + " (samples=" + sample.cpuSamples + "), memory=" + sample.totalAllocatedMemory);
            Pass("M10-08-profile-capture", "A/B/A controlled M8 text versus M10 icons: 90 warm-up + 240 measured frames per phase; raw JSON saved, unsupported counters remain unavailable");
            if (report.samples.Any(s => s.cpuSamples != 240)) NotCovered("M10-08-CPU", "CPU Main Thread Frame Time counter unavailable for all samples; frame interval does not substitute CPU execution time");
            if (report.samples.Any(s => s.gpuSamples != 240)) NotCovered("M10-08-GPU", "XR plugin did not supply GPU timings for all samples");
            if (report.samples.Any(s => s.gcSamples != 240)) NotCovered("M10-08-GC", "release runtime did not supply GC allocation counters for all samples");
            CheckProfileBudget(report);
            NotCovered("M10-09", "person wearing Quest in MR/Studio VR, real controller hover, readability and comfort remain a physical trial");
        }

        private void CheckProfileBudget(M10UiPerformance.Report report)
        {
            var a = report.samples[0]; var b = report.samples[1]; var c = report.samples[2];
            if (report.samples.Any(s => s.cpuSamples != 240 || s.gpuSamples != 240 || s.gcSamples != 240))
            { NotCovered("M10-08-budget", "full comparison requires all CPU/GPU/GC samples; available counters remain in raw report"); return; }
            float cpuControl = Mathf.Max(a.cpuP95Ms, c.cpuP95Ms), gpuControl = Mathf.Max(a.gpuP95Ms, c.gpuP95Ms);
            float cpuAllowance = Mathf.Max(.5f, cpuControl * .1f), gpuAllowance = Mathf.Max(.5f, gpuControl * .1f);
            if (Mathf.Abs(a.cpuP95Ms - c.cpuP95Ms) > cpuAllowance || Mathf.Abs(a.gpuP95Ms - c.gpuP95Ms) > gpuAllowance)
            { NotCovered("M10-08-budget", "A/A text controls differ beyond 0.5 ms / 10%; scene/device workload not stable enough to accept A/B budget"); return; }
            Check(b.cpuP95Ms <= cpuControl + cpuAllowance, "icon CPU p95 increase within max(0.5 ms, 10%) text control");
            Check(b.gpuP95Ms <= gpuControl + gpuAllowance, "icon GPU p95 increase within max(0.5 ms, 10%) text control");
            Check(b.gcP95Bytes <= Math.Max(a.gcP95Bytes, c.gcP95Bytes) + 128, "steady-state GC p95 increase <=128 bytes/frame");
            Pass("M10-08-budget", "stable A/B/A controlled M8 text geometry versus M10 icons: CPU/GPU p95 delta <=max(0.5 ms,10%); steady GC delta <=128 bytes/frame");
        }

        private async Task CheckProviders(CancellationToken ct)
        {
            var providers = new IActionProvider[] {
                Read<DesignWorkspace>(App, "_design"), Read<LamieraWorkspace>(App, "_lamiera"),
                Read<AssemblyWorkspace>(App, "_assembly"), Read<InspectWorkspace>(App, "_inspect"),
                Read<DocumentActions>(App, "_document"), Read<ViewActions>(App, "_view")
            };
            var palette = PaletteView.Create(null);
            var ring = RingView.Create(null);
            try
            {
                int iconActions = 0;
                foreach (var provider in providers)
                {
                    Check(provider != null, "actual application action provider exists");
                    var catalog = new ActionCatalog(provider);
                    palette.Render(catalog);
                    foreach (var tab in provider.Tabs.Where(t => !t.Hidden))
                    {
                        palette.ShowTab(tab.Id);
                        await Task.Delay(30, ct);
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)palette.Canvas.transform);
                        foreach (var action in catalog.Palette(tab.Id))
                        {
                            if (string.IsNullOrEmpty(action.Icon)) continue;
                            iconActions++;
                            var button = palette.GetComponentsInChildren<ThemedButton>().Single(b => b.Label.text == action.Label);
                            Check(button.Icon != null && !button.Label.gameObject.activeSelf, "actual command uses icon: " + action.Id);
                            Check(((RectTransform)button.transform).rect.size == new Vector2(75.5f, 20), "palette hit area: " + action.Id);
                            Check(button.interactable == action.Enabled, "availability unchanged: " + action.Id);
                            Hover(button, action);
                        }
                    }
                    foreach (var selection in new[] { SelectionKind.PlanarFace, SelectionKind.Face, SelectionKind.Edge, SelectionKind.Component })
                    {
                        var actions = provider.ContextActions(selection).ToArray();
                        ring.Show(Vector3.zero, actions, null);
                        foreach (var button in ring.GetComponentsInChildren<ThemedButton>())
                        {
                            Check(((RectTransform)button.transform).sizeDelta == RingView.ButtonMm, "ring hit area unchanged");
                            var action = actions.Single(a => a.Label == button.Label.text);
                            if (!string.IsNullOrEmpty(action.Icon)) { Check(button.Icon != null, "ring native icon"); Hover(button, action); }
                        }
                    }
                    Record("UI provider " + provider.GetType().Name + ": SYNTHETIC hover, no CAD command invoked by presentation probe");
                }
                Check(iconActions >= 40, "mapped commands across all six providers");
                Pass("M10-03/04/06", "actual six providers: icon/hit area, enabled/disabled hints and palette/ring presentation; synthetic hover");
            }
            finally { Destroy(palette.gameObject); Destroy(ring.gameObject); }
        }

        private static void Hover(ThemedButton button, XrAction action)
        {
            ExecuteEvents.Execute<IPointerEnterHandler>(button.gameObject, null, (h, e) => h.OnPointerEnter(null));
            var hint = button.GetComponent<ActionTooltip>();
            Check(hint.Visible && hint.Text.Contains(action.Label), "complete command name on hover");
            if (!action.Enabled) Check(hint.Text.Contains(action.DisabledReason), "disabled reason on hover");
            foreach (var text in button.GetComponentInParent<Canvas>().GetComponentsInChildren<TextMeshProUGUI>())
            {
                if (text.transform.parent.name != "Suggerimento azione") continue;
                text.ForceMeshUpdate();
                Check(!text.enableAutoSizing && !text.isTextTruncated, "hint is not shrunk/truncated");
                Check(!text.raycastTarget, "hint does not intercept pointer");
                Check(text.textBounds.size.x <= text.rectTransform.rect.width + 0.1f &&
                    text.textBounds.size.y <= text.rectTransform.rect.height + 0.1f, "hint glyph bounds fit");
            }
            ExecuteEvents.Execute<IPointerExitHandler>(button.gameObject, null, (h, e) => h.OnPointerExit(null));
            Check(!hint.Visible, "hint clears after hover");
        }

        private void CheckIconStates()
        {
            var canvas = UiFactory.WorldCanvas(null, "M10 states", new Vector2(400, 400));
            int calls = 0;
            try
            {
                Check(Resources.Load<TextAsset>("InventorIconsManifest") != null && Resources.Load<TextAsset>("InventorIconsNotice") != null,
                    "icon provenance and notice ship offline in APK");
                var disabled = new XrAction("probe.disabled", "Comando disabilitato", "icons", () => false, () => calls++,
                    () => "Offline — CAD non disponibile.", icon: "undo");
                var button = (ThemedButton)UiFactory.ActionButton(canvas.transform, disabled, 7, 16, () => disabled.TryInvoke());
                Hover(button, disabled);
                button.onClick.Invoke();
                Check(calls == 0 && !button.interactable && button.Icon.color.a < 1, "disabled hover cannot invoke CAD");
                var toggle = new XrAction("probe.toggle", "Sezione", "icons", () => true, () => calls++,
                    kind: XrActionKind.Toggle, icon: "section", isOn: () => true);
                var selected = (ThemedButton)UiFactory.ActionButton(canvas.transform, toggle, 14, 32, () => toggle.TryInvoke());
                Hover(selected, toggle);
                Check(selected.Primary && selected.Icon.transform.parent.GetComponent<Image>().color == UiTheme.Navy, "toggle state and native glyph backdrop");
                var fallback = new XrAction("probe.fallback", "Altezza: 12,5 mm", "icons", () => true, () => calls++,
                    kind: XrActionKind.Numeric, icon: "missing-m10-icon");
                var numeric = (ThemedButton)UiFactory.ActionButton(canvas.transform, fallback, 7, 16, () => fallback.TryInvoke());
                Check(numeric.Icon == null && numeric.Label.gameObject.activeSelf && numeric.Label.text == fallback.Label, "missing icon retains full numeric text");
                numeric.onClick.Invoke();
                Check(calls == 1, "fallback callback exactly once");
                Pass("M10-04-states", "offline notice/provenance, disabled synthetic hover with no invocation, toggle and missing-icon numeric fallback");
            }
            finally { Destroy(canvas.gameObject); }
        }

        private async Task CheckRebuilds(Sprite[] sprites, CancellationToken ct)
        {
            var provider = new Provider();
            foreach (var sprite in sprites.Take(8))
                provider.Items.Add(new XrAction("probe." + sprite.name, sprite.name, "icons", () => true, () => { }, icon: sprite.name));
            var palette = PaletteView.Create(null);
            try
            {
                var catalog = new ActionCatalog(provider);
                palette.Render(catalog);
                await Task.Delay(150, ct);
                foreach (var b in palette.GetComponentsInChildren<ThemedButton>()) Hover(b, provider.Items.Single(a => a.Label == b.Label.text));
                await Task.Delay(150, ct);
                int materials = Resources.FindObjectsOfTypeAll<Material>().Length;
                int textures = Resources.FindObjectsOfTypeAll<Texture>().Length;
                int spriteCount = Resources.FindObjectsOfTypeAll<Sprite>().Length;
                long memoryBefore = Profiler.GetTotalAllocatedMemoryLong();
                for (int i = 0; i < 100; i++) { palette.Render(catalog); await Task.Delay(30, ct); }
                await Task.Delay(150, ct);
                Check(Resources.FindObjectsOfTypeAll<Material>().Length <= materials, "100 rebuilds: materials stable");
                Check(Resources.FindObjectsOfTypeAll<Texture>().Length <= textures, "100 rebuilds: textures stable");
                Check(Resources.FindObjectsOfTypeAll<Sprite>().Length <= spriteCount, "100 rebuilds: sprites stable");
                foreach (var b in palette.GetComponentsInChildren<ThemedButton>())
                    Check(ReferenceEquals(b.Icon.sprite, sprites.Single(s => s.name == b.Icon.sprite.name)), "sprite reused after rebuilds");
                Record("Resource snapshot: materials=" + materials + ", textures=" + textures + ", sprites=" + spriteCount +
                    "; totalAllocatedMemory before=" + memoryBefore + ", after=" + Profiler.GetTotalAllocatedMemoryLong() + " (observation, not a frame budget)");
                Pass("M10-08-rebuild", "100 runtime rebuilds without material/texture/sprite growth; local sprites reused");
            }
            finally { Destroy(palette.gameObject); }
            await CaptureScreenshot("after-regression", ct);
        }
    }
}
#endif
