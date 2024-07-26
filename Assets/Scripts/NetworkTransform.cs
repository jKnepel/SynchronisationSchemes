using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Networking;
using jKnepel.SimpleUnityNetworking.Serialising;
using System;
using System.Collections;
using System.Collections.Generic;
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
            public uint Tick;
            public DateTime Timestamp;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public ETransformSnapshot(uint senderID, uint tick, DateTime timestamp, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                SenderID = senderID;
                Tick = tick;
                Timestamp = timestamp;
                Position = position;
                Rotation = Quaternion.Normalize(rotation);
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
        
        [SerializeField] private bool _useInterpolation = true;
        [SerializeField] private float _interpolationInterval = .05f;
        [SerializeField] private float _moveMult = .30f;
        
        // TODO : add component type configuration (Rigidbody, CharacterController)
        // TODO : add hermite interpolation for rigibodies
        
        private NetworkObject _networkObject;
        
        private string _transformNetworkID;
        private INetworkManager _syncNetworkManager;

        private readonly List<ETransformSnapshot> _receivedSnapshots = new();
        
        #endregion
        
        #region lifecycle

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            _networkObject.OnNetworkIDUpdated += NetworkIDUpdated;
            _networkObject.OnSyncNetworkManagerUpdated += NetworkIDUpdated;
            NetworkIDUpdated();
        }

        private void Update()
        {
            if (_networkObject.ShouldSynchronise)
                return;
                
            if (!_useInterpolation || _receivedSnapshots.Count < 2) return;

            ETransformSnapshot a = default, b = default;
            var renderingTime = DateTime.Now.AddSeconds(-Mathf.Abs(_interpolationInterval));

            if (_receivedSnapshots.Count == 2)
            {
                a = _receivedSnapshots[0];
                b = _receivedSnapshots[1];
            }
            else
            {
                for (var i = 2; i <= _receivedSnapshots.Count; i++)
                {
                    var snapshot = _receivedSnapshots[^i];
                    if (snapshot.Timestamp > renderingTime)
                        continue;
                    b = snapshot;
                    a = _receivedSnapshots[^(i - 1)];
                    break;
                }

                if (a.Tick == 0 || b.Tick == 0)
                {
                    // TODO : extrapolate?
                    a = _receivedSnapshots[^1];
                    b = _receivedSnapshots[^2];
                    LinearInterpolateSnapshots(a, b, 1);
                }
            }

            var t = (float)((renderingTime - b.Timestamp) / (a.Timestamp - b.Timestamp));
            t = Mathf.Clamp01(t);
            LinearInterpolateSnapshots(a, b, t);
            
            // TODO : add smoothing without interpolation
        }
        
        #endregion
        
        #region private methods

        private void LinearInterpolateSnapshots(ETransformSnapshot a, ETransformSnapshot b, float t)
        {
            // TODO : add teleport/snap threshold delta
            // TODO : add rate multiplier depending on length of interpolation queue
            if (_synchronisePosition)
            {
                var targetPos = Vector3.Lerp(a.Position, b.Position, t);
                transform.position = Vector3.MoveTowards(transform.position, targetPos, Time.deltaTime * _moveMult);
            }

            if (_synchroniseRotation)
            {
                var targetRot = Quaternion.Lerp(a.Rotation, b.Rotation, t);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, Time.deltaTime * _moveMult);
            }

            if (_synchroniseScale)
            {
                var targetScale = Vector3.Lerp(a.Scale, b.Scale, t);
                transform.localScale = Vector3.MoveTowards(transform.localScale, targetScale, Time.deltaTime * _moveMult);
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
            if (_synchronisePosition)
                pos = trf.position;
            if (_synchroniseRotation)
                rot = trf.rotation;
            if (_synchroniseScale)
                scale = trf.localScale;

            if (pos is not null || rot is not null || scale is not null)
            {
                ETransformPacket packet = new(pos, rot, scale);
                Writer writer = new(_syncNetworkManager.SerialiserSettings);
                ETransformPacket.Write(writer, packet);
                if (_syncNetworkManager.IsServer)
                    _syncNetworkManager.Server.SendByteDataToAll(_transformNetworkID, writer.GetBuffer(), _synchroniseChannel);
                else
                    _syncNetworkManager.Client.SendByteDataToAll(_transformNetworkID, writer.GetBuffer(), _synchroniseChannel);
            }
        }

        private void TransformUpdateReceived(ByteData data)
        {
            if (_syncNetworkManager is null || !_networkObject.IsActiveMode) return;
            
            Reader reader = new(data.Data, _syncNetworkManager.SerialiserSettings);

            ETransformPacket packet;
            
            try
            {
                packet = ETransformPacket.Read(reader);
            }
            catch (IndexOutOfRangeException) { return; }

            if (_useInterpolation)
            {
                var trf = transform;
                var snapshot = new ETransformSnapshot(data.SenderID, data.Tick, data.Timestamp, 
                    packet.Position ?? trf.position, 
                    packet.Rotation ?? trf.rotation, 
                    packet.Scale ?? trf.localScale);
                _receivedSnapshots.Add(snapshot);
            }
            else
            {
                var trf = transform;
                if (packet.Position is not null)
                    trf.position = (Vector3)packet.Position;
                if (packet.Rotation is not null)
                    trf.rotation = (Quaternion)packet.Rotation;
                if (packet.Scale is not null)
                    trf.localScale = (Vector3)packet.Scale;
            }
        }
        
        #endregion
    }
}
