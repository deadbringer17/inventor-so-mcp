using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Mcp;

namespace InventorXrSo.Core.Verify
{
    public enum FindingSeverity { Error, Warning }

    /// <summary>One row of the Risultati list: what is wrong, a value, and the direct occurrences (and boxes) to bring into focus.</summary>
    public sealed class VerifyFinding
    {
        public VerifyFinding(FindingSeverity severity, string title, string detail, IReadOnlyList<string> occurrenceIds, IReadOnlyList<VerifyBox> boxes = null)
        {
            Severity = severity; Title = title; Detail = detail ?? "";
            OccurrenceIds = occurrenceIds ?? Array.Empty<string>();
            Boxes = boxes ?? Array.Empty<VerifyBox>();
        }
        public FindingSeverity Severity { get; }
        public string Title { get; }
        public string Detail { get; }
        public IReadOnlyList<string> OccurrenceIds { get; }
        public IReadOnlyList<VerifyBox> Boxes { get; }
    }

    public static class VerifyFindings
    {
        private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

        public static string Millimetres(double value) => value.ToString("0.###", It);

        private static string[] Ids(params string[] ids) => ids.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();

        public static IReadOnlyList<VerifyFinding> FromInterference(InterferenceReport report) => report.Pairs
            .OrderByDescending(p => p.VolumeMm3)
            .Select(p => new VerifyFinding(FindingSeverity.Error, "Interferenza: " + p.AName + " ↔ " + p.BName,
                Millimetres(p.VolumeMm3) + " mm³", Ids(p.AOccurrenceId, p.BOccurrenceId), p.Boxes))
            .ToArray();

        public static string Summary(InterferenceReport report) => report.Count == 0
            ? "Nessuna interferenza su " + report.Analyzed + " occorrenze."
            : report.Count + " interferenze su " + report.Analyzed + " occorrenze, " + Millimetres(report.TotalVolumeMm3) + " mm³ in totale.";

        public static IReadOnlyList<VerifyFinding> FromHealth(HealthReport report)
        {
            var rows = new List<VerifyFinding>();
            foreach (var issue in report.Issues)
                rows.Add(new VerifyFinding(FindingSeverity.Error, (issue.Kind == "joint" ? "Giunto in errore: " : "Vincolo in errore: ") + issue.Name,
                    issue.Health, Ids(issue.AOccurrenceId, issue.BOccurrenceId)));
            foreach (var free in report.Unconstrained)
                rows.Add(new VerifyFinding(FindingSeverity.Warning, "Non vincolato: " + free.Name, "6 gradi di libertà", Ids(free.OccurrenceId)));
            foreach (var bom in report.BomIssues)
                rows.Add(new VerifyFinding(bom.Severity == "error" ? FindingSeverity.Error : FindingSeverity.Warning,
                    "Distinta: " + BomText(bom.Code), bom.Message, Array.Empty<string>()));
            return rows;
        }

        public static string Summary(HealthReport report)
        {
            int relationships = report.Issues.Count, free = report.Unconstrained.Count, bom = report.BomIssues.Count;
            if (relationships + free + bom == 0) return "Assieme sano: nessun vincolo in errore, nessun componente libero, distinta valida.";
            return "Assieme con " + (relationships + free + bom) + " problemi: " + relationships + " vincoli o giunti in errore, "
                + free + " componenti non vincolati, " + bom + " righe di distinta.";
        }

        public static string BomText(string code)
        {
            switch (code)
            {
                case "PART_NUMBER_MISSING": return "numero di parte mancante";
                case "PART_NUMBER_DUPLICATE": return "numero di parte duplicato";
                case "DESCRIPTION_CONFLICT": return "descrizioni in conflitto";
                case "DESCRIPTION_MISSING": return "descrizione mancante";
                case "QUANTITY_INVALID": return "quantità non valida";
                case "BOM_TRUNCATED": return "distinta troncata";
                default: return code ?? "problema";
            }
        }
    }

    public static class VerifyMessages
    {
        public const string Busy = "Un'altra verifica è in corso.";
        public const string Timeout = "Inventor sta ancora calcolando. Riprova tra poco o restringi alla selezione.";

        public static string For(Exception ex)
        {
            if (!(ex is McpException mcp)) return "Verifica non riuscita.";
            switch (mcp.Code)
            {
                case "STALE_REVISION":
                case "DOCUMENT_CHANGED": return "Il modello è cambiato, rilancia.";
                case "TIMEOUT": return Timeout;
                case "WRONG_DOCUMENT_TYPE": return "Serve un assieme.";
                case "EXPERIMENTAL_DISABLED": return "Verifiche non attive sul server.";
                default: return "Verifica non riuscita (" + mcp.Code + ").";
            }
        }
    }
}
