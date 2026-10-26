using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using InventorXrSo.Xr;
using InventorXrSo.Core.Pairing;
using InventorXrSo.Unity.Scene;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    /// <summary>Checks the QA-only source contract without compiling/running a device acceptance runner in EditMode.</summary>
    public sealed class S1QuestAcceptanceContractTests
    {
        private static string Source => File.ReadAllText(Path.Combine(Application.dataPath,
            "XrSo/Xr/Acceptance/S1QuestAcceptance.cs"));
        private static string Extension => File.ReadAllText(Path.Combine(Application.dataPath,
            "XrSo/Xr/Acceptance/S1QuestAcceptance.TextAndParameters74.cs"));

        [Test]
        public void RunnerIsOptInAndAbsentFromOrdinaryAssembly()
        {
            Assert.That(Source.TrimStart(), Does.StartWith("#if XR_SO_ACCEPTANCE"));
            Assert.That(Source.TrimEnd(), Does.EndWith("#endif"));
            Assert.That(Source, Does.Contain("StartIfRequested<S1QuestAcceptance>(\"xr_s1_acceptance\")"));
            Assert.That(Source.TrimStart(), Does.StartWith("#if XR_SO_ACCEPTANCE || UNITY_EDITOR"), "ordinary players exclude the runner");
            var runner = typeof(AppController).Assembly.GetType("InventorXrSo.Xr.S1QuestAcceptance");
            Assert.That(runner, Is.Not.Null, "Editor exposes the opt-in simulator runner");
            QuestAcceptanceContractTests.AssertEditorRunnerRequiresFixture(runner, "xr_s1_acceptance");
        }

        [Test]
        public void ExtendedRunnerRemainsQaOnlyAndKeepsTextAuthoringClosed()
        {
            Assert.That(Extension.TrimStart(), Does.StartWith("#if XR_SO_ACCEPTANCE"));
            Assert.That(Extension.TrimEnd(), Does.EndWith("#endif"));
            Assert.That(Extension, Does.Not.Contain("text_create"));
            Assert.That(Extension, Does.Not.Contain("text_update"));
            Assert.That(Extension, Does.Not.Contain("text_delete"));
            Assert.That(Extension, Does.Not.Contain("ConfirmRendered("),
                "Only the real workspace renderer may confirm a plan for Apply.");
            Assert.That(Extension, Does.Not.Contain("SetDraft("),
                "The runner must use actual parameter UI actions, not construct Core drafts.");
            Assert.That(Source, Does.Contain("await RunContext(part.Id, false, ct)"));
            Assert.That(Source, Does.Contain("await RunContext(sheet.Id, true, ct)"));
        }

        [Test]
        public void BootstrapRunnerBindingHasOnlyImmutablePublicMetadata()
        {
            var properties = typeof(S1AcceptanceBootstrapMetadata).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            CollectionAssert.AreEquivalent(new[] { "RunId", "ExpiresUtc", "TargetId", "PartDocumentId", "SheetDocumentId" },
                properties.Select(p => p.Name).ToArray());
            Assert.That(properties.All(p => p.GetMethod != null && p.SetMethod == null), Is.True);
            Assert.That(Extension, Does.Contain("_binding.ExpiresUtc > DateTimeOffset.UtcNow"));
            Assert.That(Extension, Does.Contain("ReadBoolean(App, \"_inSession\")"));
            Assert.That(Extension, Does.Contain("await _inspection.ListOpenAsync(ct)"));
            Assert.That(Source + Extension, Does.Not.Contain("CreatePairedServer("));
        }

        [Test]
        public void NativeGlyphIdentityAndRendererInspectionSeamsExist()
        {
            var entry = typeof(NativeSketchTextLayer).GetNestedType("Entry", BindingFlags.NonPublic);
            Assert.That(entry, Is.Not.Null);
            foreach (string field in new[] { "Id", "Object", "Mesh", "Renderer" })
                Assert.That(entry.GetField(field, BindingFlags.Public | BindingFlags.Instance), Is.Not.Null, field);
            Assert.That(typeof(DesignWorkspace).GetProperty("Geometry"), Is.Not.Null);
            Assert.That(typeof(LamieraWorkspace).GetProperty("Geometry"), Is.Not.Null);
            Assert.That(typeof(DesignWorkspace).GetProperty("ActiveParameterTextEntry"), Is.Not.Null);
        }

        [Test]
        public void EveryDeclaredReflectionMemberExistsInCurrentProduct()
        {
            var block = Regex.Match(Source, @"ReflectedMembers\s*=\s*\{(?<body>.*?)\};", RegexOptions.Singleline);
            Assert.That(block.Success, Is.True);
            var declarations = Regex.Matches(block.Groups["body"].Value, "\"(?<member>[^\"]+)\"");
            Assert.That(declarations.Count, Is.GreaterThan(0));
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("InventorXrSo", StringComparison.Ordinal))
                .SelectMany(a => a.GetTypes()).ToArray();
            foreach (Match declaration in declarations)
            {
                string[] parts = declaration.Groups["member"].Value.Split('.');
                Assert.That(parts.Length, Is.EqualTo(2));
                var type = types.Single(t => t.Name == parts[0]);
                bool found = false;
                for (var current = type; current != null; current = current.BaseType)
                    found |= current.GetMember(parts[1], BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Length > 0;
                Assert.That(found, Is.True, declaration.Value + " is no longer a valid runner seam.");
            }
        }

    }
}
