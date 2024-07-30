using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Networking;
using jKnepel.SimpleUnityNetworking.Serialising;
using System;
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
        #region fields and properties
        
        [SerializeField] private ENetworkChannel synchroniseChannel = ENetworkChannel.UnreliableOrdered;
        
        [SerializeField] private bool synchronisePosition = true;
        [SerializeField] private bool synchroniseRotation = true;
        [SerializeField] private bool synchroniseScale = true;
        
        [SerializeField] private bool teleportPosition = true;
        [SerializeField] private float positionTeleportThreshold = 1;
        [SerializeField] private bool teleportRotation = true;
        [SerializeField] private float rotationTeleportThreshold = 90;
        [SerializeField] private bool teleportScale = true;
        [SerializeField] private float scaleTeleportThreshold = 1;
        
        [SerializeField] private bool useInterpolation = true;
        [SerializeField] private float interpolationInterval = .05f;

        [SerializeField] private bool useExtrapolation = true;
        [SerializeField] private float extrapolationInterval = .2f;
        
        private float moveMult = 30; // TODO : calculate this
        // TODO : add component type configuration (Rigidbody, CharacterController)
        // TODO : add hermite interpolation for rigibodies
        
        private NetworkObject _networkObject;
        
        private string _transformNetworkID;
        private INetworkManager _syncNetworkManager;

        private readonly List<ETransformSnapshot> _receivedSnapshots = new();
        // TODO : cleanup unused snapshots
        
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
            if (_networkObject.ShouldSynchronise || _receivedSnapshots.Count == 0)
                return;
                
            TargetTransform target;
            if (useInterpolation && _receivedSnapshots.Count >= 2)
            {
                target = InterpolateTransform();
            }
            else
            {
                var snapshot = _receivedSnapshots[^1];
                if (useExtrapolation && _receivedSnapshots.Count >= 2 && (DateTime.Now - snapshot.Timestamp).TotalSeconds <= extrapolationInterval)
                {
                    target = LinearExtrapolateSnapshots(snapshot, _receivedSnapshots[^2], DateTime.Now);
                }
                else
                {
                    target = new()
                    {
                        Position = snapshot.Position,
                        Rotation = snapshot.Rotation,
                        Scale = snapshot.Scale
                    };
                }
            }
            
            // TODO : add snap
            var trf = transform;
            if (synchronisePosition)
            {
                if (teleportPosition && Vector3.Distance(trf.position, target.Position) >= positionTeleportThreshold)
                    trf.position = target.Position;
                else
                    trf.position = Vector3.MoveTowards(trf.position, target.Position, Time.deltaTime * moveMult);
            }

            if (synchroniseRotation)
            {
                if (teleportRotation && Quaternion.Angle(trf.rotation, target.Rotation) >= rotationTeleportThreshold)
                    trf.rotation = target.Rotation;
                else
                    trf.rotation = Quaternion.RotateTowards(trf.rotation, target.Rotation, Time.deltaTime * moveMult);
            }

            if (synchroniseScale)
            {
                if (teleportScale && Vector3.Distance(trf.localScale, target.Scale) >= scaleTeleportThreshold)
                    trf.localScale = target.Scale;
                else
                    trf.localScale = Vector3.MoveTowards(trf.localScale, target.Scale, Time.deltaTime * moveMult);
            }
        }
        
        #endregion
        
        #region private methods

        private void NetworkIDUpdated()
        {
            if (_syncNetworkManager is not null)
            {
                _syncNetworkManager.Client.UnregisterByteData(_transformNetworkID, TransformUpdateReceived);
                _syncNetworkManager.OnTickStarted -= SendTransformUpdates;
            }
            _transformNetworkID = $"{_networkObject.NetworkID}#Transform";
            _syncNetworkManager = _networkObject.SyncNetworkManager;
            if (_syncNetworkManager is not null)
            {
                _syncNetworkManager.Client.RegisterByteData(_transformNetworkID, TransformUpdateReceived);
                _syncNetworkManager.OnTickStarted += SendTransformUpdates;
            }
        }
        
        private void SendTransformUpdates(uint _)
        {
            if (_syncNetworkManager is null || !_networkObject.ShouldSynchronise) return;

            Vector3? pos = null;
            Quaternion? rot = null;
            Vector3? scale = null;

            var trf = transform;
            if (synchronisePosition)
                pos = trf.position;
            if (synchroniseRotation)
                rot = trf.rotation;
            if (synchroniseScale)
                scale = trf.localScale;

            if (pos is not null || rot is not null || scale is not null)
            {
                ETransformPacket packet = new(pos, rot, scale);
                Writer writer = new(_syncNetworkManager.SerialiserSettings);
                ETransformPacket.Write(writer, packet);
                if (_syncNetworkManager.IsServer)
                    _syncNetworkManager.Server.SendByteDataToAll(_transformNetworkID, writer.GetBuffer(), synchroniseChannel);
                else
                    _syncNetworkManager.Client.SendByteDataToAll(_transformNetworkID, writer.GetBuffer(), synchroniseChannel);
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

            var trf = transform;
            var snapshot = new ETransformSnapshot(data.SenderID, data.Tick, data.Timestamp, 
                packet.Position ?? trf.position, 
                packet.Rotation ?? trf.rotation, 
                packet.Scale ?? trf.localScale);
            _receivedSnapshots.Add(snapshot);
        }

        private TargetTransform InterpolateTransform()
        {
            var renderingTime = DateTime.Now.AddSeconds(-Mathf.Abs(interpolationInterval));
            // TODO : add rate multiplier depending on length of interpolation queue

            if (_receivedSnapshots[^1].Timestamp < renderingTime)
            {
                var snapshot = _receivedSnapshots[^1];
                if (useExtrapolation && (renderingTime - snapshot.Timestamp).TotalSeconds <= extrapolationInterval)
                {
                    return LinearExtrapolateSnapshots(snapshot, _receivedSnapshots[^2], renderingTime);
                }
                
                return new()
                {
                    Position = snapshot.Position,
                    Rotation = snapshot.Rotation,
                    Scale = snapshot.Scale
                };
            }

            TargetTransform target = default;
            for (var i = 2; i <= _receivedSnapshots.Count; i++)
            {
                var snapshot = _receivedSnapshots[^i];
                if (snapshot.Timestamp > renderingTime) continue;
                target = LinearInterpolateSnapshots(_receivedSnapshots[^(i - 1)], snapshot, renderingTime);
                break;
            }
            return target;
        }
        
        private static TargetTransform LinearInterpolateSnapshots(ETransformSnapshot a, ETransformSnapshot b, DateTime time)
        {
            var t = (float)((time - b.Timestamp) / (a.Timestamp - b.Timestamp));
            t = Mathf.Clamp01(t);
            
            return new()
            {
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Rotation = Quaternion.Lerp(a.Rotation, b.Rotation, t),
                Scale = Vector3.Lerp(a.Scale, b.Scale, t)
            };
        }

        private static TargetTransform LinearExtrapolateSnapshots(ETransformSnapshot a, ETransformSnapshot b, DateTime time)
        {
            var deltaTime = (float)(a.Timestamp - b.Timestamp).TotalSeconds;
            var deltaPos = (a.Position - b.Position) / deltaTime;
            var deltaRot = a.Rotation * Quaternion.Inverse(b.Rotation);
            var deltaScale = (a.Scale - b.Scale) / deltaTime;
                    
            var extrapolateTime = (float)(time - a.Timestamp).TotalSeconds;
            var targetPos = IsVector3NaN(deltaPos)
                ? a.Position
                : a.Position + deltaPos * extrapolateTime;
            var targetRot = a.Rotation * Quaternion.Slerp(Quaternion.identity, deltaRot, extrapolateTime / deltaTime);
            var targetScale = IsVector3NaN(deltaScale) 
                ? a.Scale
                : a.Scale + deltaScale * extrapolateTime;
                    
            return new()
            {
                Position = targetPos,
                Rotation = targetRot,
                Scale = targetScale
            };
        }

        private static bool IsVector3NaN(Vector3 vector)
        {
            return float.IsNaN(vector.x) || float.IsNaN(vector.y) || float.IsNaN(vector.z);
        }
        
        #endregion
        
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

        private class TargetTransform
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }
    }
}
