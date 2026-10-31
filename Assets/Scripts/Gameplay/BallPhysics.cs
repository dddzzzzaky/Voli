using System;
using System.Collections;
using UnityEngine;

namespace SpikeCourt
{
    /// Fisika bola 3D: gravitasi kustom, drag udara, efek Magnus (spin), prediksi lintasan,
    /// dan peluncuran "tepat sasaran" yang mengompensasi drag lewat simulasi iteratif.
    /// Setup: Rigidbody (Interpolate, Continuous Dynamic) + SphereCollider (radius 0.105)
    /// dengan Physic Material (Bounciness ~0.65, Bounce Combine = Maximum). Tag lantai "Floor", net "Net".
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public class BallPhysics : MonoBehaviour
    {
        [Header("Fisika")]
        [SerializeField, Range(0.8f, 2f)] float gravityScale = 1.15f;   // >1 = terasa lebih "arcade"
        [SerializeField] float airDrag = 0.012f;
        [SerializeField] float magnusStrength = 0.015f;
        [SerializeField] float maxSpeed = 32f;

        [Header("Visual (opsional)")]
        [SerializeField] Transform shadowBlob;                           // decal bayangan di lantai

        public Team LastTouchTeam { get; private set; } = Team.Right;
        public bool IsFrozen { get; private set; }
        public Vector3 Position => rb.position;
        public Vector3 Velocity => Vel;

        public event Action<Team, Vector3, bool> OnFloorHit;            // sisi, titik, di dalam lapangan?
        public event Action OnNetHit;
        public event Action<Team> OnTouched;

        const float Step = 0.02f;                                        // = Time.fixedDeltaTime default
        Rigidbody rb;
        Vector3 savedVel, savedSpin;

        Vector3 Gravity => Physics.gravity * gravityScale;
        Vector3 Vel
        {
#if UNITY_6000_0_OR_NEWER
            get => rb.linearVelocity; set => rb.linearVelocity = value;
#else
            get => rb.velocity; set => rb.velocity = value;
#endif
        }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;                                       // gravitasi diatur manual
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxAngularVelocity = 60f;
        }

        void FixedUpdate()
        {
            if (IsFrozen) return;
            Vector3 v = Vel;
            rb.AddForce(Gravity, ForceMode.Acceleration);
            rb.AddForce(-airDrag * v.magnitude * v, ForceMode.Acceleration);
            rb.AddForce(magnusStrength * Vector3.Cross(rb.angularVelocity, v), ForceMode.Acceleration); // spin membelokkan bola
            if (v.sqrMagnitude > maxSpeed * maxSpeed) Vel = v.normalized * maxSpeed;
        }

        void Update()
        {
            if (!shadowBlob) return;
            float h = Mathf.Max(0f, rb.position.y);
            shadowBlob.position = new Vector3(rb.position.x, 0.012f, rb.position.z);
            shadowBlob.localScale = Vector3.one * Mathf.Lerp(0.35f, 0.12f, Mathf.Clamp01(h / 6f));
        }

        // ------------------------------------------------------------------ peluncuran
        /// Luncurkan bola agar tiba di 'target' tepat setelah 't' detik (drag & gravitasi dikompensasi).
        public void LaunchByTime(Vector3 target, float t, Vector3 spin = default)
        {
            t = Mathf.Max(0.1f, t);
            Vector3 p0 = rb.position;
            Vector3 v = (target - p0 - 0.5f * Gravity * t * t) / t;      // solusi balistik tanpa drag
            for (int i = 0; i < 4; i++) v += (target - Simulate(p0, v, t)) / t;   // koreksi drag
            Release(v, spin);
        }

        /// Luncurkan dengan lengkung tinggi (umpan/receive). Mengembalikan waktu terbang.
        public float LaunchByApex(Vector3 target, float apexHeight, Vector3 spin = default)
        {
            Vector3 p0 = rb.position;
            float g = -Gravity.y;
            apexHeight = Mathf.Max(apexHeight, Mathf.Max(p0.y, target.y) + 0.3f);
            float tUp = Mathf.Sqrt(2f * (apexHeight - p0.y) / g);
            float tDown = Mathf.Sqrt(2f * (apexHeight - target.y) / g);
            float t = tUp + tDown;
            LaunchByTime(target, t, spin);
            return t;
        }

        void Release(Vector3 v, Vector3 spin)
        {
            StopAllCoroutines();
            rb.isKinematic = false;
            IsFrozen = false;
            Vel = Vector3.ClampMagnitude(v, maxSpeed);
            rb.angularVelocity = spin;
        }

        public void SetPosition(Vector3 pos) { rb.position = pos; transform.position = pos; }

        public void RegisterTouch(Team team) { LastTouchTeam = team; OnTouched?.Invoke(team); }

        /// Hit-stop singkat (bola diam sesaat) untuk efek impact.
        public void HitStop(float seconds) { if (seconds > 0f) { StopAllCoroutines(); StartCoroutine(HitStopRoutine(seconds)); } }

        IEnumerator HitStopRoutine(float s)
        {
            IsFrozen = true;
            savedVel = Vel; savedSpin = rb.angularVelocity;
            rb.isKinematic = true;
            yield return new WaitForSecondsRealtime(s);
            rb.isKinematic = false;
            Vel = savedVel; rb.angularVelocity = savedSpin;
            IsFrozen = false;
        }

        // ------------------------------------------------------------------ prediksi
        Vector3 Accel(Vector3 v) => Gravity - airDrag * v.magnitude * v;

        /// Simulasi posisi bola setelah 'time' detik dari (p, v) -> dipakai peluncuran & prediksi.
        public Vector3 Simulate(Vector3 p, Vector3 v, float time)
        {
            for (float t = 0f; t < time;)
            {
                float dt = Mathf.Min(Step, time - t);
                v += Accel(v) * dt; p += v * dt; t += dt;
            }
            return p;
        }

        /// Kapan & di mana bola (sedang turun) melewati ketinggian y.
        public bool PredictTimeToHeight(float y, out Vector3 point, out float time, float maxTime = 6f)
        {
            Vector3 p = rb.position, v = Vel;
            point = p; time = 0f;
            if (p.y <= y && v.y <= 0f) return false;
            while (time < maxTime)
            {
                Vector3 vn = v + Accel(v) * Step, pn = p + vn * Step;
                if (vn.y < 0f && pn.y <= y)
                {
                    float f = (p.y - y) / Mathf.Max(1e-4f, p.y - pn.y);
                    point = Vector3.Lerp(p, pn, f); time += Step * f; return true;
                }
                p = pn; v = vn; time += Step;
            }
            return false;
        }

        /// Waktu kontak bertanda: positif = bola belum sampai ketinggian y, negatif = sudah lewat (terlambat).
        public bool TryGetContact(float y, out Vector3 pos, out float signedTime)
        {
            if (PredictTimeToHeight(y, out pos, out signedTime)) return true;
            Vector3 p = rb.position, v = Vel;
            if (v.y < -0.1f && p.y < y && p.y > y - 0.6f) { pos = p; signedTime = -(y - p.y) / -v.y; return true; }
            signedTime = 0f; return false;
        }

        // ------------------------------------------------------------------ tabrakan
        void OnCollisionEnter(Collision c)
        {
            if (c.collider.CompareTag("Floor"))
            {
                Vector3 p = c.GetContact(0).point;
                OnFloorHit?.Invoke(Court.SideOf(p), p, Court.InBounds(p));
            }
            else if (c.collider.CompareTag("Net")) OnNetHit?.Invoke();
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            Gizmos.color = Color.yellow;
            Vector3 p = rb.position, v = Vel;
            for (int i = 0; i < 120 && p.y > 0f; i++)
            {
                v += Accel(v) * Step; Vector3 n = p + v * Step;
                Gizmos.DrawLine(p, n); p = n;
            }
        }
    }
}
