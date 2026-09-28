#if UNITY_ANDROID && !UNITY_EDITOR
using InventorXrSo.Core.Pairing;
using UnityEngine;

namespace InventorXrSo.Unity.Pairing
{
    /// <summary>Calls CredentialVault.java; a Java exception surfaces as AndroidJavaException (Load then forgets the file).</summary>
    public sealed class KeystoreProtector : IProtector
    {
        private const string Vault = "com.occhipinti.inventorxrso.CredentialVault";

        public string Protect(string plain)
        {
            using (var vault = new AndroidJavaClass(Vault)) return vault.CallStatic<string>("encrypt", plain);
        }

        public string Unprotect(string protectedText)
        {
            using (var vault = new AndroidJavaClass(Vault)) return vault.CallStatic<string>("decrypt", protectedText);
        }
    }
}
#endif
