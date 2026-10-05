#if XR_SO_ACCEPTANCE
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace InventorXrSo.Xr
{
    /// <summary>
    /// Opt-in, fixture-scoped M9 runner for definitions that are LOADED BUT HAVE NO WINDOW in Inventor (fixture m9h, guard XR_M9H_Quest_Acceptance).
    /// It is the user's real situation: only the top assembly Robot has a window, every referenced definition was loaded by Inventor without one
    /// (<c>Documents.Open(path, false)</c>). <c>Document.Activate()</c> alone fails on such a document (E_FAIL); the bridge handler
    /// <c>activate_open_document_xr</c> must show the window of the already loaded document and activate it. The other fixtures keep every
    /// definition open WITH a window, which is why they never caught it.
    ///
    /// Scenario: exactly the one of <see cref="M9FlexQuestAcceptance"/> (Robot > AsmFlex > AsmFlexInner > PartX, the face-feature edit with XR Undo,
    /// Torna three times, then AsmFixed > AsmInner and back), run on the m9h fixture. It only passes when windowless definitions can be entered.
    /// When an entry fails the run stops with the diagnostics of the base scenario and this runner first records the EXACT notice of the Assieme
    /// workspace and the full HUD text the user would read. Whether a document has a window is read by <c>--inspect-quest m9h</c>
    /// (Documents.VisibleDocuments), which the orchestrating script prints before and after the run: the Quest cannot observe windows.
    /// Note: the Torna path activates the parent document, so documents that were entered DO have a window afterwards.
    ///
    /// Same rules as the other M9 runners: input is SYNTHETIC, Inventor answers are real, PASS COMPLETE only without NOT COVERED sub-cases.
    /// Written without a device: compiled and contract-tested, never run.
    /// </summary>
    internal sealed class M9HiddenQuestAcceptance : M9FlexQuestAcceptance
    {
        internal new static readonly string[] ReflectedMembers =
        {
            "AssemblyWorkspace._busy",
        };

        protected override string Milestone => "m9h";
        protected override string FixtureMilestone => "m9h";
        protected override int TimeoutSeconds => 960;
        protected override string RestoreHint => "--restore-quest m9h";
        protected override string CompletionNote =>
            "SYNTHETIC input and the dedicated hidden-definition fixture (m9h) only; the physical trial (M9-12), the user's real assembly and an assembly constraint on a flexible "
            + "sub-assembly (the fixture has none) are separate evidence and remain open";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => StartIfRequested<M9HiddenQuestAcceptance>("xr_m9h_acceptance");

        protected override async Task Run(CancellationToken ct)
        {
            Record("Hidden-definition fixture m9h (guard " + FixturePrefix + "): every definition is loaded WITHOUT a window, only Robot has one; the window state is printed by --inspect-quest m9h before and after the run");
            try
            {
                await base.Run(ct);
            }
            catch (Exception)
            {
                Record("M9H entry failed. Notice of the Assieme workspace as the user reads it: '" + (_assembly?.Notice ?? "").Replace('\n', '|') + "'");
                Record("M9H entry failed. Full HUD canvas text: '" + HudText().Replace('\n', '|') + "'");
                throw;
            }
            Pass("M9H-hidden-entry", "every definition entered by the flex scenario (AsmFlex, AsmFlexInner, PartX, AsmFixed, AsmInner) was loaded without a window: the activation showed its window "
                + "and activated it; the window state itself is confirmed by --inspect-quest m9h (Documents.VisibleDocuments), not by the headset");
        }
    }
}
#endif
