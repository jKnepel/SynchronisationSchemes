using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Serialising;
using System;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [AddComponentMenu("SimpleUnityNetworking/Component/Network Transform")]
    public class NetworkTransform : MonoBehaviour
    {
        [SerializeField] private bool _synchronisePosition = true;
        [SerializeField] private bool _synchroniseRotation = true;
        [SerializeField] private bool _synchroniseScale = true;
        
        private NetworkObject _networkObject;
        private Vector3 _position;
        private Quaternion _rotation;
        private Vector3 _scale;

        private string _transformNetworkID;
        private INetworkManager _syncNetworkManager;

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            _networkObject.OnNetworkIDUpdated += NetworkIDUpdated;
            _networkObject.OnSyncNetworkManagerUpdated += NetworkIDUpdated;
            _transformNetworkID = $"{_networkObject.NetworkID}#Transform";
            _syncNetworkManager = _networkObject.SyncNetworkManager;
            _syncNetworkManager?.Client.RegisterByteData(_transformNetworkID, TransformUpdateReceived);
            
            _position = transform.position;
            _rotation = transform.rotation;
            _scale = transform.localScale;
        }

        private void Update()
        {
            if (!transform.hasChanged) return;
            transform.hasChanged = false;

            if (_syncNetworkManager is null || !_networkObject.ShouldSynchronise) return;

            var update = new NetworkUpdate(_transformNetworkID);
            if (_synchronisePosition && _position != transform.position)
            {
                var position = transform.position;
                update.Add(0, position);
                _position = position;
            }
            if (_synchroniseRotation && _rotation != transform.rotation)
            {
                var rotation = transform.rotation;
                update.Add(1, rotation);
                _rotation = rotation;
            }
            if (_synchroniseScale && _scale != transform.localScale)
            {
                var scale = transform.localScale;
                update.Add(2, scale);
                _scale = scale;
            }

            if (update.UpdateValues.Count > 0)
                _syncNetworkManager.AddNetworkUpdate(update);
        }

        private void NetworkIDUpdated()
        {
            _syncNetworkManager?.Client.UnregisterByteData(_transformNetworkID, TransformUpdateReceived);
            _transformNetworkID = $"{_networkObject.NetworkID}#Transform";
            _syncNetworkManager = _networkObject.SyncNetworkManager;
            _syncNetworkManager?.Client.RegisterByteData(_transformNetworkID, TransformUpdateReceived);
        }

        private void TransformUpdateReceived(uint sender, byte[] data)
        {
            if (_syncNetworkManager is null) return;
            
            var serialiserSettings = _syncNetworkManager.SerialiserSettings;
            Reader reader = new(data, serialiserSettings);

            try
            {
                while (reader.Remaining > 0)
                {
                    var flag = reader.ReadByte();
                    switch (flag)
                    {
                        case 0:
                            _position = transform.position = reader.ReadVector3();
                            break;
                        case 1:
                            _rotation = transform.rotation = reader.ReadQuaternion();
                            break;
                        case 2:
                            _scale = transform.localScale = reader.ReadVector3();
                            break;
                        default: return; // TODO : handle error
                    }
                }

                transform.hasChanged = false;
            }
            catch (IndexOutOfRangeException) 
            { 
                // TODO : handle error
            }
        }
    }
}
