using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    public class ObjectSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform objectParent;
        [SerializeField] private Transform objectPrefab;
        
        [Header("Values")]
        public int numberOfObjects = 50;
        public float spawnDistance = 2.0f;

        private GameObject[] _objects;

        public void SpawnObjects()
        {
            _objects = new GameObject[numberOfObjects];
            var numberOfColumns = Mathf.CeilToInt(Mathf.Sqrt(numberOfObjects));
            var offset = (numberOfColumns - 1) * spawnDistance / 2;
            
            var count = 0;
            for (var i = 0; i < numberOfColumns; i++)
            {
                for (var j = 0; j < numberOfColumns; j++)
                {
                    if (count >= numberOfObjects) return;

                    Vector3 position = new(
                        i * spawnDistance - offset, 
                        objectPrefab.transform.position.y, 
                        j * spawnDistance - offset
                    );
                    var go = Instantiate(objectPrefab, position, objectPrefab.rotation, objectParent).gameObject;
                    _objects[count++] = go;
                }
            }
        }
    }
}
