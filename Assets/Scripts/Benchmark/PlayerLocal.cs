using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    public class PlayerLocal : Player
    {
        #region lifecycle
        
        private void FixedUpdate()
        {
            var delta = new Vector3(directionalInput.y, 0, directionalInput.x);
            _rb.AddForce(forceMult * delta, ForceMode.Force);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.TryGetComponent<Attachable>(out var att))
                return;

            att.Attach(true, transform);
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