using System;

namespace InventorXrSo.Core.Pairing
{
    public sealed class PairingException : Exception
    {
        public PairingException(string code, string message) : base(message) { Code = code; }
        /// <summary>PAIRING_EXPIRED, PAIRING_USED, PAIRING_INVALID or PAIRING_FAILED.</summary>
        public string Code { get; }
    }
}
