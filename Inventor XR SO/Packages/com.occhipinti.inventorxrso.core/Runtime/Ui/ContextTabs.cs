using System.Collections.Generic;
using InventorXrSo.Core.Navigation;

namespace InventorXrSo.Core.Ui
{
    /// <summary>Cosa è vero adesso: decide le schede che compaiono da sole.</summary>
    public sealed class TabState
    {
        public bool SketchOpen, FeatureInProgress, FeatureEditOpen;
        public string FeatureName;
    }

    /// <summary>Schede principali e gruppo Ispeziona per tipo di documento (M9, spec §2).</summary>
    public static class ContextTabs
    {
        /// <summary>Scheda «Ispeziona ▸» che apre il gruppo.</summary>
        public const string Inspect = "ispeziona";
        public const string SketchConstraints = "vincoli_schizzo";
        public const string FeatureOptions = "opzioni_feature";
        public const string FeatureEditPrefix = "feature_";

        public static IReadOnlyList<string> Main(DocContext c, TabState s)
        {
            s = s ?? new TabState();
            var tabs = new List<string>();
            if (s.FeatureInProgress) tabs.Add(FeatureOptions);

            switch (c)
            {
                case DocContext.Assembly:
                    tabs.Add("componenti");
                    tabs.Add("vincoli");
                    break;
                case DocContext.Part:
                    tabs.Add("schizzo");
                    if (s.SketchOpen) tabs.Add(SketchConstraints);
                    tabs.Add("feature");
                    tabs.Add("parametri");
                    break;
                case DocContext.SheetMetal:
                    tabs.Add("lamiera");
                    tabs.Add("schizzo");
                    if (s.SketchOpen) tabs.Add(SketchConstraints);
                    tabs.Add("sviluppo");
                    break;
            }

            if (s.FeatureEditOpen && c != DocContext.Assembly && !string.IsNullOrWhiteSpace(s.FeatureName))
            {
                var id = FeatureEditPrefix + s.FeatureName;
                if (!tabs.Contains(id)) tabs.Add(id);
            }

            tabs.Add(Inspect);
            tabs.Add("vista");
            tabs.Add("documento");
            return tabs;
        }

        public static IReadOnlyList<string> InspectGroup(DocContext c)
            => c == DocContext.Assembly
                ? new[] { "misura", "sezione", "visibilita", "verifica" }
                : new[] { "misura", "sezione" };
    }
}
