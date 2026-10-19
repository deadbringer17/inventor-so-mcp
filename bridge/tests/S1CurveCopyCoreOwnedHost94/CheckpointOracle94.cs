using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static partial class Program
{
    private static void VerifyCheckpointTrace94(JObject client)
    {
        Require89(client["checkpoint_trace94"] is JArray trace&&trace.Count==74,"Oracle94 checkpoint trace count differs.");
        var states=new Dictionary<int,JObject>();var rows=new Dictionary<int,JObject>();int ordinal=0;
        foreach(JObject row in (JArray)client["checkpoint_trace94"]!)
        {
            ordinal++;Require89((int?)row["ordinal"]==ordinal,"Oracle94 checkpoint ordinal differs.");
            var request=(JObject)row["request"]!;var ack=(JObject)row["ack"]!;string nonce=(string)request["nonce"]!;
            string file="copy-native-checkpoint94-"+ordinal.ToString("D3")+".json";
            Require89((string?)request["run_id"]==Settings.RunId&&(string?)ack["run_id"]==Settings.RunId
                &&(string?)ack["nonce"]==nonce&&CheckpointNonces94.Contains(nonce)&&(string?)ack["file"]==file,"Oracle94 checkpoint metadata differs.");
            var raw=ReadPublished94(file,64*1024*1024);var state=(JObject)raw["native_state"]!;
            Require89((string?)raw["run_id"]==Settings.RunId&&(string?)raw["nonce"]==nonce&&(string?)raw["native_state_sha256"]==(string?)ack["native_state_sha256"]
                &&HashText73(state.ToString(Formatting.None))==(string?)raw["native_state_sha256"],"Oracle94 checkpoint raw digest differs.");
            states.Add(ordinal,state);rows.Add(ordinal,row);
        }
        var used=new HashSet<int>();
        int Use(JToken? value,string context,string workflow,string stage)
        {
            Require89(value?.Type==JTokenType.Integer,"Oracle94 missing checkpoint reference.");int index=(int)value!;
            Require89(rows.TryGetValue(index,out var row)&&used.Add(index)&&(string?)row["context"]==context&&(string?)row["workflow"]==workflow&&(string?)row["stage"]==stage,"Oracle94 checkpoint stage/reference differs.");return index;
        }
        foreach(JObject workflow in (JArray)client["cases"]!)
        {
            string context=(string)workflow["context"]!,label=(string)workflow["workflow"]!;var cp=(JObject)workflow["checkpoints"]!;
            int before=Use(cp["preview_before"],context,label,"preview_before"),after=Use(cp["preview_cancel_after"],context,label,"preview_cancel_after"),commit=Use(cp["commit_first_core_after"],context,label,"commit_first_core_after");
            Require89(before<after&&after<commit&&ExactNative94(states[before],states[after]),"Oracle94 native PreviewAbort was not exact.");
            PreservePriorState94(states[after],states[commit]);
        }
        var kinds=new[]{"stale_request","foreign_reference","parameter_text_unsupported","mixed_parameter_preflight","post_valid_copy_failure"};
        foreach(string context in new[]{"Part","Sheet"})
        {
            var refusals=((JArray)client["refusals"]!).OfType<JObject>().Where(r=>(string?)r["context"]==context).ToArray();
            Require89(refusals.Length==5&&refusals.Select(r=>(string)r["kind"]!).ToHashSet(StringComparer.Ordinal).SetEquals(kinds),"Oracle94 refusal labels differ.");
            foreach(var refusal in refusals)
            {
                string label=(string)refusal["kind"]!;
                Require89((string?)refusal["document_id"]==Documents.Single(d=>d.Sheet==(context=="Sheet")).DocumentId,"Oracle94 refusal document differs.");
                int before=Use(refusal["before_native_checkpoint"],context,label,"refusal_before"),after=Use(refusal["after_native_checkpoint"],context,label,"refusal_after");
                Require89(before<after&&(bool?)refusal["represented_rollback_exact"]==true&&(bool?)refusal["event_revision_preserved"]==true
                    &&(bool?)refusal["full_native_checkpoint_exact"]==true&&ExactNative94(states[before],states[after]),"Oracle94 native refusal/rollback was not exact.");
                if(label=="stale_request")Require89((string?)refusal["code"]=="STALE_REVISION","Oracle94 stale code differs.");
                else Require89((string?)refusal["code"]=="ROLLED_BACK"&&(string?)refusal["step_code"]=="INVALID_ARGUMENT"&&(int?)refusal["step_index"]==(label=="post_valid_copy_failure"?1:0),"Oracle94 refusal step code differs.");
            }
        }
        Require89(used.Count==74,"Oracle94 unclaimed checkpoint trace row.");
    }
    private static void PreservePriorState94(JObject before,JObject after)
    {
        // Check every prior native object and relation at each commit, not merely
        // final source state. This detects change-and-restore between workflows.
        foreach(var owned in Documents)
        {
            var a=Graph94(before,owned);var b=Graph94(after,owned);
            foreach(string collection in new[]{"entities","constraints"})
            {
                var current=Index94(b[collection]!);
                foreach(var pair in Index94(a[collection]!))Require89(current.TryGetValue(pair.Key,out var same)&&ExactNative94(pair.Value,same),"Oracle94 commit changed a prior native object/relation.");
            }
            var docA=((JArray)before["documents"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId);
            var docB=((JArray)after["documents"]!).Single(d=>(string?)d["document_id"]==owned.DocumentId);
            var textsA=(JArray)((JArray)docA["sketches"]!).Single(s=>(string?)s["name"]=="S1_Copy88")["texts"]!;
            var textsB=Index94(((JArray)docB["sketches"]!).Single(s=>(string?)s["name"]=="S1_Copy88")["texts"]!);
            foreach(JObject text in textsA)Require89(textsB.TryGetValue((string)text["key"]!,out var same)&&ExactNative94(text,same),"Oracle94 commit changed prior native text/glyph/style.");
        }
        Equal88(Unrelated94(before),Unrelated94(after),"commit prior unrelated state/styles/parameters");
    }
}
