using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Session;
using InventorXrSo.Unity.Scene;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public sealed class AssemblyVisualColorsTests
    {
        private GameObject _root;
        private Material _material;
        private AssemblyVisuals _visuals;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Assembly visual colors test");
            _material = new Material(Shader.Find("XrSo/CadSurface"));
            _visuals = _root.AddComponent<AssemblyVisuals>();
            _visuals.Initialize(_material);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (_material != null) Object.DestroyImmediate(_material);
        }

        [Test]
        public void ReferenceALinesUseCyanAndReferenceBLinesUseYellow()
        {
            var context = AssemblyContext.Parse(JObject.Parse(@"{""document_id"":""doc_colors"",""revision"":""r"",""kind"":""assembly"",""truncated"":false,
                ""occurrences"":[
                    {""occurrence_id"":""ent_a"",""name"":""Occurrence A"",""editable"":true,""grounded"":false,""suppressed"":false,""adaptive"":false,""dof_translation"":0,""dof_rotation"":0,""dof_complete"":true,""translation_axes"":[],""rotation_axes"":[]},
                    {""occurrence_id"":""ent_b"",""name"":""Occurrence B"",""editable"":true,""grounded"":false,""suppressed"":false,""adaptive"":false,""dof_translation"":0,""dof_rotation"":0,""dof_complete"":true,""translation_axes"":[],""rotation_axes"":[]}],""references"":[
                    {""id"":""ent_ref_a"",""occurrence_id"":""ent_a"",""name"":""A"",""kind"":""face"",""geometry"":""kPlaneSurface"",""point_mm"":[0,0,0]},
                    {""id"":""ent_ref_b"",""occurrence_id"":""ent_b"",""name"":""B"",""kind"":""face"",""geometry"":""kPlaneSurface"",""point_mm"":[10,0,0]}]}"),
                new DocumentState("doc_colors", "r", "v"));

            _visuals.Show(null, context.References[0], context.References[1], false, false, -1);

            AssertColor("A", Color.cyan);
            AssertColor("B", Color.yellow);
        }

        private void AssertColor(string name, Color expected)
        {
            var line = _root.GetComponentsInChildren<LineRenderer>().First(l => l.name == name);
            var properties = new MaterialPropertyBlock();
            line.GetPropertyBlock(properties);
            Assert.That(properties.GetColor(Shader.PropertyToID("_BaseColor")), Is.EqualTo(expected));
            Assert.That(properties.GetColor(Shader.PropertyToID("_Color")), Is.EqualTo(expected));
            Assert.That(line.sharedMaterial, Is.SameAs(_material));
        }
    }
}
