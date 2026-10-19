using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Inventor;
using Bimwright.Ipt.Shared.Handlers;

internal static partial class Program
{
    private static void RunTextClone72(PlanarSketch sketch)
    {
        var cases=new JsonArray();Report["native_constructor_clone_cases"]=cases;
        Report["constructor_clone_policy"]="A new native fitted TextBox is created from the exact FormattedText read from the owned source and its explicitly owned local TextStyle. Native overrides and color are copied; no native Copy=true call or reconstructed glyphs.";
        Report["not_covered"]=new JsonArray("Non-fitted and multiline TextBoxes", "Nonzero rotation", "All spacing/alignment/font/opacity combinations", "External document parameter/property references", "Constraint and downstream-feature transfer", "Mixed copy/rotate copy", "Product preview/commit and desktop equivalence", "Quest and physical interaction");
        foreach(bool parameterCase in new[]{false,true})
        {
            GuardDocument();var baseline=CaptureClone72(sketch);var row=new JsonObject {["case"]=parameterCase ? "local_parameter_field" : "fitted_literal",["baseline"]=baseline.DeepClone()};cases.Add(row);
            Require(sketch.TextBoxes.Count==0,"Each constructor-clone subcase must start with no TextBoxes.");
            Transaction? transaction=null;Exception? primary=null;
            try
            {
                transaction=Native("TransactionManager.StartTransaction.clone72",()=>App!.TransactionManager.StartTransaction((_Document)OwnedDocument!,"S1 native constructor clone72, always aborted"));
                Require(!Native("Transaction.HasParentTransaction.clone72",()=>transaction.HasParentTransaction),"Constructor research refuses nested transactions.");
                UserParameter? parameter=null;
                string input="Clone72";
                if(parameterCase)
                {
                    int count=Native("UserParameters.Count.beforeCreator72",()=>OwnedDocument!.ComponentDefinition.Parameters.UserParameters.Count);row["user_parameters_before_creator"]=count;
                    Require(count==0,"Parameter-field stencil requires a zero-user-parameter baseline; none will be removed to force this fixture.");
                    parameter=Native("UserParameters.AddByExpression.creator72",()=>OwnedDocument!.ComponentDefinition.Parameters.UserParameters.AddByExpression("S1Clone72Length","10 mm","mm"));
                    row["source_parameter_key"]=Key(parameter);row["source_parameter_name"]=parameter.Name;
                    input="Length=<Parameter Name='S1Clone72Length' Precision='0'>S1Clone72Length</Parameter>";
                    row["native_creator_input_stencil"]=input;
                }
                var source=Native("TextBoxes.AddFitted.source72",()=>sketch.TextBoxes.AddFitted(Point(10,20),input,Type.Missing));
                var local=Native("TextStyle.Copy.ownedLocal72",()=> (TextStyle)source.Style.Copy(parameterCase ? "S1_Clone72_Parameter" : "S1_Clone72_Literal"));
                Require(local.StyleLocation==StyleLocationEnum.kLocalStyleLocation,"Copied text style is not local to the owned document; no library mutation permitted.");
                // All font changes affect only this new style in the owned fixture.
                Native("TextStyle.FontSize.local72",()=>{local.FontSize=UnitConvert.MmToCm(4);});
                Native("TextStyle.Bold.local72",()=>{local.Bold=true;});
                Native("TextStyle.Italic.local72",()=>{local.Italic=false;});
                Native("TextStyle.Underline.local72",()=>{local.Underline=false;});
                var styleColor=Native("TransientObjects.CreateColor.style72",()=>App!.TransientObjects.CreateColor(32,48,64,1));
                Native("TextStyle.Color.local72",()=>{local.Color=styleColor;});
                // Assigning Style to an existing TextBox failed in native research59.
                // Use the verified native constructor style argument instead.
                Native("TextBox.Delete.styleSeed72",()=>source.Delete());
                source=Native("TextBoxes.AddFitted.localSource72",()=>sketch.TextBoxes.AddFitted(Point(10,20),input,local.Name));
                Native("TextBox.Rotation.source72",()=>{source.Rotation=0;});
                Native("TextBox.WidthScale.source72",()=>{source.WidthScale=1.25;});
                Native("TextBox.Horizontal.source72",()=>{source.HorizontalJustification=HorizontalTextAlignmentEnum.kAlignTextCenter;});
                Native("TextBox.Vertical.source72",()=>{source.VerticalJustification=VerticalTextAlignmentEnum.kAlignTextMiddle;});
                Native("TextBox.SingleLine.source72",()=>{source.SingleLineText=true;});
                Native("TextBox.LineSpacingType.source72",()=>{source.LineSpacingType=TextLineSpacingTypeEnum.kMultipleLineSpacing;});
                Native("TextBox.LineSpacing.source72",()=>{source.LineSpacing=1.2;});
                var color=Native("TransientObjects.CreateColor.text72",()=>App!.TransientObjects.CreateColor(80,60,140,1));
                Native("TextBox.Color.source72",()=>{source.Color=color;});
                RebuildClone72(sketch,"source");
                Require(sketch.TextBoxes.Count==1,"Native creator must produce exactly one source TextBox.");
                var original=ReadCloneText72(source);row["source_before_clone"]=original.DeepClone();string sourceKey=Key(source),sourceOriginKey=Key(source.OriginSketchPoint);
                if(parameter!=null) {row["source_binding"]=ParameterBinding72(source,parameter.Name);Require(ContainsCloneValue72(source.Text,10),"Native source creator did not resolve the initial 10 mm value.");}
                // The constructor input is read from the native source, not rebuilt
                // from a guessed field grammar or resolved literal Text property.
                var clone=ConstructClone72(sketch,source,Point(22,12),"actualClone");
                RebuildClone72(sketch,"initialClone");
                Require(sketch.TextBoxes.Count==2,"Native constructor clone did not produce exactly two TextBoxes.");
                string cloneKey=Key(clone),cloneOriginKey=Key(clone.OriginSketchPoint);
                Require(cloneKey!=sourceKey && cloneOriginKey!=sourceOriginKey,"Constructor clone does not have distinct native text/origin identities.");
                row["clone_key"]=cloneKey;row["clone_origin_key"]=cloneOriginKey;row["after_clone"]=CaptureClone72(sketch);
                row["original_exact_after_clone"]=JsonNode.DeepEquals(original,ReadCloneText72(source));
                row["initial_translation"]=CheckTextTranslation70(sketch,original,ReadCloneText72(clone),12,-8);
                Require((bool?)row["original_exact_after_clone"]==true && (bool?)row["initial_translation"]?["pure_native_translation"]==true,"Constructor clone changed the original or failed native glyph/font/property translation.");
                var updates=new JsonArray();row["native_updates"]=updates;
                if(parameter==null)
                {
                    double dx=12,dy=-8;
                    foreach(var offset in new[]{(x:3d,y:4d),(x:-2d,y:5d)})
                    {
                        dx+=offset.x;dy+=offset.y;var update=new JsonObject {["delta_mm"]=new JsonArray(offset.x,offset.y)};updates.Add(update);
                        GuardDocument();var location=Point(10+dx,20+dy);Native("TextBox.Origin.actualClone72",()=>{clone.Origin=location;});RebuildClone72(sketch,"literalEdit");
                        update["state"]=CaptureClone72(sketch);update["translation"]=CheckTextTranslation70(sketch,original,ReadCloneText72(clone),dx,dy);
                        update["original_exact"]=JsonNode.DeepEquals(original,ReadCloneText72(source));
                        Require((bool?)update["original_exact"]==true && (bool?)update["translation"]?["pure_native_translation"]==true && Key(clone)==cloneKey && Key(clone.OriginSketchPoint)==cloneOriginKey && sketch.TextBoxes.Count==2,"Literal native constructor clone edit lost identity, original preservation or glyph translation.");
                    }
                }
                else
                {
                    string parameterKey=Key(parameter);var previousSource=ReadCloneText72(source);var previousClone=ReadCloneText72(clone);
                    row["clone_binding_before_updates"]=ParameterBinding72(clone,parameter.Name);
                    foreach(int value in new[]{22,7})
                    {
                        var update=new JsonObject {["source_parameter_mm"]=value};updates.Add(update);
                        GuardDocument();Native("UserParameter.Expression.update72",()=>{parameter.Expression=value.ToString(CultureInfo.InvariantCulture)+" mm";});RebuildClone72(sketch,"parameterEdit");
                        var currentSource=ReadCloneText72(source);var currentClone=ReadCloneText72(clone);
                        update["source"]=currentSource.DeepClone();update["clone"]=currentClone.DeepClone();update["source_binding"]=ParameterBinding72(source,parameter.Name);update["clone_binding"]=ParameterBinding72(clone,parameter.Name);
                        update["translation"]=CheckTextTranslation70(sketch,currentSource,currentClone,12,-8);
                        bool values=ContainsCloneValue72(source.Text,value) && ContainsCloneValue72(clone.Text,value) && source.Text==clone.Text && source.Text!=(string?)previousSource["text"] && clone.Text!=(string?)previousClone["text"];
                        bool glyphsChanged=!JsonNode.DeepEquals(previousSource["raw_model_vertices_mm"],currentSource["raw_model_vertices_mm"]) && !JsonNode.DeepEquals(previousClone["raw_model_vertices_mm"],currentClone["raw_model_vertices_mm"]);
                        bool invariants=CloneTextInvariants72(original,currentSource) && CloneTextInvariants72(original,currentClone,true);
                        update["both_native_resolved_values_changed"]=values;update["both_raw_glyph_meshes_changed"]=glyphsChanged;update["pose_font_style_and_identity_invariants"]=invariants;
                        Require(values && glyphsChanged && invariants && (bool?)update["translation"]?["pure_native_translation"]==true && Key(parameter)==parameterKey
                            && Math.Abs(UnitConvert.CmToMm(Convert.ToDouble(parameter.Value))-value)<=1e-6 && Key(source)==sourceKey && Key(source.OriginSketchPoint)==sourceOriginKey && Key(clone)==cloneKey && Key(clone.OriginSketchPoint)==cloneOriginKey && sketch.TextBoxes.Count==2,
                            "Existing source and constructor clone did not both update native parameter binding, resolved text, glyphs and preserved identities before the oracle.");
                        update["assertions_before_constructor_oracle"]=true;Tick("clone72.parameter.asserted_before_oracle",new JsonObject {["source_value_mm"]=value});
                        // This independent fresh constructor is allowed only after
                        // the existing source/clone dependency assertions passed.
                        var oracle=ConstructClone72(sketch,source,Point(22,12),"afterAssertOracle");RebuildClone72(sketch,"freshOracle");
                        Require(sketch.TextBoxes.Count==3 && Key(oracle)!=sourceKey && Key(oracle)!=cloneKey,"Fresh constructor oracle does not have its own third native identity.");
                        update["fresh_constructor_oracle"]=ReadCloneText72(oracle);
                        update["fresh_oracle_translation"]=CheckTextTranslation70(sketch,currentClone,(JsonObject)update["fresh_constructor_oracle"]!,0,0);
                        Require((bool?)update["fresh_oracle_translation"]?["pure_native_translation"]==true,"Updated native clone glyphs differ from the fresh native constructor oracle.");
                        Native("TextBox.Delete.ownedOracle72",()=>oracle.Delete());RebuildClone72(sketch,"oracleDelete");
                        Require(sketch.TextBoxes.Count==2 && JsonNode.DeepEquals(currentSource,ReadCloneText72(source)) && JsonNode.DeepEquals(currentClone,ReadCloneText72(clone)),"Creating/deleting the oracle changed either existing native TextBox.");
                        update["existing_source_and_clone_exact_across_oracle"]=true;previousSource=currentSource;previousClone=currentClone;
                    }
                }
                row["case_completed"]=true;
            }
            catch(Exception error) {primary=error;row["failure"]=Error(error);throw;}
            finally
            {
                if(transaction!=null)
                {
                    try
                    {
                        GuardDocument();Require(Same(Native("TransactionManager.CurrentTransaction.beforeCloneAbort72",()=>App!.TransactionManager.CurrentTransaction),transaction),"Owned constructor transaction changed; no unrelated Abort permitted.");
                        Native("Transaction.Abort.clone72",()=>transaction.Abort());var restored=CaptureClone72(sketch);row["after_abort"]=restored;bool exact=JsonNode.DeepEquals(baseline,restored);row["exact_abort"]=exact;Report["exact_abort"]=exact;
                        Require(exact,"Constructor clone Abort failed exact text/style/parameter/geometry baseline restoration.");
                    }
                    catch(Exception error) {row["rollback_failure"]=Error(error);Report["exact_abort"]=false;if(primary==null) throw;}
                }
            }
        }
        Require(cases.Count==2 && cases.OfType<JsonObject>().All(row=>(bool?)row["case_completed"]==true && (bool?)row["exact_abort"]==true),"Both constructor-clone cases and exact Abort are required.");
        Report["copy_and_second_native_edit_verified"]=true;
    }

    private static TextBox ConstructClone72(PlanarSketch sketch,TextBox source,Point2d origin,string role)
    {
        GuardDocument();string nativeMarkup=Native("TextBox.FormattedText.readForConstructor72",()=>source.FormattedText);var style=Native("TextBox.Style.readForConstructor72",()=>source.Style);
        Require(style.StyleLocation==StyleLocationEnum.kLocalStyleLocation,"Constructor clone requires the explicitly owned local style.");
        var target=Native("TextBoxes.AddFitted."+role+"72",()=>sketch.TextBoxes.AddFitted(origin,nativeMarkup,style));
        Native("TextBox.Rotation.transfer72",()=>{target.Rotation=source.Rotation;});Native("TextBox.WidthScale.transfer72",()=>{target.WidthScale=source.WidthScale;});
        Native("TextBox.Horizontal.transfer72",()=>{target.HorizontalJustification=source.HorizontalJustification;});Native("TextBox.Vertical.transfer72",()=>{target.VerticalJustification=source.VerticalJustification;});
        Native("TextBox.SingleLine.transfer72",()=>{target.SingleLineText=source.SingleLineText;});Native("TextBox.LineSpacingType.transfer72",()=>{target.LineSpacingType=source.LineSpacingType;});Native("TextBox.LineSpacing.transfer72",()=>{target.LineSpacing=source.LineSpacing;});
        var color=Native("TransientObjects.CreateColor.transfer72",()=>App!.TransientObjects.CreateColor(source.Color.Red,source.Color.Green,source.Color.Blue,source.Color.Opacity));
        Native("Color.ColorSourceType.transfer72",()=>{color.ColorSourceType=source.Color.ColorSourceType;});Native("TextBox.Color.transfer72",()=>{target.Color=color;});
        return target;
    }
    private static void RebuildClone72(PlanarSketch sketch,string phase)
    {
        Native("Sketch.Solve."+phase+"72",()=>sketch.Solve());Require(Native("Document.Update2."+phase+"72",()=>OwnedDocument!.Update2(false)),"Constructor clone native update failed.");
        Require(Native("Document.Rebuild2."+phase+"72",()=>OwnedDocument!.Rebuild2(false)),"Constructor clone native rebuild failed.");Require(sketch.ConstraintStatus!=ConstraintStatusEnum.kOverConstrainedConstraintStatus,"Constructor clone solver conflict.");
    }
    private static JsonObject ReadCloneText72(TextBox text)
    {
        var state=ReadFittedText70(text);state["color"]=CloneColor72(text.Color);((JsonObject)state["style"]!)["color"]=CloneColor72(text.Style.Color);return state;
    }
    private static JsonObject CloneColor72(Color color)=>Native("Color.Readback72",()=>new JsonObject {["red"]=color.Red,["green"]=color.Green,["blue"]=color.Blue,["opacity"]=color.Opacity,["source_type"]=color.ColorSourceType.ToString()});
    private static JsonObject CaptureClone72(PlanarSketch sketch)
    {
        var state=CaptureText(sketch);state["texts"]=new JsonArray(sketch.TextBoxes.Cast<TextBox>().Select(text=>(JsonNode)ReadCloneText72(text)).OrderBy(node=>(string?)node["key"],StringComparer.Ordinal).ToArray());
        foreach(TextStyle style in OwnedDocument!.TextStyles)
        {
            var item=((JsonArray)state["text_styles"]!).OfType<JsonObject>().Single(row=>(string?)row["key"]==Key(style));item["color"]=CloneColor72(style.Color);
        }
        state["user_parameter_count"]=OwnedDocument.ComponentDefinition.Parameters.UserParameters.Count;return state;
    }
    private static JsonObject ParameterBinding72(TextBox text,string parameterName)
    {
        var nodes=XElement.Parse("<root>"+Native("TextBox.FormattedText.parameterBinding72",()=>text.FormattedText)+"</root>").Descendants("Parameter").ToArray();
        Require(nodes.Length==1 && (string?)nodes[0].Attribute("Name")==parameterName && string.Equals((string?)nodes[0].Attribute("Resolved"),"True",StringComparison.OrdinalIgnoreCase),"Native parameter field is missing, unresolved or names another source.");
        return new JsonObject {["name"]=(string?)nodes[0].Attribute("Name"),["resolved"]=(string?)nodes[0].Attribute("Resolved"),["component_identifier"]=(string?)nodes[0].Attribute("ComponentIdentifier"),["native_field_xml"]=nodes[0].ToString(System.Xml.Linq.SaveOptions.DisableFormatting)};
    }
    private static bool ContainsCloneValue72(string text,int expected)=>Regex.Matches(text,@"[-+]?\d+(?:[.,]\d+)?").Any(match=>double.TryParse(match.Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double value) && Math.Abs(value-expected)<=1e-8);
    private static bool CloneTextInvariants72(JsonObject baseline,JsonObject current,bool translated=false)
    {
        var one=(JsonObject)baseline.DeepClone();var two=(JsonObject)current.DeepClone();
        foreach(string field in new[]{"text","formatted_text","width_mm","height_mm","fitted_width_mm","fitted_height_mm","range_mm","vertex_count","facet_count","raw_model_vertices_mm","indices_one_based"}) {one.Remove(field);two.Remove(field);}
        if(translated) foreach(string field in new[]{"key","origin_key","origin_mm"}) {one.Remove(field);two.Remove(field);}
        return JsonNode.DeepEquals(one,two);
    }
}

