namespace InventorXrSo.Core.Input
{
    /// <summary>Regole pure della legenda 3D dei controller (spec M9 §3 «Legenda 3D»).</summary>
    public static class LegendVisibility
    {
        /// <summary>Opacità di base, controller fuori dal cono di sguardo.</summary>
        public const float BaseOpacity = 0.35f;
        /// <summary>Opacità piena, controller dentro il cono di sguardo.</summary>
        public const float FullOpacity = 1f;
        /// <summary>Semiapertura del cono di sguardo attorno all'asse della testa, in gradi (inclusa).</summary>
        public const float ConeDegrees = 25f;
        /// <summary>Lunghezza massima di un'etichetta.</summary>
        public const int MaxLabelChars = 12;

        /// <summary>Opacità dato l'angolo (gradi) tra l'asse della testa e la direzione verso il controller. Angolo non valido: opacità di base.</summary>
        public static float Opacity(double angleDeg)
        {
            if (double.IsNaN(angleDeg)) return BaseOpacity;
            return System.Math.Abs(angleDeg) <= ConeDegrees ? FullOpacity : BaseOpacity;
        }

        /// <summary>Taglia difensivamente il testo a <see cref="MaxLabelChars"/> caratteri; null diventa "".</summary>
        public static string Fit(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= MaxLabelChars ? text : text.Substring(0, MaxLabelChars);
        }
    }
}
