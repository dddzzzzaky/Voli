using UnityEngine;

namespace SpikeCourt
{
    /// Efek billboard 2.5D: sprite selalu sejajar bidang pandang kamera (tidak miring oleh perspektif).
    public class SpriteBillboard : MonoBehaviour
    {
        [SerializeField] bool yAxisOnly = true;             // true = sprite tetap tegak saat kamera menunduk
        Camera cam;

        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            if (!cam) return;
            Vector3 f = cam.transform.forward;
            if (yAxisOnly) f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
        }
    }
}
