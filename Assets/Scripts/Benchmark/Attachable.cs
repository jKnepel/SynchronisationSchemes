using System.Linq;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    [RequireComponent(typeof(Rigidbody))]
    public class Attachable : MonoBehaviour
    {
        #region attributes

        [SerializeField] private Rigidbody rb;
        [SerializeField] private float gravitationalPull;
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

        public void Attach(bool success, Transform trf)
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

            IsAttached = false;
            _attachedTo = null;
            _maxDistance = 0;
        }
        
        #endregion

        #region private methods

        private static float Map(float value, float from1, float from2, float to1, float to2)
        {
            return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
        }

        #endregion
    }
}