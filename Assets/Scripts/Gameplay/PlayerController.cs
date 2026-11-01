using System;
using System.Collections;
using UnityEngine;

namespace SpikeCourt
{
    public enum PlayerState { Idle, Moving, AttackRun, Airborne, Recovering }

    /// Kontrol Hinata di lapangan 3D dengan sprite 2D (billboard).
    /// Fase bertahan : gerak bebas -> ring timing -> Receive.
    /// Fase menyerang: lari + lompat otomatis -> pemain menekan Spike tepat saat umpan quick set tiba.
    /// Logika bergerak di XZ (Rigidbody kinematik); tinggi lompatan (Height) hanya menggeser sprite & titik jangkau.
    [RequireComponent(typeof(Rigidbody))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Referensi")]
        [SerializeField] BallPhysics ball;
        [SerializeField] MonoBehaviour setterBehaviour;                  // harus implementasi ISetter
        [SerializeField] BurstMeter burst;
        [SerializeField] Transform spriteRoot;                           // anak: SpriteRenderer + SpriteBillboard
        [SerializeField] SpriteRenderer spriteRenderer;
        [SerializeField] Animator animator;
        [SerializeField] Team team = Team.Left;

        [Header("Gerak")]
        [SerializeField] float moveSpeed = 7.5f, acceleration = 45f;

        [Header("Receive (timing ring)")]
        [SerializeField] float receiveHeight = 0.7f;                     // tinggi platform lengan
        [SerializeField] float receiveReach = 1.1f, diveReach = 2.0f;
        [SerializeField] float ringLeadTime = 1.2f;                      // ring mulai tampil X detik sebelum kontak
        [SerializeField] float perfectWindow = 0.07f, goodWindow = 0.16f;

        [Header("Serangan Freak Quick")]
        [SerializeField] Vector3 quickSpot = new Vector3(-1.6f, 0f, 1.0f);   // tempat Hinata melompat
        [SerializeField] float jumpApex = 1.6f, jumpGravity = 24f, handReach = 1.9f;
        [SerializeField] float setFlight = 0.45f, decoyArc = 1.8f;
        [SerializeField] float spikeEarlyWindow = 0.30f, spikeLateWindow = 0.12f;

        // ---- status & event (UI, kamera sinematik, VFX, AudioManager berlangganan di sini)
        public PlayerState State { get; private set; }
        public float Height { get; private set; }
        public Vector3 ReachPoint => transform.position + Vector3.up * (Height + handReach);
        public Vector3 QuickContactPoint => quickSpot + Vector3.up * (jumpApex + handReach);

        public event Action<Vector3, float, float> OnReceiveWindow;    // titik kontak, sisa waktu, total (ring = sisa/total)
        public event Action OnReceiveWindowClosed;
        public event Action<HitGrade> OnReceived;
        public event Action<float, Vector3> OnAttackSequenceStarted;    // durasi total, titik spike -> kamera sinematik
        public event Action<SpikeResult> OnSpiked;
        public event Action OnBurstArmed, OnBurstFired;

        Rigidbody rb;
        ISetter setter;
        Vector3 velocity;
        Vector2 touchMove, keyMove;
        bool windowActive, burstArmed;
        Vector3 contactPos;
        float timeToContact, lastPressTime = -99f;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            setter = setterBehaviour as ISetter;
            if (setter == null) Debug.LogError("setterBehaviour harus mengimplementasikan ISetter (SetterAI).");
        }

        // ------------------------------------------------------------------ input (dipanggil UI mobile / Input System)
        public void OnMove(Vector2 v) => touchMove = Vector2.ClampMagnitude(v, 1f);
        public void OnBurst()
        {
            if (burst != null && burst.IsFull && !burstArmed) { burstArmed = true; OnBurstArmed?.Invoke(); }
        }
        public void OnAction()                                           // satu tombol: Receive (bertahan) / Spike (menyerang)
        {
            lastPressTime = Time.unscaledTime;
            if (State == PlayerState.Idle || State == PlayerState.Moving) TryReceive();
        }

        void ReadKeyboard()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            keyMove = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (Input.GetKeyDown(KeyCode.Space)) OnAction();
            if (Input.GetKeyDown(KeyCode.LeftShift)) OnBurst();
#endif
        }

        // ------------------------------------------------------------------ loop
        void Update() { ReadKeyboard(); UpdateReceiveWindow(); }

        void FixedUpdate()
        {
            if (State == PlayerState.Idle || State == PlayerState.Moving) Move(Time.fixedDeltaTime);
        }

        void LateUpdate()
        {
            if (spriteRoot) spriteRoot.localPosition = Vector3.up * Height;          // lompatan = geser sprite, bayangan tetap di lantai
            if (animator) { animator.SetFloat("Speed", velocity.magnitude); animator.SetBool("Airborne", State == PlayerState.Airborne); }
        }

        void Move(float dt)
        {
            Vector2 inp = touchMove.sqrMagnitude > 0.01f ? touchMove : keyMove;
            velocity = Vector3.MoveTowards(velocity, CameraRelative(inp) * moveSpeed, acceleration * dt);
            Vector3 p = rb.position + velocity * dt;
            float minX = team == Team.Left ? -Court.HalfLength - 1f : 0.6f;
            float maxX = team == Team.Left ? -0.6f : Court.HalfLength + 1f;
            p.x = Mathf.Clamp(p.x, minX, maxX);
            p.z = Mathf.Clamp(p.z, -Court.HalfWidth - 1f, Court.HalfWidth + 1f);
            rb.MovePosition(p);
            if (Mathf.Abs(velocity.x) > 0.1f) FaceX(velocity.x);
            State = velocity.sqrMagnitude > 0.05f ? PlayerState.Moving : PlayerState.Idle;
        }

        static Vector3 CameraRelative(Vector2 i)
        {
            Camera cam = Camera.main;
            if (!cam) return new Vector3(i.x, 0f, i.y);
            Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
            Vector3 r = cam.transform.right; r.y = 0f; r.Normalize();
            return r * i.x + f * i.y;
        }

        void FaceX(float dirX) { if (spriteRenderer) spriteRenderer.flipX = dirX < 0f; }   // sprite digambar menghadap kanan

        // ------------------------------------------------------------------ FASE BERTAHAN: receive
        void UpdateReceiveWindow()
        {
            bool open = false;
            if ((State == PlayerState.Idle || State == PlayerState.Moving) && ball.LastTouchTeam != team && !ball.IsFrozen
                && ball.TryGetContact(receiveHeight, out contactPos, out timeToContact)
                && Court.SideOf(contactPos) == team && Court.InBounds(contactPos)
                && timeToContact <= ringLeadTime && timeToContact > -goodWindow)
            {
                open = true;
                OnReceiveWindow?.Invoke(contactPos, timeToContact, ringLeadTime);
            }
            if (!open && windowActive) OnReceiveWindowClosed?.Invoke();
            windowActive = open;
        }

        void TryReceive()
        {
            if (!windowActive) { if (animator) animator.SetTrigger("Whiff"); return; }
            float dist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(contactPos.x, contactPos.z));
            float dt = Mathf.Abs(timeToContact);
            HitGrade g = dt <= perfectWindow ? HitGrade.Perfect : dt <= goodWindow ? HitGrade.Good : HitGrade.Miss;
            if (dist > diveReach || g == HitGrade.Miss) { if (animator) animator.SetTrigger("Whiff"); return; }
            if (dist > receiveReach) { g = HitGrade.Good; if (animator) animator.SetTrigger("Dive"); }   // jangkauan jauh = dive, maks. Good
            else if (animator) animator.SetTrigger("Receive");
            DoReceive(g);
        }

        void DoReceive(HitGrade g)
        {
            windowActive = false; OnReceiveWindowClosed?.Invoke();
            ball.SetPosition(contactPos);                                // bola tepat di platform lengan
            ball.RegisterTouch(team);

            float scatter = g == HitGrade.Perfect ? 0f : 0.6f;           // receive buruk = umpan melenceng
            Vector3 target = setter.PassTarget + new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)) * scatter;
            float passT = ball.LaunchByApex(target, g == HitGrade.Perfect ? 4.2f : 3.6f);
            float stop = g == HitGrade.Perfect ? 0.06f : 0.03f;
            ball.HitStop(stop);

            if (burst) burst.Add(g == HitGrade.Perfect ? 12f : 6f);
            setter.ScheduleQuickSet(passT + stop, QuickContactPoint, setFlight);
            OnReceived?.Invoke(g);

            float total = passT + stop + setter.HoldTime + setFlight;
            StartCoroutine(AttackRoutine(total));
        }

        // ------------------------------------------------------------------ FASE MENYERANG: Freak Quick
        IEnumerator AttackRoutine(float total)
        {
            State = PlayerState.AttackRun;
            OnAttackSequenceStarted?.Invoke(total, QuickContactPoint);   // CinematicCamera berganti sudut di sini

            float v0 = Mathf.Sqrt(2f * jumpGravity * jumpApex), tApex = v0 / jumpGravity;
            float runT = Mathf.Max(0.2f, total - tApex);                 // sampai di spot tepat saat harus melompat
            Vector3 p0 = transform.position, p2 = quickSpot;
            Vector3 p1 = (p0 + p2) * 0.5f + Vector3.forward * decoyArc;  // lengkung "decoy" di belakang setter
            if (animator) animator.SetTrigger("Run");

            for (float t = 0f; t < runT; t += Time.deltaTime)
            {
                float u = 1f - (1f - t / runT) * (1f - t / runT);        // ease-out
                Vector3 pos = (1 - u) * (1 - u) * p0 + 2 * (1 - u) * u * p1 + u * u * p2;
                rb.position = pos; transform.position = pos;
                FaceX(1f);
                yield return null;
            }
            transform.position = rb.position = p2;

            State = PlayerState.Airborne;
            if (animator) animator.SetTrigger("Jump");
            for (float j = 0f; j < tApex; j += Time.deltaTime) { Height = v0 * j - 0.5f * jumpGravity * j * j; yield return null; }
            Height = jumpApex;

            // Titik kontak: bola & Hinata "menahan napas" sesaat agar tombol yang telat sedikit masih terhitung
            float contactT = Time.unscaledTime;
            if (animator) animator.SetTrigger("Spike");
            ball.HitStop(spikeLateWindow);
            yield return new WaitForSecondsRealtime(spikeLateWindow + 0.02f);
            ResolveSpike(contactT);

            for (float j = tApex; j < tApex * 2f; j += Time.deltaTime) { Height = Mathf.Max(0f, v0 * j - 0.5f * jumpGravity * j * j); yield return null; }
            Height = 0f; State = PlayerState.Recovering;
            yield return new WaitForSeconds(0.35f);
            velocity = Vector3.zero; State = PlayerState.Idle;
        }

        void ResolveSpike(float contactT)
        {
            float delta = lastPressTime - contactT;                      // negatif = terlalu cepat, positif = telat
            HitGrade g = HitGrade.Miss;
            if (delta >= -spikeEarlyWindow && delta <= spikeLateWindow)
            {
                float a = Mathf.Abs(delta);
                g = a <= perfectWindow ? HitGrade.Perfect : a <= goodWindow ? HitGrade.Good : HitGrade.Miss;
            }

            var r = new SpikeResult { grade = g };
            if (g != HitGrade.Miss)
            {
                r.power = g == HitGrade.Perfect ? 1f : 0.7f;
                r.unblockable = burstArmed;                              // Freak Quick: tidak bisa diblok
                r.target = PickTarget(g);
                float flight = Mathf.Lerp(0.55f, 0.30f, r.power);
                ball.RegisterTouch(team);
                ball.HitStop(g == HitGrade.Perfect ? 0.09f : 0.05f);
                ball.LaunchByTime(r.target, flight, new Vector3(0f, 0f, 25f * r.power));   // topspin
                if (burst) burst.Add(g == HitGrade.Perfect ? 15f : 8f);
                if (burstArmed) { burstArmed = false; if (burst) burst.Consume(); OnBurstFired?.Invoke(); }   // VFX oranye/hitam
            }
            OnSpiked?.Invoke(r);
        }

        static Vector3 PickTarget(HitGrade g)
        {
            float sign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            return g == HitGrade.Perfect
                ? new Vector3(UnityEngine.Random.Range(6f, 8.4f), 0.1f, sign * UnityEngine.Random.Range(2.5f, 4.2f))   // sudut belakang
                : new Vector3(UnityEngine.Random.Range(3.5f, 7f), 0.1f, UnityEngine.Random.Range(-3f, 3f));
        }
    }
}
