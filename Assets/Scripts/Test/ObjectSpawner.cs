using System;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes
{
    public class ObjectSpawner : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform objectParent;
        [SerializeField] private Transform objectPrefab;
        
        [Header("Values")]
        [SerializeField] private int numberOfObjects = 50;
        [SerializeField] private float spawnDistance = 2.0f;
        
        private void Start()
        {
            var numberOfColumns = (int)Math.Ceiling(Mathf.Sqrt(numberOfObjects));
            var numberOfRows = (int)Math.Ceiling((float)numberOfObjects / numberOfColumns);
            var remainder = numberOfObjects % numberOfRows;
            var startX = -((float)(numberOfColumns - 1) / 2 * spawnDistance);
            var startZ = -((float)(numberOfRows - 1) / 2 * spawnDistance);
            
            for (var i = 0; i < numberOfColumns; i++)
            {
                for (var j = 0; j < numberOfRows; j++)
                {
                    if (remainder > 0 && i == numberOfColumns - 1 && j >= remainder)
                        return;

                    var x = startX + i * spawnDistance;
                    var z = startZ + j * spawnDistance;
                    Vector3 position = new(x, objectPrefab.transform.position.y, z);
                    Instantiate(objectPrefab, position, objectPrefab.rotation, objectParent);
                }
            }
        }
    }
}
