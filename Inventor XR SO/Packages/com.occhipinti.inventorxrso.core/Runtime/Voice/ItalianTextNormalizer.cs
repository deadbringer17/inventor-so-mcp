using System.Globalization;
using System.Text;

namespace InventorXrSo.Core.Voice
{
    /// <summary>Normalizzazione del testo italiano condivisa da router e parser numerico.</summary>
    public static class ItalianTextNormalizer
    {
        public static string StripAccents(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string d = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Minuscolo, senza accenti ne punteggiatura, spazi singoli. Le cifre restano.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string s = StripAccents(text.ToLowerInvariant());
            var sb = new StringBuilder(s.Length);
            bool space = true;
            foreach (char c in s)
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) { sb.Append(c); space = false; }
                else if (!space) { sb.Append(' '); space = true; }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
