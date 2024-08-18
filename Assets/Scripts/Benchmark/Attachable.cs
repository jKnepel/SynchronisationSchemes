using System.Linq;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    [RequireComponent(typeof(Rigidbody), typeof(NetworkObjectAuthority))]
    public class Attachable : MonoBehaviour
    {
        #region attributes

        [SerializeField] private NetworkObjectAuthority networkObject;
        [SerializeField] private Rigidbody rb;
        [SerializeField] private float gravitationalPull = 3000;
        [SerializeField] private bool isAttached;
        
        private Transform _attachedTo;
        private float _maxDistance;

        public bool IsAttached
        {
            get => isAttached; 
            private set => isAttached = value;
        }

        #endregion

        #region lifecycle

        private void Awake()
        {
            if (networkObject == null)
                networkObject = GetComponent<NetworkObjectAuthority>();
            if (rb == null)
                rb = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (!IsAttached)
                return;

            var distance = Vector3.Distance(transform.position, _attachedTo.position);
            var strength = Map(distance, _maxDistance, 0, 0, gravitationalPull);
            rb.AddForce(strength * Time.fixedDeltaTime * (_attachedTo.position - transform.position));
        }

        #endregion

        #region public methods

        public void Attach(Transform trf)
        {
            if (IsAttached)
                return;

            networkObject.RequestOwnership(success => Attach(success, trf));
        }

        private void Attach(bool success, Transform trf)
        {
            if (!success || IsAttached)
                return;
            
            IsAttached = true;
            _attachedTo = trf;
            _maxDistance = trf.GetComponents<Collider>().First(x => x.isTrigger).bounds.size.x;
        }

        public void Detach()
        {
            if (!IsAttached)
                return;

            networkObject.ReleaseOwnership();
            IsAttached = false;
            _attachedTo = null;
            _maxDistance = 0;
        }
        
        // TODO : add authority grab on collision

        #endregion

        #region private methods

        private static float Map(float value, float from1, float from2, float to1, float to2)
        {
            return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
        }

        #endregion
    }
}