using UnityEngine;

namespace SpikeCourt
{
    public enum Team { Left = -1, Right = 1 }          // Left = tim pemain, Right = lawan
    public enum HitGrade { Miss = 0, Good = 1, Perfect = 2 }

    public struct SpikeResult
    {
        public HitGrade grade;
        public bool unblockable;   // true saat Freak Quick (Burst) aktif -> BlockSystem harus mengabaikan blok
        public Vector3 target;
        public float power;        // 0..1
    }

    /// Konstanta lapangan. Sumbu X = panjang (net di x=0), Z = lebar, Y = tinggi.
    public static class Court
    {
        public const float HalfLength = 9f, HalfWidth = 4.5f, NetHeight = 2.43f, BallRadius = 0.105f;
        public static bool InBounds(Vector3 p) => Mathf.Abs(p.x) <= HalfLength && Mathf.Abs(p.z) <= HalfWidth;
        public static Team SideOf(Vector3 p) => p.x < 0f ? Team.Left : Team.Right;
    }

    /// Kontrak untuk setter otomatis (Kageyama).
    public interface ISetter
    {
        Vector3 PassTarget { get; }      // titik tangan setter tempat bola receive harus mendarat
        float HoldTime { get; }          // jeda bola "menempel" di tangan sebelum dilempar
        void ScheduleQuickSet(float delay, Vector3 contactPoint, float flightTime);
    }
}
