using System.Collections.Generic;
using UnityEngine;

namespace InventorXrSo.Unity.Ui
{
    /// <summary>Matematica pura della legenda 3D (posizioni a colonna, lato esterno, punto di attacco della linea guida): testabile senza scena.</summary>
    public static class LegendGeometry
    {
        /// <summary>Distanza (m) dell'etichetta dal baricentro dei tasti, verso l'esterno.</summary>
        public const float OutwardMeters = 0.07f;
        /// <summary>Passo verticale (m) tra due etichette della colonna.</summary>
        public const float SlotSpacing = 0.024f;
        /// <summary>Spostamento (m) in alto del centro della colonna rispetto al baricentro dei tasti.</summary>
        public const float ColumnLift = 0.03f;

        /// <summary>Baricentro di un insieme di posizioni locali (Vector3.zero se vuoto).</summary>
        public static Vector3 Centroid(IEnumerable<Vector3> points)
        {
            var sum = Vector3.zero; int n = 0;
            foreach (var p in points) { sum += p; n++; }
            return n == 0 ? Vector3.zero : sum / n;
        }

        /// <summary>
        /// Destra "orizzontale" della testa: head.right proiettato sul piano orizzontale (così un leggero rollio della testa non inclina la colonna);
        /// se la testa guarda quasi in verticale resta head.right.
        /// </summary>
        public static Vector3 HeadRight(Transform head)
        {
            if (head == null) return Vector3.right;
            var flat = Vector3.ProjectOnPlane(head.right, Vector3.up);
            return flat.sqrMagnitude < 1e-6f ? head.right : flat.normalized;
        }

        /// <summary>Su della testa (orientamento dell'etichetta e asse della colonna).</summary>
        public static Vector3 HeadUp(Transform head) => head == null ? Vector3.up : head.up;

        /// <summary>Direzione verso l'esterno: +destra della testa per il controller destro, -destra per il sinistro.</summary>
        public static Vector3 Outward(Transform head, bool rightHand) => rightHand ? HeadRight(head) : -HeadRight(head);

        /// <summary>
        /// Scostamento verticale (m) dello slot <paramref name="slot"/> (0 = in alto) su <paramref name="count"/> slot consecutivi,
        /// colonna centrata sul baricentro + <see cref="ColumnLift"/>.
        /// </summary>
        public static float StackY(int slot, int count) => ColumnLift + ((count - 1) * 0.5f - slot) * SlotSpacing;

        /// <summary>Posizione mondo dell'etichetta.</summary>
        public static Vector3 LabelPosition(Vector3 centroidWorld, Vector3 outward, Vector3 up, int slot, int count)
            => centroidWorld + outward * OutwardMeters + up * StackY(slot, count);

        /// <summary>Punto del bordo del rettangolo dell'etichetta più vicino a <paramref name="point"/> (il punto stesso se cade dentro).</summary>
        public static Vector3 NearestEdgePoint(Vector3 center, Vector3 axisRight, Vector3 axisUp, float halfWidth, float halfHeight, Vector3 point)
        {
            var d = point - center;
            float x = Vector3.Dot(d, axisRight), y = Vector3.Dot(d, axisUp);
            float cx = Mathf.Clamp(x, -halfWidth, halfWidth), cy = Mathf.Clamp(y, -halfHeight, halfHeight);
            if (Mathf.Approximately(cx, x) && Mathf.Approximately(cy, y))
            {
                // dentro il rettangolo: spingi sul lato più vicino
                float dx = halfWidth - Mathf.Abs(x), dy = halfHeight - Mathf.Abs(y);
                if (dx < dy) cx = halfWidth * Mathf.Sign(x == 0f ? 1f : x); else cy = halfHeight * Mathf.Sign(y == 0f ? 1f : y);
            }
            return center + axisRight * cx + axisUp * cy;
        }
    }
}
