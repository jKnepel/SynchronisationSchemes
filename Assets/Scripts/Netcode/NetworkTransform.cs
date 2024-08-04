using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Networking;
using jKnepel.SimpleUnityNetworking.Serialising;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [AddComponentMenu("SimpleUnityNetworking/Component/Network Transform")]
    public class NetworkTransform : MonoBehaviour
    {
        public enum ComponentType
        {
            Transform,
            Rigidbody
        }
        
        #region fields and properties
        
        [SerializeField] private ENetworkChannel synchroniseChannel = ENetworkChannel.UnreliableOrdered;
        
        [SerializeField] private ComponentType type;
        public ComponentType Type
        {
            get => type;
            set
            {
                if (type == value) return;
                type = value;
                // TODO : synchronise type across network
            }
        }
        
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
        private Rigidbody _rigidbody;
        
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
            SetupTransform();
        }
        
        private void Reset()
        {
            if (transform.TryGetComponent(out _rigidbody))
                type = ComponentType.Rigidbody;
            else
                type = ComponentType.Transform;
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

            if (Type == ComponentType.Rigidbody)
            {
                _rigidbody.velocity = target.LinearVelocity;
                _rigidbody.angularVelocity = target.AngularVelocity;
            }
        }

        #endregion
        
        #region private methods

        private void SetupTransform()
        {
            switch (Type)
            {
                case ComponentType.Transform:
                    _rigidbody = null;
                    break;
                case ComponentType.Rigidbody:
                    if (!transform.TryGetComponent(out _rigidbody))
                        _rigidbody = transform.AddComponent<Rigidbody>();
                    break;
            }
        }

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
            Vector3? linearVelocity = null;
            Vector3? angularVelocity = null;

            var trf = transform;
            if (synchronisePosition)
                pos = trf.position;
            if (synchroniseRotation)
                rot = trf.rotation;
            if (synchroniseScale)
                scale = trf.localScale;
            if (Type == ComponentType.Rigidbody)
            {
                linearVelocity = _rigidbody.velocity;
                angularVelocity = _rigidbody.angularVelocity;
            }

            if (synchronisePosition || synchroniseRotation || synchroniseScale)
            {
                ETransformPacket packet = new()
                {
                    Position = pos,
                    Rotation = rot,
                    Scale = scale,
                    LinearVelocity = linearVelocity,
                    AngularVelocity = angularVelocity
                };
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
            _receivedSnapshots.Add(new()
            {
                SenderID = data.SenderID,
                Tick = data.Tick,
                Timestamp = data.Timestamp,
                Position = packet.Position ?? trf.position,
                Rotation = packet.Rotation ?? trf.rotation,
                Scale = packet.Scale ?? trf.localScale,
                LinearVelocity = packet.LinearVelocity ?? Vector3.zero,
                AngularVelocity = packet.AngularVelocity ?? Vector3.zero
            });
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
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
        }

        private class ETransformPacket
        {
            private enum ETransformPacketFlag : byte
            {
                Position,
                Rotation,
                Scale,
                LinearVelocity,
                AngularVelocity
            }
            
            public Vector3? Position;
            public Quaternion? Rotation;
            public Vector3? Scale;
            public Vector3? LinearVelocity;
            public Vector3? AngularVelocity;
            
            // TODO : ignore resting velocities

            public static ETransformPacket Read(Reader reader)
            {
                var packet = new ETransformPacket();
                while (reader.Remaining > 0)
                {
                    var flag = (ETransformPacketFlag)reader.ReadByte();
                    switch (flag)
                    {
                        case ETransformPacketFlag.Position:
                            packet.Position = reader.ReadVector3();
                            break;
                        case ETransformPacketFlag.Rotation:
                            packet.Rotation = reader.ReadQuaternion();
                            break;
                        case ETransformPacketFlag.Scale:
                            packet.Scale = reader.ReadVector3();
                            break;
                        case ETransformPacketFlag.LinearVelocity:
                            packet.LinearVelocity = reader.ReadVector3();
                            break;
                        case ETransformPacketFlag.AngularVelocity:
                            packet.AngularVelocity = reader.ReadVector3();
                            break;
                    }
                }

                return packet;
            }

            public static void Write(Writer writer, ETransformPacket packet)
            {
                if (packet.Position is not null)
                {
                    writer.WriteByte((byte)ETransformPacketFlag.Position);
                    writer.WriteVector3((Vector3)packet.Position);
                }
                
                if (packet.Rotation is not null)
                {
                    writer.WriteByte((byte)ETransformPacketFlag.Rotation);
                    writer.WriteQuaternion((Quaternion)packet.Rotation);
                }
                
                if (packet.Scale is not null)
                {
                    writer.WriteByte((byte)ETransformPacketFlag.Scale);
                    writer.WriteVector3((Vector3)packet.Scale);
                }
                
                if (packet.LinearVelocity is not null)
                {
                    writer.WriteByte((byte)ETransformPacketFlag.LinearVelocity);
                    writer.WriteVector3((Vector3)packet.LinearVelocity);
                }
                
                if (packet.AngularVelocity is not null)
                {
                    writer.WriteByte((byte)ETransformPacketFlag.AngularVelocity);
                    writer.WriteVector3((Vector3)packet.AngularVelocity);
                }
            }
        }

        private class TargetTransform
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
        }
    }
}
