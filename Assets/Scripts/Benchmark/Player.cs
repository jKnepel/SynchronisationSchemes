using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    [RequireComponent(typeof(Rigidbody))] 
    public class Player : MonoBehaviour
    {
        [SerializeField] protected float forceMult = 100;
        [HideInInspector] public Vector2 directionalInput = Vector2.zero;
        
        protected Rigidbody _rb; 

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody>();
        }
    }
}
