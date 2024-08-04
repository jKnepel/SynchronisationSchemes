using jKnepel.ProteusNet.Managing;
using jKnepel.ProteusNet.Networking;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes
{
    [RequireComponent(typeof(Rigidbody))]
    public class AttachablePlayer : MonoBehaviour
    {
        #region attributes

        [SerializeField] private MonoNetworkManager networkManager;
        [SerializeField] private Rigidbody rb;

        [SerializeField] private float forceMult = 100;

        private DefaultInputActions _input;

        #endregion

        #region lifecycle

        private void Awake()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody>();

            _input = new();
            networkManager.Client.OnLocalStateUpdated += LocalStateUpdated;
        }

        private void FixedUpdate()
        {
            var dir = _input.gameplay.directional.ReadValue<Vector2>();
            var delta = new Vector3(dir.x, 0, dir.y);
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
        
        #region private methods

        private void LocalStateUpdated(ELocalClientConnectionState state)
        {
            switch (state)
            {
                case ELocalClientConnectionState.Authenticated:
                    _input.Enable();
                    break;
                case ELocalClientConnectionState.Stopping:
                    _input.Disable();
                    break;
            }
        }
        
        #endregion
    }
}