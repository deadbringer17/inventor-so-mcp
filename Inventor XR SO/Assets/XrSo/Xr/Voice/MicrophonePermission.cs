using System;
using System.Threading;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace InventorXrSo.Xr.Voice
{
    /// <summary>Permesso RECORD_AUDIO, richiesto alla prima pressione del push-to-talk (mai all'avvio).</summary>
    public interface IMicrophonePermission
    {
        bool IsGranted { get; }
        /// <summary>Avvia la richiesta; il callback puo arrivare da un thread qualunque.</summary>
        void Request(Action<bool> done);
    }

    public sealed class AndroidMicrophonePermission : IMicrophonePermission
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        public bool IsGranted => Permission.HasUserAuthorizedPermission(Permission.Microphone);

        public void Request(Action<bool> done)
        {
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => done(true);
            callbacks.PermissionDenied += _ => done(false);
            callbacks.PermissionDeniedAndDontAskAgain += _ => done(false);
            Permission.RequestUserPermission(Permission.Microphone, callbacks);
        }
#else
        // Editor / piattaforme desktop: nessun permesso di runtime da chiedere.
        public bool IsGranted => true;
        public void Request(Action<bool> done) => done(true);
#endif
    }
}
