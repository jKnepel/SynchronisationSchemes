using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Networking;
using jKnepel.SimpleUnityNetworking.Serialising;
using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [AddComponentMenu("SimpleUnityNetworking/Component/Network Transform")]
    public class NetworkTransform : MonoBehaviour
    {
        private struct ETransformSnapshot
        {
            public uint SenderID;
            // TODO : add tick number
            public readonly Vector3? Position;
            public readonly Quaternion? Rotation;
            public readonly Vector3? Scale;

            public ETransformSnapshot(uint senderID, Vector3? position, Quaternion? rotation, Vector3? scale)
            {
                SenderID = senderID;
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }

        private struct ETransformPacket
        {
            public readonly Vector3? Position;
            public readonly Quaternion? Rotation;
            public readonly Vector3? Scale;

            public ETransformPacket(Vector3? position, Quaternion? rotation, Vector3? scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }

            public static ETransformPacket Read(Reader reader)
            {
                var hasPos = reader.ReadBoolean();
                Vector3? position = null;
                if (hasPos)
                    position = reader.ReadVector3();

                var hasRot = reader.ReadBoolean();
                Quaternion? rotation = null;
                if (hasRot)
                    rotation = reader.ReadQuaternion();

                var hasScale = reader.ReadBoolean();
                Vector3? scale = null;
                if (hasScale)
                    scale = reader.ReadVector3();

                return new(position, rotation, scale);
            }

            public static void Write(Writer writer, ETransformPacket packet)
            {
                var hasPos = packet.Position is not null;
                writer.WriteBoolean(hasPos);
                if (hasPos)
                    writer.WriteVector3((Vector3)packet.Position);
                
                var hasRot = packet.Rotation is not null;
                writer.WriteBoolean(hasRot);
                if (hasRot)
                    writer.WriteQuaternion((Quaternion)packet.Rotation);
                
                var hasScale = packet.Scale is not null;
                writer.WriteBoolean(hasScale);
                if (hasScale)
                    writer.WriteVector3((Vector3)packet.Scale);
            }
        }
        
        #region fields and properties
        
        [SerializeField] private ENetworkChannel _synchroniseChannel = ENetworkChannel.ReliableOrdered;
        
        [SerializeField] private bool _synchronisePosition = true;
        [SerializeField] private bool _synchroniseRotation = true;
        [SerializeField] private bool _synchroniseScale = true;
        
        [SerializeField] private bool _preventDisallowedChanges = false;
        
        [SerializeField] private bool _useInterpolation = true;
        [SerializeField] private float _interpolationInterval = .1f;
        
        // TODO : add component type configuration (Rigidbody, CharacterController)
        
        private NetworkObject _networkObject;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;
        private Vector3 _lastScale;
        
        private string _transformNetworkID;
        private INetworkManager _syncNetworkManager;

        private readonly ConcurrentQueue<ETransformSnapshot> _receivedSnapshots = new();
        
        #endregion
        
        #region lifecycle

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            _networkObject.OnNetworkIDUpdated += NetworkIDUpdated;
            _networkObject.OnSyncNetworkManagerUpdated += NetworkIDUpdated;
            NetworkIDUpdated();

            var trf = transform;
            _lastPosition = trf.position;
            _lastRotation = trf.rotation;
            _lastScale = trf.localScale;
        }

        private void Start()
        {
            StartCoroutine(InterpolationCoroutine());
        }

        private void Update()
        {
            if (!_preventDisallowedChanges || _networkObject.ShouldSynchronise) return;

            var trf = transform;
            trf.rotation = _lastRotation;
            trf.position = _lastPosition;
            trf.localScale = _lastScale;
        }
        
        #endregion
        
        #region private methods

        private IEnumerator InterpolationCoroutine()
        {
            while (_useInterpolation)
            {
                if (_receivedSnapshots.TryDequeue(out var snapshot))
                {
                    if (snapshot.Position is not null)
                        _lastPosition = transform.position = (Vector3)snapshot.Position;
                    if (snapshot.Rotation is not null)
                        _lastRotation = transform.rotation = (Quaternion)snapshot.Rotation;
                    if (snapshot.Scale is not null)
                        _lastScale = transform.localScale = (Vector3)snapshot.Scale;
                }
                yield return new WaitForSecondsRealtime(_interpolationInterval);
            }
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

            Vector3? pos = null;
            Quaternion? rot = null;
            Vector3? scale = null;

            var trf = transform;
            if (_synchronisePosition && _lastPosition != trf.position)
                pos = _lastPosition = trf.position;
            if (_synchroniseRotation && _lastRotation != trf.rotation)
                rot = _lastRotation = trf.rotation;
            if (_synchroniseScale && _lastScale != trf.localScale)
                scale = _lastScale = trf.localScale;

            if (pos is not null || rot is not null || scale is not null)
            {
                ETransformPacket packet = new(pos, rot, scale);
                Writer writer = new(_syncNetworkManager.SerialiserSettings);
                ETransformPacket.Write(writer, packet);
                _syncNetworkManager.Client.SendByteDataToAll(_transformNetworkID, writer.GetBuffer(), _synchroniseChannel);
            }
        }

        private void TransformUpdateReceived(uint sender, byte[] data)
        {
            if (_syncNetworkManager is null || !_networkObject.IsActiveMode) return;
            
            Reader reader = new(data, _syncNetworkManager.SerialiserSettings);

            ETransformPacket packet;
            
            try
            {
                packet = ETransformPacket.Read(reader);
            }
            catch (IndexOutOfRangeException) { return; }

            if (_useInterpolation)
            {
                var snapshot = new ETransformSnapshot(sender, packet.Position, packet.Rotation, packet.Scale);
                _receivedSnapshots.Enqueue(snapshot);
            }
            else
            {
                var trf = transform;
                if (packet.Position is not null)
                    _lastPosition = trf.position = (Vector3)packet.Position;
                if (packet.Rotation is not null)
                    _lastRotation = trf.rotation = (Quaternion)packet.Rotation;
                if (packet.Scale is not null)
                    _lastScale = trf.localScale = (Vector3)packet.Scale;
            }
        }
        
        #endregion
    }
}
