using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Inventor.So.Pairing.Desktop;

/// <summary>HIVE/EnerBot desktop profile. Official static fonts are embedded locally.</summary>
internal sealed class PairingTheme : IDisposable
{
    internal static readonly Color Navy = ColorTranslator.FromHtml("#0d2030");
    internal static readonly Color Paper = ColorTranslator.FromHtml("#f4f8fb");
    internal static readonly Color Ink = ColorTranslator.FromHtml("#102235");
    internal static readonly Color Teal = ColorTranslator.FromHtml("#326975");
    internal static readonly Color Signal = ColorTranslator.FromHtml("#fdd11b");
    internal static readonly Color Error = ColorTranslator.FromHtml("#b03050");
    private readonly PrivateFontCollection _collection = new();
    private readonly List<IntPtr> _buffers = [];
    private readonly List<Font> _fonts = [];
    private bool _disposed;

    public PairingTheme()
    {
        foreach (var weight in new[] { "Medium", "Bold" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Inventor.So.Pairing.Satoshi-" + weight + ".ttf");
            if (stream == null) continue;
            using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            var data = bytes.ToArray();
            var buffer = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, buffer, data.Length);
            _buffers.Add(buffer);
            try { _collection.AddMemoryFont(buffer, data.Length); }
            catch (ArgumentException) { /* Explicit Segoe UI fallback on unsupported Windows font loaders. */ }
        }
    }

    public Font Font(float size, FontStyle style = FontStyle.Regular)
    {
        var family = _collection.Families.FirstOrDefault(f => f.IsStyleAvailable(style));
        var font = family == null ? new Font("Segoe UI", size, style) : new Font(family, size, style);
        _fonts.Add(font);
        return font;
    }

    public void Apply(System.Windows.Forms.Control root)
    {
        root.BackColor = Paper;
        root.ForeColor = Ink;
        foreach (System.Windows.Forms.Control child in root.Controls)
        {
            Apply(child);
            if (child is PictureBox) { child.BackColor = Color.White; continue; }
            if (child is TextBox or ComboBox) child.BackColor = Color.White;
            if (child is Button button)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = Teal;
                button.FlatAppearance.MouseOverBackColor = ColorTranslator.FromHtml("#285660");
                button.Padding = new Padding(10, 5, 10, 5);
                button.MinimumSize = new Size(0, 40);
                void Restyle()
                {
                    bool primary = button.Name == "GenerateCode" && button.Enabled;
                    button.BackColor = primary ? Signal : button.Enabled ? Teal : ColorTranslator.FromHtml("#eaf2f7");
                    button.ForeColor = primary || !button.Enabled ? Ink : Color.White;
                    button.FlatAppearance.MouseOverBackColor = primary ? Color.White : ColorTranslator.FromHtml("#285660");
                    button.FlatAppearance.MouseDownBackColor = primary ? Paper : Navy;
                }
                button.EnabledChanged += (_, _) => Restyle();
                Restyle();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var font in _fonts) font.Dispose();
        _collection.Dispose();
        foreach (var buffer in _buffers) Marshal.FreeCoTaskMem(buffer);
    }
}
