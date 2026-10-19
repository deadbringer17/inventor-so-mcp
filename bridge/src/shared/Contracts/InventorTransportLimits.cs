namespace Bimwright.Ipt.Shared.Contracts;

/// <summary>Wire limits shared by the server serializer and add-in listeners.</summary>
public static class InventorTransportLimits
{
    /// <summary>UTF-8 bytes of one compact JSON envelope, excluding its newline delimiter.</summary>
    public const int MaxRequestLineBytes = 1024 * 1024;
}
