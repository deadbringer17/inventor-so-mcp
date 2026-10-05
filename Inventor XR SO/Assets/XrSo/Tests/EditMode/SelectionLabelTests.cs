using InventorXrSo.Core.Navigation;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;
using InventorXrSo.Xr;
using NUnit.Framework;
using UnityEngine;

namespace InventorXrSo.Tests
{
    public class SelectionLabelTests
    {
        private GameObject _head;
        private SelectionLabel _label;

        [SetUp]
        public void SetUp()
        {
            _head = new GameObject("head");   // at the origin, looking along +Z
            _label = SelectionLabel.Create(_head.transform);
        }

        [TearDown]
        public void TearDown()
        {
            if (_label != null) Object.DestroyImmediate(_label.gameObject);
            if (_head != null) Object.DestroyImmediate(_head);
        }

        private static Bounds Box(Vector3 center, float size = 0.1f) => new Bounds(center, Vector3.one * size);

        // ---- texts

        [Test]
        public void KindTextsAreShortItalianWords()
        {
            Assert.AreEqual("Parte", SelectionLabelText.Kind(SelectionLabelKind.Part));
            Assert.AreEqual("Sottoassieme", SelectionLabelText.Kind(SelectionLabelKind.Subassembly));
            Assert.AreEqual("Faccia 6", SelectionLabelText.Kind(SelectionLabelKind.Face, 6));
            Assert.AreEqual("Faccia", SelectionLabelText.Kind(SelectionLabelKind.Face, 0));
            Assert.AreEqual("Spigolo", SelectionLabelText.Kind(SelectionLabelKind.Edge));
            Assert.AreEqual("", SelectionLabelText.Kind(SelectionLabelKind.None));
        }

        [Test]
        public void LevelTextIsLivelloNOverTotalOrEmpty()
        {
            Assert.AreEqual("Livello 2/3", SelectionLabelText.Level(2, 3));
            Assert.AreEqual("Livello 1/1", SelectionLabelText.Level(1, 1));
            Assert.AreEqual("", SelectionLabelText.Level(0, 0), "no navigation stack");
            Assert.AreEqual("", SelectionLabelText.Level(4, 3), "out of range is not shown");
        }

        [Test]
        public void SecondLineJoinsKindAndLevelAndNeverExceedsTwoLines()
        {
            Assert.AreEqual("Parte · Livello 2/3", SelectionLabelText.Detail("Parte", "Livello 2/3"));
            Assert.AreEqual("Spigolo", SelectionLabelText.Detail("Spigolo", ""));
            Assert.AreEqual("Livello 2/3", SelectionLabelText.Detail("", "Livello 2/3"));
            Assert.IsFalse(SelectionLabelText.Detail("Sottoassieme", "Livello 3/3").Contains("\n"));
        }

        [Test]
        public void LongOrBlankNamesAreCutOrReplaced()
        {
            Assert.AreEqual("Assieme1:1", SelectionLabelText.Name("Assieme1:1"));
            var cut = SelectionLabelText.Name(new string('x', 60));
            Assert.AreEqual(SelectionLabelText.MaxNameLength, cut.Length);
            Assert.IsTrue(cut.EndsWith("…"));
            Assert.AreEqual("(senza nome)", SelectionLabelText.Name("  "));
            Assert.AreEqual("(senza nome)", SelectionLabelText.Name(null));
            Assert.IsTrue(UiTypography.Font(TMPro.FontStyles.Bold).HasCharacter('…'), "the ellipsis glyph exists in the UI font");
        }

        // ---- visibility

        [Test]
        public void TheLabelStartsHiddenAndHidesAgain()
        {
            Assert.IsFalse(_label.Visible);
            _label.Show("Bolt:1", "Parte", "Livello 1/2", Box(Vector3.forward));
            Assert.IsTrue(_label.Visible);
            _label.Hide();
            Assert.IsFalse(_label.Visible);
        }

        [Test]
        public void ShowSetsTheTwoLines()
        {
            _label.Show("Assieme1:1", "Parte", "Livello 2/3", Box(Vector3.forward));
            Assert.AreEqual("Assieme1:1", _label.NameText);
            Assert.AreEqual("Parte · Livello 2/3", _label.DetailText);
            _label.Show("Piastra:1", "Faccia 6", "", Box(Vector3.forward));
            Assert.AreEqual("Faccia 6", _label.DetailText);
        }

        // ---- readability and orientation

        [Test]
        public void BothLinesMeetTheHudCapHeightAtTheReferenceDistance()
        {
            Assert.GreaterOrEqual(SelectionLabel.CapMm, 14f);
            Assert.GreaterOrEqual(_label.NameCapMm, 14f - 0.01f);
            Assert.GreaterOrEqual(_label.DetailCapMm, 14f - 0.01f);
            Assert.AreEqual(14f, HudView.TextMm, "same cap height as the HUD, which is read at SelectionLabelPlacement.ReferenceDistance");
            _label.Show("Bolt:1", "Parte", "", Box(_head.transform.position + Vector3.forward * SelectionLabelPlacement.ReferenceDistance, 0.01f));
            Assert.AreEqual(0.001f, _label.CanvasTransform.localScale.x, 1e-5f, "1 canvas mm = 1 mm at the reference distance");
        }

        [Test]
        public void TheLabelGrowsWithDistanceWithinLimits()
        {
            Assert.AreEqual(1f, SelectionLabelPlacement.ScaleAt(SelectionLabelPlacement.ReferenceDistance), 1e-4f);
            Assert.Greater(SelectionLabelPlacement.ScaleAt(2.4f), SelectionLabelPlacement.ScaleAt(1.2f));
            Assert.AreEqual(SelectionLabelPlacement.MinScale, SelectionLabelPlacement.ScaleAt(0.01f));
            Assert.AreEqual(SelectionLabelPlacement.MaxScale, SelectionLabelPlacement.ScaleAt(50f));
        }

        [Test]
        public void TheLabelAlwaysFacesTheHead()
        {
            foreach (var headPosition in new[] { new Vector3(0, 0, 0), new Vector3(0.8f, 0.3f, -0.5f), new Vector3(-1f, 1.5f, 0.2f) })
            {
                _head.transform.position = headPosition;
                _head.transform.rotation = Quaternion.identity;
                var target = headPosition + new Vector3(0.1f, -0.2f, 1.0f);
                _label.Show("Bolt:1", "Parte", "", Box(target));
                var canvas = _label.CanvasTransform;
                // The canvas is read from its back (-Z side): +Z points away from the head, like the controller legend.
                var away = (canvas.position - headPosition).normalized;
                Assert.Greater(Vector3.Dot(canvas.forward, away), 0.999f, headPosition.ToString());
                Assert.Greater(Vector3.Dot(canvas.up, _head.transform.up), 0.9f, "text stays upright for the head");
            }
        }

        [Test]
        public void TheLabelTurnsWhenTheHeadMovesWithoutShowingAgain()
        {
            _label.Show("Bolt:1", "Parte", "", Box(new Vector3(0, 0, 1)));
            _head.transform.position = new Vector3(0.5f, 0, 0);
            _label.Tick();
            var away = (_label.CanvasTransform.position - _head.transform.position).normalized;
            Assert.Greater(Vector3.Dot(_label.CanvasTransform.forward, away), 0.999f);
        }

        // ---- placement

        [Test]
        public void ThePositionIsAboveTheTopOfTheHighlightAndTowardTheUser()
        {
            var bounds = new Bounds(new Vector3(0, -0.2f, 1.0f), new Vector3(0.2f, 0.1f, 0.2f));
            _label.Show("Bolt:1", "Parte", "", bounds);
            var p = _label.WorldPosition;
            Assert.Greater(p.y, bounds.max.y, "above the top of the bounds");
            Assert.Less(p.z, bounds.center.z, "pulled toward the head, which is at z = 0");
            Assert.Less(Vector3.Distance(p, bounds.center), 0.5f, "next to the highlight, not across the room");
            var flat = SelectionLabelPlacement.Position(bounds, _head.transform.position, Vector3.up, 0.06f);
            Assert.AreEqual(bounds.max.y + SelectionLabelPlacement.LiftMeters + 0.03f, flat.y, 0.02f);
        }

        [Test]
        public void TheLabelFollowsMovingBounds()
        {
            _label.Show("Bolt:1", "Parte", "", Box(new Vector3(0, 0, 1)));
            var before = _label.WorldPosition;
            _label.SetBounds(Box(new Vector3(0, 0.3f, 1)));
            Assert.AreEqual(before.y + 0.3f, _label.WorldPosition.y, 0.05f, "moved up with the bounds (the pull toward the head shifts it a little)");
            Assert.AreEqual("Bolt:1", _label.NameText);
        }

        [Test]
        public void HalfHeightAlongTheHeadUpUsesTheBoundsExtents()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(2f, 4f, 6f));
            Assert.AreEqual(2f, SelectionLabelPlacement.HalfHeightAlong(bounds, Vector3.up), 1e-5f);
            Assert.AreEqual(3f, SelectionLabelPlacement.HalfHeightAlong(bounds, Vector3.forward), 1e-5f);
        }

        // ---- driver

        private sealed class Source
        {
            public SelectionTarget Target;
            public SelectionTarget Get() => Target;
        }

        private static SelectionTarget Target(SelectionLabelKind kind, string name, int ordinal = 0, bool bounds = true, Vector3? at = null)
            => new SelectionTarget { Kind = kind, Name = name, FaceOrdinal = ordinal, HasBounds = bounds, Bounds = Box(at ?? new Vector3(0, 0, 1)) };

        [Test]
        public void DriverHidesTheLabelWhenNothingIsSelectedOrBoundsAreMissing()
        {
            var source = new Source();
            var driver = new SelectionLabelDriver(_label, new NavigationStack(), source.Get);
            driver.Tick();
            Assert.IsFalse(_label.Visible, "nothing selected");
            source.Target = Target(SelectionLabelKind.Part, "Bolt:1", bounds: false);
            driver.Tick();
            Assert.IsFalse(_label.Visible, "no highlight on screen: the workspace is not showing the model");
            source.Target = Target(SelectionLabelKind.Part, "Bolt:1");
            driver.Tick();
            Assert.IsTrue(_label.Visible);
            source.Target = default;
            driver.Tick();
            Assert.IsFalse(_label.Visible);
        }

        [Test]
        public void DriverFormatsPartSubassemblyFaceAndEdgeTexts()
        {
            var nav = new NavigationStack();
            nav.Reset(new NavLevel("doc_asm", DocContext.Assembly, "Assieme1"));
            var source = new Source();
            var driver = new SelectionLabelDriver(_label, nav, source.Get);
            source.Target = Target(SelectionLabelKind.Part, "Bolt:1");
            driver.Tick();
            Assert.AreEqual("Bolt:1", _label.NameText);
            Assert.AreEqual("Parte · Livello 1/2", _label.DetailText);
            source.Target = Target(SelectionLabelKind.Subassembly, "Sub:1");
            driver.Tick();
            Assert.AreEqual("Sottoassieme · Livello 1/2", _label.DetailText);
            nav.Push(new NavLevel("doc_sub", DocContext.Assembly, "Sub"));
            nav.Push(new NavLevel("doc_part", DocContext.Part, "Piastra", "occ"));
            source.Target = Target(SelectionLabelKind.Face, "Piastra", 6);
            driver.Tick();
            Assert.AreEqual("Piastra", _label.NameText);
            Assert.AreEqual("Faccia 6 · Livello 3/3", _label.DetailText);
            source.Target = Target(SelectionLabelKind.Edge, "Piastra");
            driver.Tick();
            Assert.AreEqual("Spigolo · Livello 3/3", _label.DetailText);
        }

        [Test]
        public void LevelIsOmittedWithoutANavigationPath()
        {
            var source = new Source { Target = Target(SelectionLabelKind.Part, "Bolt:1") };
            new SelectionLabelDriver(_label, new NavigationStack(), source.Get).Tick();
            Assert.AreEqual("Parte", _label.DetailText);
            Assert.AreEqual("", SelectionLabelDriver.LevelFor(SelectionLabelKind.Face, 0));
            Assert.AreEqual("Livello 2/3", SelectionLabelDriver.LevelFor(SelectionLabelKind.Part, 2));
            Assert.AreEqual("Livello 2/2", SelectionLabelDriver.LevelFor(SelectionLabelKind.Face, 2));
        }

        [Test]
        public void DriverRebuildsTextsOnlyOnChangeButAlwaysFollowsTheBounds()
        {
            var source = new Source { Target = Target(SelectionLabelKind.Part, "Bolt:1") };
            var driver = new SelectionLabelDriver(_label, new NavigationStack(), source.Get);
            driver.Tick();
            var first = _label.WorldPosition;
            source.Target = Target(SelectionLabelKind.Part, "Bolt:1", at: new Vector3(0, 0.5f, 1));
            driver.Tick();
            Assert.AreEqual(first.y + 0.5f, _label.WorldPosition.y, 0.06f, "bounds followed");
            Assert.AreEqual("Bolt:1", _label.NameText);
            source.Target = Target(SelectionLabelKind.Part, "Bolt:2");
            driver.Tick();
            Assert.AreEqual("Bolt:2", _label.NameText);
        }

        [Test]
        public void DriverFollowsNavigationLevelChanges()
        {
            var nav = new NavigationStack();
            nav.Reset(new NavLevel("doc_asm", DocContext.Assembly, "Assieme1"));
            var source = new Source { Target = Target(SelectionLabelKind.Face, "Piastra", 2) };
            var driver = new SelectionLabelDriver(_label, nav, source.Get);
            driver.Tick();
            Assert.AreEqual("Faccia 2 · Livello 1/1", _label.DetailText);
            nav.Push(new NavLevel("doc_part", DocContext.Part, "Piastra", "occ"));
            driver.Tick();
            Assert.AreEqual("Faccia 2 · Livello 2/2", _label.DetailText);
        }
    }
}
