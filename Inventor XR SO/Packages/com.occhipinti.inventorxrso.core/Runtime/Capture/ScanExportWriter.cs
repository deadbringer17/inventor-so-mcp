using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace InventorXrSo.Core.Capture
{
    public static class ScanExportWriter
    {
        /// <summary>Publishes a new self-contained export directory only after all files and their manifest exist.</summary>
        public static string WriteNew(string parent,ScanReview review,string format,byte[] verifiedAsset)
        {
            if(format!="glb"&&format!="ply"&&format!="obj") throw new ArgumentException("Invalid export format.");
            if(!Guid.TryParseExact(review.SessionId,"N",out _)||verifiedAsset==null||verifiedAsset.Length<1||verifiedAsset.Length>32*1024*1024) throw new ArgumentException("Invalid export.");
            string root=Path.Combine(parent,review.SessionId+"-r"+review.Revision+"-"+Guid.NewGuid().ToString("N")),pending=root+".pending";
            var files=new Dictionary<string,byte[]> { ["scan."+format]=verifiedAsset,
                ["units.json"]=Json(review.Units),["quality_report.json"]=Json(review.Quality),["review.json"]=Json(review.Annotations),["constraints.json"]=Json(review.Constraints) };
            try
            {
                Directory.CreateDirectory(pending);
                foreach(var file in files) using(var output=new FileStream(Path.Combine(pending,file.Key),FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {output.Write(file.Value,0,file.Value.Length);output.Flush(true);}
                using(var sha=SHA256.Create())
                {
                    var manifest=JsonConvert.SerializeObject(new {scan_id=review.SessionId,revision=review.Revision,review_version=review.Annotations.Value<int>("version"),format,
                        unit=format=="glb"?"metre":"mm",geometry="estimated",uncertainty_status="not_estimated",evidence_kind=review.Quality.Value<string>("evidence_kind")??"unverified",
                        files=files.Select(file=>new {name=file.Key,size=file.Value.Length,sha256=BitConverter.ToString(sha.ComputeHash(file.Value)).Replace("-","").ToLowerInvariant()}).ToArray()},Formatting.Indented);
                    File.WriteAllText(Path.Combine(pending,"export.json"),manifest);
                }
                Directory.Move(pending,root);return root;
            }
            finally {if(Directory.Exists(pending)) Directory.Delete(pending,true);}
        }
        private static byte[] Json(object value)
        {var bytes=Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value,Formatting.Indented));if(bytes.Length>1024*1024) throw new InvalidDataException("Export metadata exceeds limits.");return bytes;}
    }
}
