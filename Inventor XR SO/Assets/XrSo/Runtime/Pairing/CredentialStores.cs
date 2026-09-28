using System.IO;
using InventorXrSo.Core.Pairing;
using UnityEngine;

namespace InventorXrSo.Unity.Pairing
{
    public static class CredentialStores
    {
        public static ICredentialStore Create()
        {
            var path = Path.Combine(Application.persistentDataPath, "pairing.dat");
#if UNITY_ANDROID && !UNITY_EDITOR
            return new FileCredentialStore(path, new KeystoreProtector());
#else
            return new FileCredentialStore(path, new PlainProtector());
#endif
        }
    }
}
