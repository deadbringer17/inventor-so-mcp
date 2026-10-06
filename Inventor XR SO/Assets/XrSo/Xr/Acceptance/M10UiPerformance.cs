#if XR_SO_ACCEPTANCE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.XR;

namespace InventorXrSo.Xr
{
    /// <summary>Controlled A/B: identical CAD scene, palette pose, theme and labels, with M8 text presentation versus M10 icons.</summary>
    internal static class M10UiPerformance
    {
        [Serializable] internal sealed class Sample
        {
            public string presentation;
            public int frames, gpuSamples, gcSamples, cpuSamples;
            public float frameP95Ms, gpuP95Ms, cpuP95Ms;
            public long gcP95Bytes, totalAllocatedMemory;
        }
        [Serializable] internal sealed class Report
        {
            public string method = "Same M10 APK and unchanged fixture; M8 text geometry versus M10 icons. Warm 90 frames, sample 240 frames, A/B/A; no hover or rebuild during samples. Frame interval includes XR pacing and is not CPU execution time.";
            public string device = SystemInfo.deviceModel;
            public string unity = Application.unityVersion;
            public Sample[] samples;
        }
        private sealed class Provider : IActionProvider
        {
            public IReadOnlyList<XrTab> Tabs { get; } = new[] { new XrTab("m10-bench", "Feature") };
            public readonly List<XrAction> Items = new List<XrAction>();
            public IEnumerable<XrAction> Actions => Items;
            public IEnumerable<XrAction> ContextActions(SelectionKind selection) => Array.Empty<XrAction>();
            public CommitBarState CommitBar => null;
        }
        internal static async Task<Report> Run(PaletteView palette, ActionCatalog original, CancellationToken ct)
        {
            string tab = palette.CurrentTab;
            bool visible = palette.Canvas.gameObject.activeSelf;
            var provider = new Provider();
            var keys = new[] { "extrude", "hole", "fillet", "chamfer", "parameters", "undo", "redo", "refresh" };
            var labels = new[] { "Estrusione", "Foro", "Raccordo", "Smusso", "Parametri", "Annulla modifica XR", "Ripeti modifica XR", "Aggiorna" };
            var report = new Report { samples = new Sample[3] };
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            var display = displays.FirstOrDefault(d => d.running);
            try
            {
                palette.Canvas.gameObject.SetActive(true);
                for (int phase = 0; phase < 3; phase++)
                {
                    bool icons = phase == 1;
                    provider.Items.Clear();
                    for (int i = 0; i < keys.Length; i++)
                        provider.Items.Add(new XrAction("bench." + keys[i], labels[i], "m10-bench", () => true, () => { }, icon: icons ? keys[i] : null));
                    palette.Render(new ActionCatalog(provider));
                    Canvas.ForceUpdateCanvases();
                    for (int i = 0; i < 90; i++) await NextFrame(ct);
                    report.samples[phase] = await Capture(icons ? "M10-icons" : "M8-text-control-" + phase, display, ct);
                }
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "m10-acceptance-performance.json"), JsonUtility.ToJson(report, true));
                return report;
            }
            finally { palette.Render(original); palette.ShowTab(tab); palette.Canvas.gameObject.SetActive(visible); }
        }

        private static async Task NextFrame(CancellationToken ct)
        {
            int frame = Time.frameCount;
            do { ct.ThrowIfCancellationRequested(); await Task.Yield(); } while (Time.frameCount == frame);
        }

        private static async Task<Sample> Capture(string label, XRDisplaySubsystem display, CancellationToken ct)
        {
            var frames = new List<float>(240);
            var gpu = new List<float>(240);
            var gc = new List<long>(240);
            var cpu = new List<float>(240);
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var cpuDescription = handles.Select(ProfilerRecorderHandle.GetDescription).FirstOrDefault(d => d.Name == "CPU Main Thread Frame Time");
            using (var cpuRecorder = string.IsNullOrEmpty(cpuDescription.Name) ? default : ProfilerRecorder.StartNew(cpuDescription.Category, cpuDescription.Name, 1))
            using (var allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            {
                for (int i = 0; i < 240; i++)
                {
                    await NextFrame(ct);
                    frames.Add(Time.unscaledDeltaTime * 1000f);
                    if (display != null && display.TryGetAppGPUTimeLastFrame(out float seconds) && seconds > 0)
                        gpu.Add(seconds * 1000f);
                    if (allocations.Valid && allocations.Count > 0) gc.Add(allocations.LastValue);
                    if (cpuRecorder.Valid && cpuRecorder.Count > 0) cpu.Add(cpuRecorder.LastValue / 1000000f);
                }
            }
            frames.Sort(); gpu.Sort(); gc.Sort(); cpu.Sort();
            return new Sample {
                presentation = label, frames = frames.Count, gpuSamples = gpu.Count, gcSamples = gc.Count,
                frameP95Ms = frames[(int)Math.Ceiling(frames.Count * .95) - 1],
                gpuP95Ms = gpu.Count == 0 ? -1 : gpu[(int)Math.Ceiling(gpu.Count * .95) - 1],
                gcP95Bytes = gc.Count == 0 ? -1 : gc[(int)Math.Ceiling(gc.Count * .95) - 1],
                cpuSamples = cpu.Count, cpuP95Ms = cpu.Count == 0 ? -1 : cpu[(int)Math.Ceiling(cpu.Count * .95) - 1],
                totalAllocatedMemory = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong()
            };
        }
    }
}
#endif
