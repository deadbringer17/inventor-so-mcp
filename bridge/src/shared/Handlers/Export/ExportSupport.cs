#if INVENTOR2022 || INVENTOR2023 || INVENTOR2024 || INVENTOR2025 || INVENTOR2026 || INVENTOR2027
using System;
using Inventor;

namespace Bimwright.Ipt.Shared.Handlers.Export;

/// <summary>
/// Shared helpers for the export handlers: resolving Inventor's built-in translator add-ins by their
/// stable ClassId GUIDs and running a <c>SaveCopyAs</c> to a target file. The Inventor API exposes
/// STEP / STL / etc. as <see cref="TranslatorAddIn"/> instances looked up via
/// <c>Application.ApplicationAddIns.ItemById(classId)</c>; the actual write is
/// <see cref="TranslatorAddIn.SaveCopyAs"/> against a <see cref="DataMedium"/> whose
/// <c>FileName</c> is the output path.
/// </summary>
internal static class ExportSupport
{
    // Stable Inventor translator ClassId GUIDs (consistent across 2022-2027).
    public const string StepTranslatorId = "{90AF7F40-0C01-11D5-8E83-0010B541CD80}";
    public const string StlTranslatorId  = "{533E9A98-FC3B-11D4-8E7E-0010B541CD80}";

    public static void SavePdf(Application app, object source, string outputPath)
    {
        var translator = GetTranslator(app, "{0AC6FD96-2F4D-42CE-8BE0-8AEA580399E4}", "PDF");
        var context = app.TransientObjects.CreateTranslationContext();
        context.Type = IOMechanismEnum.kFileBrowseIOMechanism;
        var options = app.TransientObjects.CreateNameValueMap();
        if (!translator.HasSaveCopyAsOptions[source, context, options]) throw new InvalidOperationException("PDF export options unavailable.");
        options.Value["All_Color_AS_Black"] = 1;
        options.Value["Vector_Resolution"] = 400;
        options.Value["Sheet_Range"] = PrintRangeEnum.kPrintAllSheets;
        var medium = app.TransientObjects.CreateDataMedium(); medium.FileName = outputPath;
        translator.SaveCopyAs(source, context, options, medium);
    }

    /// <summary>Look up a built-in translator add-in by ClassId GUID, or throw a friendly error.</summary>
    public static TranslatorAddIn GetTranslator(Application app, string classId, string label)
    {
        try
        {
            var addin = app.ApplicationAddIns.ItemById[classId] as TranslatorAddIn;
            if (addin == null)
                throw new InvalidOperationException($"{label} translator add-in is not available in this Inventor session.");
            if (!addin.Activated)
                addin.Activate();
            return addin;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"failed to resolve {label} translator: {ex.Message}");
        }
    }

    /// <summary>Run a translator export of <paramref name="source"/> (a document) to <paramref name="outputPath"/>.</summary>
    public static void SaveCopyAs(Application app, TranslatorAddIn translator, object source, string outputPath)
    {
        var to = app.TransientObjects;
        var context = to.CreateTranslationContext();
        context.Type = IOMechanismEnum.kFileBrowseIOMechanism;
        var options = to.CreateNameValueMap();
        var medium = to.CreateDataMedium();
        medium.FileName = outputPath;
        translator.SaveCopyAs(source, context, options, medium);
    }
}
#endif
