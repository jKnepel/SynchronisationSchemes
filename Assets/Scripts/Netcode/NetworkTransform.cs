using jKnepel.ProteusNet.Managing;
using jKnepel.ProteusNet.Networking;
using jKnepel.ProteusNet.Serializing;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [AddComponentMenu("ProteusNet/Component/Network Transform")]
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
                    target = LinearExtrapolateSnapshots(_receivedSnapshots[^2], snapshot, DateTime.Now);
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
                if (teleportPosition && Vector3.Distance(trf.localPosition, target.Position) >= positionTeleportThreshold)
                    trf.localPosition = target.Position;
                else
                    trf.localPosition = Vector3.MoveTowards(trf.localPosition, target.Position, Time.deltaTime * moveMult);
            }

            if (synchroniseRotation)
            {
                if (teleportRotation && Quaternion.Angle(trf.localRotation, target.Rotation) >= rotationTeleportThreshold)
                    trf.localRotation = target.Rotation;
                else
                    trf.localRotation = Quaternion.RotateTowards(trf.localRotation, target.Rotation, Time.deltaTime * moveMult);
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
                pos = trf.localPosition;
            if (synchroniseRotation)
                rot = trf.localRotation;
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
                Writer writer = new(_syncNetworkManager.SerializerSettings);
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
            
            Reader reader = new(data.Data, _syncNetworkManager.SerializerSettings);

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
                Position = packet.Position ?? trf.localPosition,
                Rotation = packet.Rotation ?? trf.localRotation,
                Scale = packet.Scale ?? trf.localScale,
                LinearVelocity = packet.LinearVelocity ?? Vector3.zero,
                AngularVelocity = packet.AngularVelocity ?? Vector3.zero
            });
        }

        private TargetTransform InterpolateTransform()
        {
            var renderingTime = DateTime.Now.AddSeconds(-interpolationInterval);
            // TODO : add rate multiplier depending on length of interpolation queue

            if (_receivedSnapshots[^1].Timestamp < renderingTime)
            {
                var snapshot = _receivedSnapshots[^1];
                if (useExtrapolation && (renderingTime - snapshot.Timestamp).TotalSeconds <= extrapolationInterval)
                {
                    return LinearExtrapolateSnapshots(_receivedSnapshots[^2], snapshot, renderingTime);
                }
                
                return new()
                {
                    Position = snapshot.Position,
                    Rotation = snapshot.Rotation,
                    Scale = snapshot.Scale,
                    LinearVelocity = snapshot.LinearVelocity,
                    AngularVelocity = snapshot.AngularVelocity
                };
            }

            TargetTransform target = default;
            for (var i = 2; i <= _receivedSnapshots.Count; i++)
            {
                var snapshot = _receivedSnapshots[^i];
                if (snapshot.Timestamp > renderingTime) continue;
                target = LinearInterpolateSnapshots(snapshot, _receivedSnapshots[^(i - 1)], renderingTime);
                break;
            }
            return target;
        }
        
        private static TargetTransform LinearInterpolateSnapshots(ETransformSnapshot left, ETransformSnapshot right, DateTime time)
        {
            var t = (float)((time - left.Timestamp) / (right.Timestamp - left.Timestamp));
            t = Mathf.Clamp01(t);
            
            return new()
            {
                Position = Vector3.Lerp(left.Position, right.Position, t),
                Rotation = Quaternion.Lerp(left.Rotation, right.Rotation, t),
                Scale = Vector3.Lerp(left.Scale, right.Scale, t),
                LinearVelocity = Vector3.Lerp(left.LinearVelocity, right.LinearVelocity, t),
                AngularVelocity = Vector3.Lerp(left.AngularVelocity, right.AngularVelocity, t)
            };
        }

        private static TargetTransform LinearExtrapolateSnapshots(ETransformSnapshot left, ETransformSnapshot right, DateTime time)
        {
            var deltaTime = (float)(right.Timestamp - left.Timestamp).TotalSeconds;

            if (deltaTime == 0)
            {   // TODO : temporary fix for doubly received packets on focus
                return new()
                {
                    Position = right.Position,
                    Rotation = right.Rotation,
                    Scale = right.Scale,
                    LinearVelocity = right.LinearVelocity,
                    AngularVelocity = right.AngularVelocity
                };
            }
            
            var extrapolateTime = (float)(time - right.Timestamp).TotalSeconds;
            
            var deltaRot = right.Rotation * Quaternion.Inverse(left.Rotation);
            var targetRot = right.Rotation * Quaternion.Slerp(Quaternion.identity, deltaRot, extrapolateTime / deltaTime);
            
            var targetPos = LinearExtrapolateVector3(left.Position, right.Position, deltaTime, extrapolateTime);
            var targetScale = LinearExtrapolateVector3(left.Scale, right.Scale, deltaTime, extrapolateTime);
            var targetLinVel = LinearExtrapolateVector3(left.LinearVelocity, right.LinearVelocity, deltaTime, extrapolateTime);
            var targetAngVel = LinearExtrapolateVector3(left.AngularVelocity, right.AngularVelocity, deltaTime, extrapolateTime);
            
            return new()
            {
                Position = targetPos,
                Rotation = targetRot,
                Scale = targetScale,
                LinearVelocity = targetLinVel,
                AngularVelocity = targetAngVel
            };
        }

        private static Vector3 LinearExtrapolateVector3(Vector3 left, Vector3 right, float deltaTime, float extrapolateTime)
        {
            var deltaVector = (right - left) / deltaTime;
            var targetVector = right + deltaVector * extrapolateTime;
            return targetVector;
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
                
                if (packet.LinearVelocity is not null && ((Vector3)packet.LinearVelocity).magnitude > 0)
                {
                    writer.WriteByte((byte)ETransformPacketFlag.LinearVelocity);
                    writer.WriteVector3((Vector3)packet.LinearVelocity);
                }
                
                if (packet.AngularVelocity is not null && ((Vector3)packet.AngularVelocity).magnitude > 0)
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
