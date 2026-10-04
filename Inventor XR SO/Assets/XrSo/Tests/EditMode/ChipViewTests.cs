using System.Collections.Generic;
using InventorXrSo.Core.Ui;
using InventorXrSo.Core.Voice;
using InventorXrSo.Unity.Ui;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class ChipViewTests
    {
        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown() { foreach (var r in _roots) if (r != null) Object.DestroyImmediate(r); _roots.Clear(); }

        private ChipView Chip(NumericEntry entry, string label = "Larghezza")
        {
            var chip = ChipView.Create(null);
            _roots.Add(chip.Canvas.gameObject);
            chip.Bind(entry, label);
            return chip;
        }

        [Test]
        public void ShowsDisplayAndStepForMillimetersAndDegrees()
        {
            var mm = Chip(new NumericEntry("w", QuantityUnit.Millimeters, 25, 0, 500));
            StringAssert.Contains("25 mm", mm.ValueText);
            Assert.AreEqual("passo 1 mm", mm.StepText);
            var deg = new NumericEntry("a", QuantityUnit.Degrees, 45, 0, 180);
            deg.CycleStep(-1);
            var chip = Chip(deg, "Angolo");
            Assert.AreEqual("passo 0,1°", chip.StepText);
        }

        [Test]
        public void RefreshesWhenTheEntryChanges()
        {
            var entry = new NumericEntry("w", QuantityUnit.Millimeters, 25, 0, 500);
            var chip = Chip(entry);
            entry.Nudge(1);
            StringAssert.Contains("26 mm", chip.ValueText);
            entry.CycleStep(1);
            Assert.AreEqual("passo 10 mm", chip.StepText);
        }

        [Test]
        public void ModifiedAndArmedAreVisuallyDistinct()
        {
            var chip = Chip(new NumericEntry("w", QuantityUnit.Millimeters, 25, 0, 500));
            var normal = chip.BorderColor;
            chip.SetModified(true);
            Assert.AreEqual(UiTheme.Preview, chip.BorderColor);
            chip.SetModified(false);
            Assert.AreEqual(normal, chip.BorderColor);
            chip.Armed = true;
            Assert.IsTrue(chip.Armed);
            Assert.AreNotEqual(normal, chip.BorderColor);
            Assert.AreNotEqual(ChipView.Fill, chip.FillColor);
        }

        [Test]
        public void RayFocusDoesNotChangeTheDraftValueAndRestoresItsStateColour()
        {
            var entry = new NumericEntry("w", QuantityUnit.Millimeters, 25, 0, 500);
            var chip = Chip(entry);
            chip.SetModified(true);
            var position = chip.transform.position;
            chip.OnPointerEnter(null);
            Assert.AreEqual(UiTheme.Signal, chip.BorderColor);
            Assert.AreEqual(25, entry.Value);
            Assert.AreEqual(position, chip.transform.position);
            chip.OnPointerExit(null);
            Assert.AreEqual(UiTheme.Preview, chip.BorderColor);
        }

        [Test]
        public void TapRaisesTheCallbackAndSizesMeetTheMinimums()
        {
            var chip = Chip(new NumericEntry("w", QuantityUnit.Millimeters, 25, 0, 500));
            int taps = 0;
            chip.Tapped += () => taps++;
            chip.Button.onClick.Invoke();
            Assert.AreEqual(1, taps);
            var size = ((RectTransform)chip.Canvas.transform).sizeDelta;
            Assert.GreaterOrEqual(Mathf.Min(size.x, size.y), 25f);
            Assert.GreaterOrEqual(UiTypography.CapHeight(chip.GetComponentInChildren<TMPro.TextMeshProUGUI>()), 14f - 0.01f);
        }

        [Test]
        public void PlaceAndFaceOrientTowardsTheHead()
        {
            var head = new GameObject("head"); _roots.Add(head);
            head.transform.position = new Vector3(0, 1.5f, 0);
            var chip = Chip(new NumericEntry("w", QuantityUnit.Millimeters, 25, 0, 500));
            chip.Face(head.transform);
            chip.Place(new Vector3(0, 1.5f, 0.5f));
            Assert.AreEqual(0.5f, chip.Canvas.transform.position.z, 1e-5f);
            // il canvas guarda via dalla testa: il testo si legge da chi sta dietro
            Assert.Greater(Vector3.Dot(chip.Canvas.transform.forward, Vector3.forward), 0.99f);
        }
    }
}
