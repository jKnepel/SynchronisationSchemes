using jKnepel.ProteusNet.Networking;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    [RequireComponent(typeof(NetworkObjectAuthority))]
    public class PlayerAuthoritative : Player
    {
        private NetworkObjectAuthority _authoritative;

        #region lifecycle

        protected override void Awake()
        {
            base.Awake();
            _authoritative = GetComponent<NetworkObjectAuthority>();
        }
        
        private void Start()
        {
            _authoritative.SyncNetworkManager.Client.OnLocalStateUpdated += LocalClientStateUpdated;
            return;

            void LocalClientStateUpdated(ELocalClientConnectionState state)
            {
                if (state == ELocalClientConnectionState.Authenticated && !_authoritative.SyncNetworkManager.IsServer)
                {
                    _authoritative.RequestOwnership();
                }
            }
        }
        
        private void FixedUpdate()
        {
            if (!_authoritative.IsOwner) return;
            var delta = new Vector3(directionalInput.y, 0, directionalInput.x);
            _rb.AddForce(forceMult * delta, ForceMode.Force);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_authoritative.IsOwner
                || !other.TryGetComponent<Attachable>(out var att)
                || !other.TryGetComponent<NetworkObjectAuthority>(out var auth))
                return;

            auth.RequestOwnership(success => att.Attach(success, transform));
        }

        private void OnTriggerExit(Collider other)
        {
            if (!_authoritative.IsOwner
                || !other.TryGetComponent<Attachable>(out var att)
                || !other.TryGetComponent<NetworkObjectAuthority>(out var auth))
                return;

            att.Detach();
            auth.ReleaseOwnership();
        }

        #endregion
    }
}