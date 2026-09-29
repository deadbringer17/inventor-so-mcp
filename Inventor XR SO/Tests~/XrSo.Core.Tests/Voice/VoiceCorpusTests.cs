using System;
using System.IO;
using InventorXrSo.Core.Voice;
using Newtonsoft.Json.Linq;

namespace XrSo.Core.Tests.Voice
{
    /// <summary>Verifica il corpus di riferimento stt-bench con router e parser C#.</summary>
    public class VoiceCorpusTests
    {
        private static JArray LoadCorpus()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, "Tools~", "stt-bench", "corpus", "phrases.json");
                if (File.Exists(p)) return (JArray)JObject.Parse(File.ReadAllText(p))["entries"];
                dir = dir.Parent;
            }
            throw new FileNotFoundException("corpus phrases.json non trovato risalendo da " + AppContext.BaseDirectory);
        }

        private static readonly ICommandAvailability AllEnabled = new DelegateCommandAvailability(_ => CommandAvailability.Available);

        [Fact]
        public void Corpus_matches_csharp_router_and_parser()
        {
            var router = new VoiceCommandRouter();
            var entries = LoadCorpus();
            Assert.True(entries.Count >= 60);
            foreach (var e in entries)
            {
                string id = (string)e["id"], text = (string)e["text"], kind = (string)e["kind"];
                var route = router.Route(text, AllEnabled);
                var q = ItalianNumberParser.Parse(text);
                if (kind == "command")
                {
                    string expected = (string)e["expected"]["command_id"];
                    Assert.True(CommandIds.IsKnown(expected), id);
                    Assert.True(route.Kind == VoiceRouteKind.Recognized || route.Kind == VoiceRouteKind.ShowApplyConfirmation, id);
                    Assert.Equal(expected, route.CommandId);
                    Assert.False(q.Ok, id);
                }
                else if (kind == "numeric")
                {
                    Assert.True(q.Ok, id + ": " + q.Reason);
                    Assert.Equal((double)e["expected"]["value"], q.Value, 9);
                    var u = e["expected"]["unit"];
                    var expUnit = u.Type == JTokenType.Null ? QuantityUnit.None : (string)u == "mm" ? QuantityUnit.Millimeters : QuantityUnit.Degrees;
                    Assert.Equal(expUnit, q.Unit);
                    Assert.Equal(VoiceRouteKind.Rejected, route.Kind);
                }
                else
                {
                    Assert.Equal(VoiceRouteKind.Rejected, route.Kind);
                    Assert.False(q.Ok, id);
                }
            }
        }
    }
}
