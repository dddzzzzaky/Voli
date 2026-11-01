using System;
using UnityEngine;

namespace SpikeCourt
{
    /// Bar "Semangat Juang". Terisi dari receive/spike sukses; penuh = Freak Quick Attack siap.
    public class BurstMeter : MonoBehaviour
    {
        [SerializeField] float max = 100f;
        public float Value { get; private set; }
        public float Normalized => Value / max;
        public bool IsFull => Value >= max - 0.01f;
        public event Action<float> Changed;                 // nilai ternormalisasi 0..1 (untuk UI)

        public void Add(float amount) { Value = Mathf.Clamp(Value + amount, 0f, max); Changed?.Invoke(Normalized); }
        public void Consume() { Value = 0f; Changed?.Invoke(0f); }
    }
}
