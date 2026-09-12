using System;
using System.Text;
using Newtonsoft.Json;

namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Portable, opaque reference including Inventor's saved B-Rep key context.</summary>
public sealed class PersistentEntityReference
{
    public int Version { get; set; } = 1;
    public string DocumentId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public byte[] Key { get; set; } = Array.Empty<byte>();
    public byte[] Context { get; set; } = Array.Empty<byte>();
    public const int MaximumEncodedLength = 131072;

    public string Encode()
    {
        Validate();
        var encoded = "ent_" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (encoded.Length > MaximumEncodedLength) throw new ArgumentException("Entity reference exceeds the supported size.");
        return encoded;
    }

    public static PersistentEntityReference Decode(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaximumEncodedLength || !id.StartsWith("ent_", StringComparison.Ordinal))
            throw new ArgumentException("Invalid entity reference.");
        try
        {
            var value = id.Substring(4).Replace('-', '+').Replace('_', '/');
            value = value.PadRight((value.Length + 3) / 4 * 4, '=');
            var reference = JsonConvert.DeserializeObject<PersistentEntityReference>(Encoding.UTF8.GetString(Convert.FromBase64String(value)))
                ?? throw new ArgumentException("Missing reference payload.");
            reference.Validate();
            return reference;
        }
        catch (Exception ex) when (ex is FormatException || ex is JsonException)
        { throw new ArgumentException("Invalid entity reference payload.", ex); }
    }

    private void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(DocumentId) || DocumentId.Length > 256 ||
            string.IsNullOrWhiteSpace(EntityType) || EntityType.Length > 128 || Key == null || Key.Length == 0 ||
            Context == null || Context.Length == 0)
            throw new ArgumentException("Unsupported or incomplete entity reference.");
    }
}
