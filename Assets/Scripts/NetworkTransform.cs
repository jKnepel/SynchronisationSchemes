using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Networking;
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
        private enum ETransformFlag : byte
        {
            Position,
            Rotation,
            Scale
        }
        
        [SerializeField] private ENetworkChannel _synchroniseChannel = ENetworkChannel.ReliableOrdered;
        [SerializeField] private bool _synchronisePosition = true;
        [SerializeField] private bool _synchroniseRotation = true;
        [SerializeField] private bool _synchroniseScale = true;
        [SerializeField] private bool _preventDisallowedChanges = false;
        
        // TODO : add component type configuration (Rigidbody, CharacterController)
        // TODO : add smoothing/snapping
        
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
            if (_syncNetworkManager is not null)
            {
                _syncNetworkManager.Client.RegisterByteData(_transformNetworkID, TransformUpdateReceived);
                _syncNetworkManager.OnTickStarted += CheckTransformUpdates;
            }
            
            _position = transform.position;
            _rotation = transform.rotation;
            _scale = transform.localScale;
        }

        private void Update()
        {
            if (!_preventDisallowedChanges || _networkObject.ShouldSynchronise) return;

            transform.position = _position;
            transform.rotation = _rotation;
            transform.localScale = _scale;
        }

        private void NetworkIDUpdated()
        {
            if (_syncNetworkManager is not null)
            {
                _syncNetworkManager.Client.UnregisterByteData(_transformNetworkID, TransformUpdateReceived);
                _syncNetworkManager.OnTickStarted -= CheckTransformUpdates;
            }
            _transformNetworkID = $"{_networkObject.NetworkID}#Transform";
            _syncNetworkManager = _networkObject.SyncNetworkManager;
            if (_syncNetworkManager is not null)
            {
                _syncNetworkManager.Client.RegisterByteData(_transformNetworkID, TransformUpdateReceived);
                _syncNetworkManager.OnTickStarted += CheckTransformUpdates;
            }
        }
        
        private void CheckTransformUpdates(uint _)
        {
            if (_syncNetworkManager is null || !_networkObject.ShouldSynchronise) return;
            Writer writer = new(_syncNetworkManager.SerialiserSettings);

            if (_synchronisePosition && _position != transform.position)
            {
                _position = transform.position;
                writer.WriteByte((byte)ETransformFlag.Position);
                writer.WriteVector3(_position);
            }
            if (_synchroniseRotation && _rotation != transform.rotation)
            {
                _rotation = transform.rotation;
                writer.WriteByte((byte)ETransformFlag.Rotation);
                writer.WriteQuaternion(_rotation);
            }
            if (_synchroniseScale && _scale != transform.localScale)
            {
                _scale = transform.localScale;
                writer.WriteByte((byte)ETransformFlag.Scale);
                writer.WriteVector3(_scale);
            }

            if (writer.Length == 0) return;

            _syncNetworkManager.Client.SendByteDataToAll(_transformNetworkID, writer.GetBuffer(), _synchroniseChannel);
        }

        private void TransformUpdateReceived(uint sender, byte[] data)
        {
            if (_syncNetworkManager is null) return;
            
            Reader reader = new(data, _syncNetworkManager.SerialiserSettings);

            try
            {
                while (reader.Remaining > 0)
                {
                    var flag = (ETransformFlag)reader.ReadByte();
                    switch (flag)
                    {
                        case ETransformFlag.Position:
                            _position = transform.position = reader.ReadVector3();
                            break;
                        case ETransformFlag.Rotation:
                            _rotation = transform.rotation = reader.ReadQuaternion();
                            break;
                        case ETransformFlag.Scale:
                            _scale = transform.localScale = reader.ReadVector3();
                            break;
                        default: return; // TODO : handle error
                    }
                }
            }
            catch (IndexOutOfRangeException) 
            { 
                // TODO : handle error
            }
        }
    }
}
