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
