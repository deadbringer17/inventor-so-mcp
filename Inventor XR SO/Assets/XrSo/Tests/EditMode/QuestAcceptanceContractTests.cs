using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    // I runner di accettazione su Quest (Xr/Acceptance/*.cs) sono compilati solo con XR_SO_ACCEPTANCE,
    // assente in EditMode: il contratto con AppController & co. (accesso per reflection) si verifica
    // quindi sul TESTO sorgente dei runner.
    public class QuestAcceptanceContractTests
    {
        private static readonly string[] RunnerTypeNames =
        {
            "QuestAcceptanceRunner",
            "M1QuestAcceptance",
            "M2QuestAcceptance",
            "M3QuestAcceptance",
            "M4QuestAcceptance",
            "M5QuestAcceptance",
            "M6QuestAcceptance",
            "M7QuestAcceptance",
            "M8QuestAcceptance",
            "M9QuestAcceptance",
            "M9NestedQuestAcceptance",
        };

        private const BindingFlags PerLevel =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private const MemberTypes Kinds =
            MemberTypes.Field | MemberTypes.Method | MemberTypes.Property | MemberTypes.Event;

        private static readonly Regex ReflectedBlock =
            new Regex(@"ReflectedMembers\s*=\s*\{(?<body>.*?)\}\s*;", RegexOptions.Singleline);

        private static readonly Regex StringLiteral =
            new Regex("\"(?<s>(?:[^\"\\\\]|\\\\.)*)\"");

        // Read<T>(x, "name"), ReadValue<T>(x, "name"), Set<T>(x, "name", v)
        private static readonly Regex GenericUse =
            new Regex("\\b(?:Read|ReadValue|Set)\\s*<.*?>\\(\\s*[^,\"]+,\\s*\"(?<n>[^\"]+)\"");

        // ReadBoolean(x, "name"), Set(x, "name", v), Call(x, "name", ...), CallAsync(x, "name", ...)
        private static readonly Regex PlainUse =
            new Regex("\\b(?:ReadBoolean|Set|Call|CallAsync)\\s*\\(\\s*[^,\"]+,\\s*\"(?<n>[^\"]+)\"");

        [Test]
        public void RunnersAreExcludedFromOrdinaryBuild()
        {
            var assembly = typeof(InventorXrSo.Xr.AppController).Assembly;
            var present = assembly.GetTypes()
                .Where(t => RunnerTypeNames.Contains(t.Name))
                .Select(t => t.FullName)
                .ToList();
            Assert.IsEmpty(present,
                "I runner di accettazione non devono essere compilati senza XR_SO_ACCEPTANCE: " + string.Join(", ", present));
        }

        [TestCase("M1QuestAcceptance")]
        [TestCase("M2QuestAcceptance")]
        [TestCase("M3QuestAcceptance")]
        [TestCase("M5QuestAcceptance")]
        [TestCase("M6QuestAcceptance")]
        [TestCase("M7QuestAcceptance")]
        [TestCase("M8QuestAcceptance")]
        [TestCase("M9QuestAcceptance")]
        [TestCase("M9NestedQuestAcceptance")]
        public void ReflectedMembersExist(string runner)
        {
            var missing = new List<string>();
            foreach (var entry in ReadReflectedMembers(runner))
            {
                var dot = entry.IndexOf('.');
                if (dot <= 0 || dot == entry.Length - 1)
                {
                    missing.Add(entry + " (formato atteso Type.member)");
                    continue;
                }

                var typeName = entry.Substring(0, dot);
                var member = entry.Substring(dot + 1);
                var matches = FindTypes(typeName);
                if (matches.Count != 1)
                {
                    missing.Add(entry + " (tipo '" + typeName + "': " + matches.Count + " corrispondenze negli assembly InventorXrSo*)");
                    continue;
                }

                if (!HasMember(matches[0], member))
                    missing.Add(entry + " (membro assente in " + matches[0].FullName + " e nelle sue basi)");
            }

            Assert.IsEmpty(missing,
                runner + ": membri di ReflectedMembers non risolti (rinomina/rimozione in produzione?):\n  "
                + string.Join("\n  ", missing));
        }

        [TestCase("M1QuestAcceptance")]
        [TestCase("M2QuestAcceptance")]
        [TestCase("M3QuestAcceptance")]
        [TestCase("M5QuestAcceptance")]
        [TestCase("M6QuestAcceptance")]
        [TestCase("M7QuestAcceptance")]
        [TestCase("M8QuestAcceptance")]
        [TestCase("M9QuestAcceptance")]
        [TestCase("M9NestedQuestAcceptance")]
        public void ReflectedMembersListIsComplete(string runner)
        {
            var declared = new HashSet<string>();
            foreach (var entry in ReadReflectedMembers(runner))
            {
                var dot = entry.IndexOf('.');
                if (dot >= 0) declared.Add(entry.Substring(dot + 1));
            }

            var source = ReadSource(runner);
            var used = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in GenericUse.Matches(source)) used.Add(m.Groups["n"].Value);
            foreach (Match m in PlainUse.Matches(source)) used.Add(m.Groups["n"].Value);

            var undeclared = used.Where(n => !declared.Contains(n)).ToList();
            Assert.IsEmpty(undeclared,
                runner + ": nomi usati per reflection ma assenti da ReflectedMembers: " + string.Join(", ", undeclared));
        }


        // ---- M9: contratto del runner di navigazione per contesto (testo sorgente: il runner non compila in EditMode)

        private static readonly Regex GateIdsBlock =
            new Regex(@"GateIds\s*=\s*\{(?<body>.*?)\}\s*;", RegexOptions.Singleline);

        private static string RepoRootFile(string relative)
        {
            // Application.dataPath = <repo>/Inventor XR SO/Assets
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            var path = Path.Combine(root, relative);
            Assert.IsTrue(File.Exists(path), "File non trovato: " + path);
            return File.ReadAllText(path);
        }

        private static string StripComments(string source)
            => string.Join("\n", source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        [Test]
        public void M9RunnerNamesEveryGateInItsLog()
        {
            var source = ReadSource("M9QuestAcceptance");
            var block = GateIdsBlock.Match(source);
            Assert.IsTrue(block.Success, "M9QuestAcceptance: blocco 'GateIds = { ... };' non trovato");
            var declared = StringLiteral.Matches(block.Groups["body"].Value).Cast<Match>().Select(m => m.Groups["s"].Value).ToList();
            var expected = Enumerable.Range(1, 12).Select(i => "M9-" + i.ToString("00")).ToList();
            CollectionAssert.AreEqual(expected, declared, "GateIds deve elencare M9-01..M9-12 nell'ordine");

            var logged = new Regex("(?:Pass|NotCovered)\\(\\s*\"(?<g>M9-\\d\\d)", RegexOptions.Compiled);
            var inLog = logged.Matches(source).Cast<Match>().Select(m => m.Groups["g"].Value).Distinct().ToList();
            var missing = expected.Where(g => !inLog.Contains(g)).ToList();
            Assert.IsEmpty(missing, "gate M9 senza una riga Pass(...) o NotCovered(...) nel runner: " + string.Join(", ", missing));
        }

        [Test]
        public void M9RunnerUsesTheDedicatedM6FixtureWithAnExplicitGuard()
        {
            var source = ReadSource("M9QuestAcceptance");
            StringAssert.Contains("protected override string Milestone => \"m9\"", source);
            StringAssert.Contains("protected override string FixtureMilestone => \"m6\"", source);
            StringAssert.Contains("StartIfRequested<M9QuestAcceptance>(\"xr_m9_acceptance\")", source);
            var wait = source.IndexOf("await WaitForFixture(ct)", StringComparison.Ordinal);
            var guard = source.IndexOf("RequireFixture();", StringComparison.Ordinal);
            var enter = source.IndexOf("\"EnterSession\"", StringComparison.Ordinal);
            var synthetic = source.IndexOf("BeginSynthetic();", StringComparison.Ordinal);
            Assert.That(wait, Is.GreaterThan(0), "Run deve attendere la fixture");
            Assert.That(guard, Is.GreaterThan(wait), "RequireFixture() deve seguire WaitForFixture");
            Assert.That(enter, Is.GreaterThan(guard), "nessuna azione prima della guardia di fixture");
            Assert.That(synthetic, Is.GreaterThan(guard), "nessun input sintetico prima della guardia di fixture");

            var runner = ReadSource("QuestAcceptanceRunner");
            StringAssert.Contains("protected virtual string FixtureMilestone => Milestone;", runner);
            StringAssert.Contains("\"XR_\" + FixtureMilestone.ToUpperInvariant() + \"_Quest_Acceptance\"", runner);
            StringAssert.Contains("protected void RequireFixture()", runner);
            // Il runner M8 resta sulla stessa fixture: M8 e M9 non hanno una fixture propria.
            StringAssert.Contains("protected override string FixtureMilestone => \"m6\"", ReadSource("M8QuestAcceptance"));
        }

        [Test]
        public void M9VerdictIsPassCompleteOnlyWithoutNotCoveredRunnerCases()
        {
            var runner = ReadSource("QuestAcceptanceRunner");
            StringAssert.Contains("protected virtual string Completion()", runner);
            StringAssert.Contains("Record(Completion());", runner);
            StringAssert.Contains("NotCoveredGates.Add(gate);", runner);

            var source = ReadSource("M9QuestAcceptance");
            StringAssert.Contains("protected override string Completion()", source);
            var start = source.IndexOf("protected override string Completion()", StringComparison.Ordinal);
            var end = source.IndexOf("private static bool IsOutsideRunner", StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start));
            var verdict = source.Substring(start, end - start);
            StringAssert.Contains("NotCoveredGates", verdict);
            StringAssert.Contains("\"PASS COMPLETE; \"", verdict);
            StringAssert.Contains("\"PARTIAL; NOT COVERED: \"", verdict);
            StringAssert.Contains("open.Count == 0", verdict);

            // Nessun altro punto del runner scrive PASS COMPLETE (i commenti non contano).
            var code = StripComments(source);
            var occurrences = Regex.Matches(code, "PASS COMPLETE").Count;
            var inVerdict = Regex.Matches(StripComments(verdict), "PASS COMPLETE").Count;
            Assert.AreEqual(inVerdict, occurrences, "PASS COMPLETE puo essere scritto solo da Completion()");

            // Solo i NOT COVERED con un suffisso di prova esterna non rendono il verdetto PARZIALE.
            var suffixBlock = new Regex(@"OutsideRunnerSuffixes\s*=\s*\{(?<body>.*?)\}\s*;", RegexOptions.Singleline).Match(source);
            Assert.IsTrue(suffixBlock.Success);
            var allowed = StringLiteral.Matches(suffixBlock.Groups["body"].Value).Cast<Match>().Select(m => m.Groups["s"].Value).ToList();
            CollectionAssert.AreEquivalent(new[] { "-physical", "-probe", "-suite", "-nested" }, allowed);

            // I gate fisici, di sonda e di suite sono dichiarati, mai passati dal runner.
            foreach (var gate in new[] { "M9-08-probe", "M9-11-suite", "M9-12-physical" })
                StringAssert.Contains("NotCovered(\"" + gate + "\"", source);
            Assert.IsFalse(Regex.IsMatch(code, "Pass\\(\\s*\"M9-(08|11|12)\""), "M9-08, M9-11 e M9-12 non si passano dal runner");
        }

        [Test]
        public void M9RunnerLogsSyntheticInputAndRestoresWhatItReplaces()
        {
            var source = ReadSource("M9QuestAcceptance");
            StringAssert.Contains("SYNTHETIC", source);
            StringAssert.Contains("_input.Source = _source;", source);
            StringAssert.Contains("_input.Source = _originalSource;", source);
            StringAssert.Contains("dispatcher.StateProbe = originalProbe;", source);
            StringAssert.Contains("_input.RestingProbe = originalResting;", source);
            StringAssert.Contains("DoubleTriggerClock = null", source);
            StringAssert.Contains("CleanupAsync", source);
        }

        [Test]
        public void RunScriptKnowsM9AndThePartialOutcome()
        {
            var script = RepoRootFile("scripts/run-quest-acceptance.ps1");
            StringAssert.Contains("'m9'", script);
            StringAssert.Contains("PARTIAL", script);
            var m9 = RepoRootFile("scripts/run-m9-acceptance.ps1");
            StringAssert.Contains("--prepare-quest m6", m9);
            StringAssert.Contains("-Milestone m9", m9);
            StringAssert.Contains("--restore-quest m6", m9);
        }

        [TestCase("M1QuestAcceptance")]
        [TestCase("M2QuestAcceptance")]
        [TestCase("M3QuestAcceptance")]
        [TestCase("M5QuestAcceptance")]
        [TestCase("M6QuestAcceptance")]
        [TestCase("M7QuestAcceptance")]
        [TestCase("M8QuestAcceptance")]
        [TestCase("M9QuestAcceptance")]
        [TestCase("M9NestedQuestAcceptance")]
        public void MigratedRunnersNoLongerOpenWorkspacesByTheRemovedSpacesActions(string runner)
        {
            var code = StripComments(ReadSource(runner));
            Assert.IsFalse(Regex.IsMatch(code, "RunAction\\(\\s*\"spaces\\."), runner + ": RunAction(\"spaces.*\") non esiste piu (M9)");
            Assert.IsFalse(Regex.IsMatch(code, "=\\s*\"spaces\\.[a-z]+\""), runner + ": costante \"spaces.*\" residua");
        }

        [Test]
        public void M4RunnerNoLongerUsesTheRemovedSpacesActions()
        {
            var path = Path.Combine(Application.dataPath, "XrSo/Xr/M4QuestAcceptance.cs");
            Assert.IsTrue(File.Exists(path), "File runner non trovato: " + path);
            var code = StripComments(File.ReadAllText(path));
            Assert.IsFalse(code.Contains("\"spaces."), "M4QuestAcceptance: azioni spaces.* rimosse da M9");
            StringAssert.Contains("DoubleTriggerClock", code);
        }

        [TestCase("M4QuestAcceptance")]
        [TestCase("M5QuestAcceptance")]
        [TestCase("M6QuestAcceptance")]
        public void SyntheticPressesNeverFormADoubleTriggerByAccident(string runner)
        {
            var path = runner == "M4QuestAcceptance"
                ? Path.Combine(Application.dataPath, "XrSo/Xr/M4QuestAcceptance.cs")
                : Path.Combine(Application.dataPath, "XrSo/Xr/Acceptance/" + runner + ".cs");
            var code = StripComments(File.ReadAllText(path));
            StringAssert.Contains("DoubleTriggerClock = () =>", code, runner + ": i tocchi sintetici sullo stesso corpo vanno su finestre separate");
            Assert.IsTrue(code.Contains("_doubleClock += 5") || code.Contains("_clock += 5"), runner + ": ogni pressione avanza l'orologio del rilevatore");
        }

        // ---- M9 nested: runner dei sottoassiemi (fixture m9n)

        [Test]
        public void M9NestedRunnerHasItsOwnFixtureAndGuardAndCoversTheUserScenario()
        {
            var source = ReadSource("M9NestedQuestAcceptance");
            StringAssert.Contains("protected override string Milestone => \"m9n\"", source);
            StringAssert.Contains("protected override string FixtureMilestone => \"m9n\"", source);
            StringAssert.Contains("StartIfRequested<M9NestedQuestAcceptance>(\"xr_m9n_acceptance\")", source);
            StringAssert.Contains("class M9NestedQuestAcceptance : M9QuestAcceptance", source);
            var wait = source.IndexOf("await WaitForFixture(ct)", StringComparison.Ordinal);
            var guard = source.IndexOf("RequireFixture();", StringComparison.Ordinal);
            var synthetic = source.IndexOf("BeginSynthetic();", StringComparison.Ordinal);
            Assert.That(guard, Is.GreaterThan(wait), "RequireFixture() deve seguire WaitForFixture");
            Assert.That(synthetic, Is.GreaterThan(guard), "nessun input sintetico prima della guardia di fixture");
            // La guardia degli altri runner non si allenta: M6 resta M6, M9N e una guardia a parte.
            StringAssert.Contains("protected override string FixtureMilestone => \"m6\"", ReadSource("M9QuestAcceptance"));
            StringAssert.Contains("protected override string FixtureMilestone => \"m6\"", ReadSource("M8QuestAcceptance"));
            // Scenario: salita per doppio Trigger, parte del sottoassieme, modifica, due Torna (X tenuto e scheda Documento), Assieme2.
            foreach (var needle in new[] { "EnterAsync(a1", "EnterByDoubleTriggerAsync(partA", "CheckFeatureEditAsync", "HoldXForBack()", "DocumentActions.IdBack",
                "EnterAsync(a2", "GhostNames()", "Nav.Levels", "Dirty", "AssertUnchanged", "TopLevel", "direct occurrence" })
                StringAssert.Contains(needle.Replace("TopLevel", "direct sub-assembly occurrence"), source, "scenario: " + needle);
            foreach (var gate in new[] { "M9-02", "M9-03", "M9-04" })
                Assert.IsTrue(Regex.IsMatch(source, "Pass\\(\\s*\"" + gate + "\""), "il log del runner annidato passa il gate " + gate);
            StringAssert.Contains("NotCovered(\"M9-12-physical\"", source);
            Assert.IsFalse(Regex.IsMatch(StripComments(source), "Pass\\(\\s*\"M9-(08|11|12)\""), "M9-08, M9-11 e M9-12 non si passano dal runner");
            Assert.IsFalse(StripComments(source).Contains("PASS COMPLETE"), "PASS COMPLETE puo essere scritto solo dal Completion() di M9");
            StringAssert.Contains("ReportKnownFeatureGaps => false", source);
            StringAssert.Contains("RestoreHint => \"--restore-quest m9n\"", source);
        }

        [Test]
        public void M9NestedFixtureAndScriptsExist()
        {
            var fixtures = RepoRootFile("bridge/tests/QuestAcceptanceFixtures/Fixtures.cs");
            StringAssert.Contains("\"m9n\" => PrepareM9Nested", fixtures);
            StringAssert.Contains("XR_M9N_Quest_Acceptance_", fixtures);
            foreach (var name in new[] { "Assieme1.iam", "Assieme2.iam", "Assieme3.iam", "PartA.ipt", "PartB.ipt", "PartC.ipt" })
                StringAssert.Contains(name, fixtures);
            StringAssert.Contains("\"m9n\"", RepoRootFile("bridge/tests/QuestAcceptanceFixtures/Program.cs"));
            var generic = RepoRootFile("scripts/run-quest-acceptance.ps1");
            StringAssert.Contains("'m9n'", generic);
            var script = RepoRootFile("scripts/run-m9-nested-acceptance.ps1");
            StringAssert.Contains("--prepare-quest m9n", script);
            StringAssert.Contains("-Milestone m9n", script);
            StringAssert.Contains("--restore-quest m9n", script);
            StringAssert.Contains("--inspect-quest m9n", script);
        }

        private static string ReadSource(string runner)
        {
            var path = Path.Combine(Application.dataPath, "XrSo/Xr/Acceptance/" + runner + ".cs");
            Assert.IsTrue(File.Exists(path), "File runner non trovato: " + path);
            return File.ReadAllText(path);
        }

        private static List<string> ReadReflectedMembers(string runner)
        {
            var block = ReflectedBlock.Match(ReadSource(runner));
            Assert.IsTrue(block.Success, runner + ": blocco 'ReflectedMembers = { ... };' non trovato");
            var entries = new List<string>();
            foreach (Match m in StringLiteral.Matches(block.Groups["body"].Value))
                entries.Add(m.Groups["s"].Value);
            Assert.IsNotEmpty(entries, runner + ": ReflectedMembers è vuoto");
            return entries;
        }

        private static List<Type> FindTypes(string simpleName)
        {
            var result = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.StartsWith("InventorXrSo", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                result.AddRange(types.Where(t => t.Name == simpleName));
            }
            return result;
        }

        private static bool HasMember(Type type, string member)
        {
            for (var t = type; t != null; t = t.BaseType)
                if (t.GetMember(member, Kinds, PerLevel).Length > 0) return true;
            return false;
        }
    }
}
