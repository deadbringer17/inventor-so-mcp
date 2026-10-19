using System.Text.Json;
using System.Text.Json.Nodes;
using Inventor;

internal static partial class Program
{
    private static void RunOptionCases(bool sheet,JsonArray cases)
    {
        RunCase(sheet,"quarter_turn_rotation_observations",cases,false,(doc,sketch,row)=>
        {
            var text=sketch.TextBoxes.AddFitted(Point(10,20),"Rotate59",Type.Missing);
            doc.Update2(false);var initial=Facets(text);var origin=text.Origin;
            var observations=new JsonArray();row["rotations"]=observations;
            foreach(int degrees in new[]{0,90,180,270})
            {
                Phase="quarter_turn_rotation_observations:set_"+degrees;
                double angle=degrees*Math.PI/180;text.Rotation=angle;doc.Update2(false);
                Require(Math.Abs(text.Rotation-angle)<1e-10,"Quarter-turn rotation property differs.");
                var actual=Facets(text);double maximum=0;
                bool topology=initial.Indices.SequenceEqual(actual.Indices)&&initial.Coordinates.Length==actual.Coordinates.Length;
                if(topology)
                    for(int i=0;i<actual.Coordinates.Length;i+=3)
                    {
                        double x=initial.Coordinates[i]-origin.X,y=initial.Coordinates[i+1]-origin.Y;
                        double expectedX=origin.X+x*Math.Cos(angle)-y*Math.Sin(angle);
                        double expectedY=origin.Y+x*Math.Sin(angle)+y*Math.Cos(angle);
                        maximum=Math.Max(maximum,Math.Abs(actual.Coordinates[i]-expectedX)*10);
                        maximum=Math.Max(maximum,Math.Abs(actual.Coordinates[i+1]-expectedY)*10);
                        maximum=Math.Max(maximum,Math.Abs(actual.Coordinates[i+2]-initial.Coordinates[i+2])*10);
                    }
                observations.Add(new JsonObject{["degrees"]=degrees,["properties"]=ReadText(text),["facet_topology_equal"]=topology,
                    ["independent_xy_rotation_max_error_mm"]=topology ? maximum : null,["facet_rotation_matches"]=topology&&maximum<=1e-5});
            }
            row["scope"]="Property acceptance is asserted; facet rotation divergences remain explicit observations, not parity PASS.";
        });
        foreach(bool useName in new[]{false,true})
            RunCase(sheet,"local_style_create_by_"+(useName ? "name" : "object"),cases,false,(doc,sketch,row)=>
            {
                var original=sketch.TextBoxes.AddFitted(Point(10,20),"Default59",Type.Missing).Style;
                var before=ReadStyle(original);var copy=(TextStyle)original.Copy("S1_StyleCreate59_"+Guid.NewGuid().ToString("N"));
                copy.FontSize=.4;copy.Bold=true;copy.Italic=true;row["copied_style"]=ReadStyle(copy);
                Phase="local_style_create:"+(useName ? "name" : "object");
                var text=sketch.TextBoxes.AddFitted(Point(10,40),"StyleCreate59",useName ? (object)copy.Name : copy);
                doc.Update2(false);row["created"]=ReadText(text);
                Require(JsonNode.DeepEquals(before,ReadStyle(original)),"Original style changed while creating styled text.");
                Require(SameCom(text.Style,copy),"Creation did not associate the exact copied native style.");
                Require(Math.Abs(text.Style.FontSize*10-4)<=1e-5&&text.Style.Bold&&text.Style.Italic,"Native copied style values differ.");
            });
    }
    private static (double[] Coordinates,int[] Indices) Facets(TextBox text)
    {
        text.GetFacetsInfo(out int vertices,out int facets,out double[] coordinates,out int[] indices);
        Require(vertices>0&&facets>0&&coordinates.Length==vertices*3&&coordinates.All(double.IsFinite),"Native glyph facets are unavailable or malformed.");
        return(coordinates,indices);
    }
}
