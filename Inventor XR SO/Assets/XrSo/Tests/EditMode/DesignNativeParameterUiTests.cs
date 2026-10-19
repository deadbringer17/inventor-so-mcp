using System;
using System.Linq;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Xr;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace InventorXrSo.Tests
{
    public partial class DesignWorkspaceTests
    {
        private void NativeParametersFixture(int extra=0)
        {
            var parameters=new JArray(
                new JObject{["name"]="Length",["expression"]="2 * Base",["unit"]="mm",["value_mm"]=20},
                new JObject{["name"]="Enabled",["expression"]="False",["unit"]="Boolean",["value_bool"]=false,["evaluated"]=JValue.CreateNull()},
                new JObject{["name"]="Label",["expression"]="\"Left & right\"",["unit"]="Text",["value_text"]="Left & right",["evaluated"]=JValue.CreateNull()},
                new JObject{["name"]="Area",["expression"]="Length * Length",["unit"]="mm^2",["value_mm2"]=400},
                new JObject{["name"]="Missing",["expression"]="Broken",["unit"]="ul",["evaluated"]=JValue.CreateNull()});
            for(int i=0;i<extra;i++) parameters.Add(new JObject{["name"]="Extra"+i,["expression"]="1 ul",["unit"]="ul",["value_unitless"]=1});
            SetField("_context",DesignContext.Parse(new JObject{["document_id"]=_backend.State.DocumentId,["revision"]=_backend.State.Revision,
                ["kind"]="part",["parameters"]=parameters},_backend.State));
            typeof(DesignWorkspace).GetMethod("Refresh",Flags).Invoke(_workspace,null);
        }
        private void NativeParameterPick(string name)
        {
            Do("design.parameters.expressions");
            var item=_workspace.Actions.Single(a=>a.Id.StartsWith("design.pick.",StringComparison.Ordinal) && a.Label.StartsWith(name+" = ",StringComparison.Ordinal));
            Assert.True(item.TryInvoke());
        }
        private void ParameterText(string value)
        {
            var entry=_workspace.ActiveParameterTextEntry;Assert.NotNull(entry);Assert.True(entry.Replace(value));Assert.True(entry.Commit(out _));
        }
        private JObject LastNativeParameterArguments=>(JObject)_backend.LastOperations[0]["arguments"];

        [Test] public void NativeParameterPickerIncludesTypedMissingAndAreaValuesWithoutZeroFallbackAndKeepsPagination()
        {
            NativeParametersFixture(19);Do("design.parameters.expressions");
            var choices=_workspace.Actions.Where(a=>a.Id.StartsWith("design.pick.",StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(24,choices.Length);Assert.AreEqual(3,_workspace.Tabs.Count(t=>t.Id.StartsWith(DesignWorkspace.PickTabPrefix,StringComparison.Ordinal)));
            StringAssert.Contains("False",choices.Single(a=>a.Label.StartsWith("Enabled = ")).Label);
            StringAssert.Contains("\"Left & right\"",choices.Single(a=>a.Label.StartsWith("Label = ")).Label);
            StringAssert.Contains("400",choices.Single(a=>a.Label.StartsWith("Area = ")).Label);
            StringAssert.Contains("valore non disponibile",choices.Single(a=>a.Label.StartsWith("Missing = ")).Label);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
        [Test] public void LegacyNumericPickerOmitsBooleanTextAndUnknownEvaluatedValues()
        {
            NativeParametersFixture();Do("design.parameters");
            var choices=_workspace.Actions.Where(a=>a.Id.StartsWith("design.pick.",StringComparison.Ordinal)).ToArray();
            Assert.AreEqual(1,choices.Length);Assert.AreEqual("Length = 20 mm",choices[0].Label);
            Assert.True(choices[0].TryInvoke());Commit(23);Assert.AreEqual(1,_backend.Previews);Assert.Zero(_backend.Commits);
            Assert.AreEqual("23 mm",(string)LastNativeParameterArguments["value"]);
        }
        [TestCase("Length","Base * 3 + 2 mm")]
        [TestCase("Enabled","True")]
        [TestCase("Label","\"New & native\"")]
        [TestCase("Area","Length * Length")]
        [TestCase("Missing","5 ul")]
        public void NativeExpressionRemainsLiteralAndRequiresExplicitPreviewApply(string name,string expression)
        {
            NativeParametersFixture();NativeParameterPick(name);Do("design.parameter.expression");
            Assert.Null(_workspace.ArmedField);Assert.True(_workspace.KeypadOpen);ParameterText(expression);
            Assert.Null(_workspace.ActiveParameterTextEntry);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Assert.True(Enabled(CommitIds.Preview));Assert.False(Enabled(CommitIds.Apply));
            Do(CommitIds.Preview);Assert.AreEqual(1,_backend.LastOperations.Count);Assert.AreEqual("set_parameter",(string)_backend.LastOperations[0]["command"]);
            Assert.AreEqual(name,(string)LastNativeParameterArguments["name"]);Assert.AreEqual(JTokenType.String,LastNativeParameterArguments["value"].Type);
            Assert.AreEqual(expression,(string)LastNativeParameterArguments["value"]);Assert.Zero(_backend.Commits);
            Do(CommitIds.Apply);Assert.AreEqual(1,_backend.Commits);
        }
        [Test] public void CreateParameterCollectsNameExpressionAndUnitBeforeMakingOneDraft()
        {
            NativeParametersFixture();Do("design.parameter.create");ParameterText("Height");Assert.False(Enabled(CommitIds.Preview));
            ParameterText("Length / 2");Assert.False(Enabled(CommitIds.Preview));ParameterText("mm");
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);Do(CommitIds.Preview);
            Assert.AreEqual("create_parameter",(string)_backend.LastOperations[0]["command"]);Assert.AreEqual("Height",(string)LastNativeParameterArguments["name"]);
            Assert.AreEqual("Length / 2",(string)LastNativeParameterArguments["expression"]);Assert.AreEqual("mm",(string)LastNativeParameterArguments["unit"]);
        }
        [TestCase(false)][TestCase(true)] public void RenameAndDeleteUseExistingBatchVocabularyWithoutImplicitMutation(bool delete)
        {
            NativeParametersFixture();NativeParameterPick("Label");Do(delete ? "design.parameter.delete" : "design.parameter.rename");
            if(!delete) ParameterText("Description");Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);Do(CommitIds.Preview);
            Assert.AreEqual(delete ? "delete_parameter" : "rename_parameter",(string)_backend.LastOperations[0]["command"]);
            Assert.AreEqual("Label",(string)LastNativeParameterArguments["name"]);
            if(!delete) Assert.AreEqual("Description",(string)LastNativeParameterArguments["new_name"]);
        }
        [TestCase("back")][TestCase("close")][TestCase("offline")][TestCase("revision")][TestCase("refresh")]
        public void NativeParameterCallbacksAreRevokedWhenTheirContextEnds(string interruption)
        {
            NativeParametersFixture();NativeParameterPick("Length");var oldDelete=Act("design.parameter.delete");Do("design.parameter.expression");
            var old=_workspace.ActiveParameterTextEntry;Assert.True(old.Replace("42 mm"));
            switch(interruption)
            {
                case "back":_workspace.Back();break;
                case "close":_workspace.Close();break;
                case "offline":_workspace.SetOnline(false);break;
                case "revision":_workspace.SetDocumentState(new DocumentState("doc_bolt","new_revision","v"));break;
                case "refresh":Do("design.refresh");break;
            }
            Assert.False(old.Commit(out _));Assert.False(oldDelete.TryInvoke());Assert.Null(_workspace.ActiveParameterTextEntry);
            Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
        [Test] public void RefreshRevokesRetainedParameterPickerChoice()
        {
            NativeParametersFixture();Do("design.parameters.expressions");var choice=PickItem(_workspace.Actions.First(a=>a.Id.StartsWith("design.pick.")).Label);
            Do("design.refresh");Assert.False(choice.TryInvoke());Assert.False(Enabled("design.parameter.delete"));Assert.Zero(_backend.Previews);
        }
        [Test] public void NativeParameterCommandsStayDisabledInsideTheNativeSketchEditor()
        {
            OpenNative();Assert.False(Enabled("design.parameters.expressions"));Assert.False(Enabled("design.parameter.create"));
            Assert.False(Enabled("design.parameters"));Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
        [Test] public void InvalidUnicodeAndCancelledCreateCannotMakeAPreviewableOperation()
        {
            NativeParametersFixture();Do("design.parameter.create");ParameterText("\ud800");ParameterText("10 mm");ParameterText("mm");
            Assert.False(Enabled(CommitIds.Preview));Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            Do("design.parameter.create");ParameterText("Fresh");_workspace.Back();
            Assert.Null(_workspace.ActiveParameterTextEntry);Assert.False(Enabled(CommitIds.Preview));
        }
        [Test] public void ExistingMultilineExpressionIsNotSilentlyFlattenedIntoAReplacement()
        {
            NativeParametersFixture();Field<DesignContext>("_context").Parameters[0]["expression"]="Length\n + 1 mm";
            NativeParameterPick("Length");Do("design.parameter.expression");
            Assert.AreEqual("",_workspace.ActiveParameterTextEntry.Value);Assert.False(_workspace.ActiveParameterTextEntry.Commit(out _));
            StringAssert.Contains("riga singola",AllHud());Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
            _workspace.Back();Assert.False(Enabled(CommitIds.Preview));
        }
        [Test] public void LegacyNumericCallbackCannotApplyAParameterFromTheContextBeforeRefresh()
        {
            NativeParametersFixture();Do("design.parameters");Pick("Length = 20 mm");var old=_workspace.ActiveEntry;
            Do("design.refresh");old.CommitValue(35,out _);Assert.Zero(_backend.Previews);Assert.Zero(_backend.Commits);
        }
    }
}
