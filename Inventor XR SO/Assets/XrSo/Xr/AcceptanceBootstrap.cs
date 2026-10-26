#if XR_SO_ACCEPTANCE || UNITY_EDITOR
using System;
using System.IO;
using InventorXrSo.Core.Pairing;
using UnityEngine;

namespace InventorXrSo.Xr
{
    public sealed partial class AppController
    {
        private bool _acceptanceBootstrapRequested,_acceptanceBootstrapBlocked;
        internal S1AcceptanceBootstrapMetadata AcceptanceBootstrap {get;private set;}
        internal bool AcceptanceBootstrapBlocked=>_acceptanceBootstrapBlocked;
        internal string AcceptanceBootstrapStage {get;private set;}="not_requested";

        private sealed class EphemeralAcceptanceCredentials : ICredentialStore
        {
            private PairedServer _value;
            internal EphemeralAcceptanceCredentials(PairedServer value){_value=value;}
            public PairedServer Load()=>_value;
            public void Save(PairedServer value){_value=value;}
            public void Clear(){_value=null;}
        }
        // True means handled, including an invalid request. Never fall back to the normal credential store.
        private bool TryStartAcceptanceBootstrap()
        {
            bool requested=false;string runId=null;string dataPath=Application.persistentDataPath;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                AcceptanceBootstrapStage="intent";
                using(var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using(var activity=player.GetStatic<AndroidJavaObject>("currentActivity"))
                using(var intent=activity.Call<AndroidJavaObject>("getIntent"))
                {
                    bool acceptance=intent.Call<bool>("getBooleanExtra","xr_s1_acceptance",false);
                    bool hasRun=intent.Call<bool>("hasExtra","xr_s1_bootstrap_run");
                    if(!acceptance && !hasRun) return false;
                    requested=true;_acceptanceBootstrapRequested=true;
                    _store=new EphemeralAcceptanceCredentials(null);
                    if(!acceptance || !hasRun) throw new FormatException();
                    runId=intent.Call<string>("getStringExtra","xr_s1_bootstrap_run");
                    // Android Intent.removeExtra returns void, not another Intent.
                    intent.Call("removeExtra","xr_s1_bootstrap_run");
                    // run-as uses the private app data directory. Android's FUSE
                    // external persistentDataPath is inaccessible from runas_app.
                    AcceptanceBootstrapStage="private_files";
                    using(var files=activity.Call<AndroidJavaObject>("getFilesDir"))
                        dataPath=files.Call<string>("getAbsolutePath");
                }
            }
            catch(Exception)
            {
                // Reading the QA gate failed: do not risk touching credentials or echo Android exception details.
                _acceptanceBootstrapRequested=true;_store=new EphemeralAcceptanceCredentials(null);
                BlockAcceptanceBootstrap();return true;
            }
#endif
            if(!requested) return false;
            try
            {
                AcceptanceBootstrapStage="read_bootstrap";
                var bootstrap=ReadAcceptanceBootstrapFile(dataPath,runId,DateTimeOffset.UtcNow);
                AcceptanceBootstrapStage="paired_server";
                _server=bootstrap.CreatePairedServer(DateTimeOffset.UtcNow);AcceptanceBootstrap=bootstrap.Metadata;
                _store=new EphemeralAcceptanceCredentials(_server);ShowHome();
                AcceptanceBootstrapStage="start_session";StartSession();AcceptanceBootstrapStage="started";
            }
            catch(Exception){BlockAcceptanceBootstrap();}
            return true;
        }
        internal static S1AcceptanceBootstrap ReadAcceptanceBootstrapFile(string dataPath,string runId,DateTimeOffset now)
        {
            // Filename has no caller-controlled path components. Android getFilesDir
            // supplies the private app root; callers cannot choose it through intent.
            string path=Path.Combine(Path.GetFullPath(dataPath),S1AcceptanceBootstrap.FileName(runId));
            byte[] bytes=null;
            try
            {
                try
                {
                    if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new FormatException();
                    using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None))
                    {
                        if(file.Length<1 || file.Length>S1AcceptanceBootstrap.MaxBytes) throw new FormatException();
                        bytes=new byte[(int)file.Length];int read=0;
                        while(read<bytes.Length){int count=file.Read(bytes,read,bytes.Length-read);if(count==0) throw new FormatException();read+=count;}
                        if(file.ReadByte()!=-1) throw new FormatException();
                    }
                }
                finally
                {
                    // Deletion failure is fatal as well; no session starts with a reusable credential bootstrap on disk.
                    File.Delete(path);
                }
                return S1AcceptanceBootstrap.Parse(bytes,runId,now);
            }
            finally{if(bytes!=null) Array.Clear(bytes,0,bytes.Length);}
        }
        private void TickAcceptanceBootstrap()
        {
            if(_acceptanceBootstrapRequested && !_acceptanceBootstrapBlocked
                && (AcceptanceBootstrap==null || AcceptanceBootstrap.ExpiresUtc<=DateTimeOffset.UtcNow)) BlockAcceptanceBootstrap();
        }
        private void BlockAcceptanceBootstrap()
        {
            _acceptanceBootstrapRequested=true;_acceptanceBootstrapBlocked=true;
            StopSession();_server=null;_store=new EphemeralAcceptanceCredentials(null);
            ShowHome();_home.ShowMessage("S1 QA arrestato","Fase: "+AcceptanceBootstrapStage+". Bootstrap mancante, non valido, scaduto o sessione QA interrotta. Il pairing ordinario resta conservato.");
            _home.SetActions();
        }
    }
}
#endif
