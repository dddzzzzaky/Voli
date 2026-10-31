using System.Collections;
using UnityEngine;

namespace SpikeCourt
{
    /// Setter otomatis (Kageyama): ke titik umpan, menerima pass, lalu melempar Quick Set presisi.
    public class SetterAI : MonoBehaviour, ISetter
    {
        [SerializeField] BallPhysics ball;
        [SerializeField] Team team = Team.Left;
        [SerializeField] Vector3 setSpot = new Vector3(-1.5f, 0f, 1.8f);
        [SerializeField] float handHeight = 2.1f, moveSpeed = 6f, holdTime = 0.12f;
        [SerializeField, Tooltip("Meter. Makin kecil = makin presisi")] float scatter = 0.03f;
        [SerializeField] Animator animator;

        public Vector3 PassTarget => setSpot + Vector3.up * handHeight;
        public float HoldTime => holdTime;

        public void ScheduleQuickSet(float delay, Vector3 contactPoint, float flightTime)
        {
            StopAllCoroutines();
            StartCoroutine(SetRoutine(delay, contactPoint, flightTime));
        }

        IEnumerator SetRoutine(float delay, Vector3 contact, float flight)
        {
            for (float t = 0f; t < delay; t += Time.deltaTime)           // geser ke titik umpan selagi bola terbang
            {
                transform.position = Vector3.MoveTowards(transform.position, setSpot, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = setSpot;
            if (animator) animator.SetTrigger("Set");
            ball.SetPosition(PassTarget);                                // bola "menempel" di tangan
            ball.HitStop(holdTime);
            yield return new WaitForSecondsRealtime(holdTime + 0.02f);
            ball.RegisterTouch(team);
            ball.LaunchByTime(contact + Random.insideUnitSphere * scatter, flight, new Vector3(0f, 0f, -8f));
        }
    }
}
