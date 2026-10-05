#if XR_SO_ACCEPTANCE
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Session;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Shared base of the opt-in, fixture-scoped Quest acceptance runners (one subclass per milestone).
    /// Handles the Android intent gate, the evidence log, reflection helpers, waits and screenshots.
    /// </summary>
    internal abstract class QuestAcceptanceRunner : MonoBehaviour
    {
        protected abstract string Milestone { get; }
        protected virtual int TimeoutSeconds => 180;
        protected virtual string CompletionNote => "physical controller input was not exercised by this runner";
        protected string Tag => "[" + Milestone.ToUpperInvariant() + "Quest]";
        protected virtual string FixtureMilestone => Milestone;
        protected string FixturePrefix => "XR_" + FixtureMilestone.ToUpperInvariant() + "_Quest_Acceptance";
        protected string LogPath { get; private set; }

        /// <summary>Every gate id passed to <see cref="NotCovered"/> in this run, in order (a runner may decide its verdict on it).</summary>
        protected readonly System.Collections.Generic.List<string> NotCoveredGates = new System.Collections.Generic.List<string>();

        protected AppController App { get; private set; }
        protected SessionController Session { get; private set; }

        /// <summary>Called by each subclass from its own AfterSceneLoad static method.</summary>
        protected static void StartIfRequested<T>(string intentExtra) where T : QuestAcceptanceRunner
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = activity.Call<AndroidJavaObject>("getIntent"))
                {
                    if (!intent.Call<bool>("getBooleanExtra", intentExtra, false)) return;
                }
                var host = new GameObject("Quest Acceptance Runner");
                DontDestroyOnLoad(host);
                var runner = host.AddComponent<T>();
                host.name = runner.Milestone.ToUpperInvariant() + " Quest Acceptance Runner";
            }
            catch (Exception ex)
            {
                Debug.LogError("[Quest] Could not read acceptance intent: " + ex.GetType().Name + ": " + ex.Message);
            }
#endif
        }

        protected abstract Task Run(CancellationToken ct);

        /// <summary>
        /// Final verdict line written after <see cref="Run"/> returns without throwing (a throw is always "FAIL; ..."). The default is
        /// "PASS COMPLETE; note" (runners M1-M8); a runner whose verdict depends on its NOT COVERED sub-cases (M9) overrides it.
        /// </summary>
        protected virtual string Completion() => "PASS COMPLETE; " + CompletionNote;

        private async void Start()
        {
            LogPath = Path.Combine(Application.persistentDataPath, Milestone + "-acceptance.txt");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.WriteAllText(LogPath, string.Empty);
                Record("START; waiting for online Inventor fixture");
                await Run(timeout.Token);
                Record(Completion());
            }
            catch (Exception ex)
            {
                var inner = ex;
                while ((inner is TargetInvocationException || inner is AggregateException) && inner.InnerException != null)
                    inner = inner.InnerException;
                Record("FAIL; " + inner.GetType().Name + ": " + inner.Message);
            }
        }

        protected void Record(string message)
        {
            var line = DateTime.UtcNow.ToString("O") + " " + Tag + " " + message;
            Debug.Log(line);
            if (string.IsNullOrEmpty(LogPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }

        protected void Pass(string gate, string message) => Record("PASS [" + gate + "] " + message);
        protected void NotCovered(string gate, string reason)
        {
            NotCoveredGates.Add(gate);
            Record("NOT COVERED [" + gate + "] " + reason);
        }

        protected static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static FieldInfo FindField(object target, string name)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        protected static T Read<T>(object target, string name) where T : class
            => FindField(target, name).GetValue(target) as T;

        protected static T ReadValue<T>(object target, string name) where T : struct
            => (T)FindField(target, name).GetValue(target);

        protected static bool ReadBoolean(object target, string name)
            => (bool)FindField(target, name).GetValue(target);

        protected static void Set<T>(object target, string name, T value)
            => FindField(target, name).SetValue(target, value);

        protected static object Call(object target, string method, params object[] args)
        {
            args ??= new object[0];
            MethodInfo found = null;
            for (var type = target.GetType(); type != null && found == null; type = type.BaseType)
            {
                foreach (var candidate in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (candidate.Name == method && candidate.GetParameters().Length == args.Length)
                    {
                        found = candidate;
                        break;
                    }
                }
            }
            if (found == null) throw new MissingMethodException(target.GetType().FullName, method);
            try
            {
                return found.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        protected static async Task CallAsync(object target, string method, params object[] args)
        {
            if (Call(target, method, args) is Task task) await task;
        }

        /// <summary>Default bound of a single wait: a wait that never resolves fails naming its caller instead of hanging until the global timeout.</summary>
        protected const int DefaultWaitSeconds = 60;

        private static InvalidOperationException WaitTimedOut(int timeoutSeconds, string caller, int line)
            => new InvalidOperationException("wait timed out after " + timeoutSeconds + "s at " + caller + ":" + line);

        protected static async Task<T> WaitFor<T>(Func<T> read, CancellationToken ct, int timeoutSeconds = DefaultWaitSeconds,
            [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0) where T : class
        {
            var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var value = read();
                if (value != null) return value;
                if (DateTime.UtcNow >= until) throw WaitTimedOut(timeoutSeconds, caller, line);
                await Task.Delay(100, ct);
            }
        }

        protected static async Task<T> WaitFor<T>(Func<Task<T>> read, CancellationToken ct, int timeoutSeconds = DefaultWaitSeconds,
            [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0) where T : class
        {
            var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var value = await read();
                if (value != null) return value;
                if (DateTime.UtcNow >= until) throw WaitTimedOut(timeoutSeconds, caller, line);
                await Task.Delay(200, ct);
            }
        }

        protected static async Task WaitUntil(Func<bool> condition, CancellationToken ct, int timeoutSeconds = DefaultWaitSeconds,
            [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0)
        {
            var until = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (condition()) return;
                if (DateTime.UtcNow >= until) throw WaitTimedOut(timeoutSeconds, caller, line);
                await Task.Delay(100, ct);
            }
        }

        protected async Task<string> CaptureScreenshot(string suffix, CancellationToken ct)
        {
            var directory = Application.persistentDataPath;
            Directory.CreateDirectory(directory);
            var fileName = Milestone + "-acceptance-" + suffix + ".png";
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path)) File.Delete(path);
            // Android resolves screenshot names under persistentDataPath itself.
            ScreenCapture.CaptureScreenshot(fileName);
            await WaitFor(() => File.Exists(path) && new FileInfo(path).Length > 0 ? path : null, ct);
            Record("Screenshot: " + path);
            return path;
        }

        protected async Task<LoadedScene> WaitForFixture(CancellationToken ct)
        {
            App = await WaitFor(() => FindObjectOfType<AppController>(), ct, TimeoutSeconds);
            Session = await WaitFor(() => Read<SessionController>(App, "_session"), ct, TimeoutSeconds);
            var session = Session;
            return await WaitFor(() => session.Status == SessionStatus.Online && HasFixtureRoot(session)
                ? session.Scene : null, ct, TimeoutSeconds);
        }

        protected void RequireFixture()
        {
            Check(Session != null && HasFixtureRoot(Session),
                "connected document is not the dedicated " + Milestone.ToUpperInvariant() + " acceptance fixture (" + FixturePrefix + ")");
        }

        private bool HasFixtureRoot(SessionController session)
            => session.Scene?.Graph?.Root?.Name?.StartsWith(FixturePrefix, StringComparison.Ordinal) == true;
    }
}
#endif
