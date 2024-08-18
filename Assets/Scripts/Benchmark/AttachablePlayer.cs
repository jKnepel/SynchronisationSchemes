using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    [RequireComponent(typeof(Rigidbody))]
    public class AttachablePlayer : MonoBehaviour
    {
        #region attributes

        [SerializeField] private Rigidbody rb;
        [SerializeField] private float forceMult = 100;

        public Vector2 directionalInput = Vector2.zero;

        #endregion

        #region lifecycle

        private void Awake()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            var delta = new Vector3(directionalInput.y, 0, directionalInput.x);
            rb.AddForce(forceMult * delta, ForceMode.Force);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.TryGetComponent<Attachable>(out var att))
                return;

            att.Attach(transform);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.TryGetComponent<Attachable>(out var att))
                return;

            att.Detach();
        }

        #endregion
    }
}