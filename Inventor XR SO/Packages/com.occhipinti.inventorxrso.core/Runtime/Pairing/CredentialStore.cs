using System;
using System.IO;

namespace InventorXrSo.Core.Pairing
{
    /// <summary>Encrypts the stored pairing (Android Keystore on the headset).</summary>
    public interface IProtector
    {
        string Protect(string plain);
        string Unprotect(string protectedText);
    }

    public sealed class PlainProtector : IProtector
    {
        public string Protect(string plain) => plain;
        public string Unprotect(string protectedText) => protectedText;
    }

    public interface ICredentialStore
    {
        PairedServer Load();
        void Save(PairedServer server);
        void Clear();
    }

    public sealed class FileCredentialStore : ICredentialStore
    {
        private readonly string _path;
        private readonly IProtector _protector;

        public FileCredentialStore(string path, IProtector protector)
        {
            _path = path;
            _protector = protector;
        }

        public PairedServer Load()
        {
            if (!File.Exists(_path)) return null;
            try { return PairedServer.FromJson(_protector.Unprotect(File.ReadAllText(_path))); }
            catch (Exception)
            {
                // Corrupt file or a reset Keystore key: forget it, the user pairs again.
                Clear();
                return null;
            }
        }

        public void Save(PairedServer server)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
            var temp = _path + ".tmp";
            File.WriteAllText(temp, _protector.Protect(server.ToJson()));
            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }

        public void Clear()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
