using jKnepel.ProteusNet.Networking;
using jKnepel.ProteusNet.Serializing;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerClientServer : Player
    {
        private NetworkObject _object;

        #region lifecycle

        protected override void Awake()
        {
            base.Awake();
            _object = GetComponent<NetworkObject>();
        }

        private void Start()
        {
            _object.SyncNetworkManager.Server.RegisterByteData("input", HandlePlayerInput);
        }

        private void FixedUpdate()
        {
            if (_object.ShouldSynchronise)
            {
                var delta = new Vector3(directionalInput.y, 0, directionalInput.x);
                _rb.AddForce(forceMult * delta, ForceMode.Force);
            }
            else
            {
                PlayerInput input = new()
                {
                    X = directionalInput.x,
                    Y = directionalInput.y
                };
                Writer writer = new();
                PlayerInput.Write(input, writer);
                _object.SyncNetworkManager.Client.SendByteDataToServer("input", writer.GetBuffer(), ENetworkChannel.UnreliableOrdered);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_object.ShouldSynchronise 
                || !other.TryGetComponent<Attachable>(out var att))
                return;

            att.Attach(true, transform);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!_object.ShouldSynchronise
                || !other.TryGetComponent<Attachable>(out var att))
                return;

            att.Detach();
        }

        private void HandlePlayerInput(ByteData data)
        {
            Reader reader = new(data.Data);
            var input = PlayerInput.Read(reader);
            directionalInput = new(input.X, input.Y);
        }

        #endregion

        private struct PlayerInput
        {
            public float X;
            public float Y;

            public static void Write(PlayerInput input, Writer writer)
            {
                writer.WriteSingle(input.X);
                writer.WriteSingle(input.Y);
            }

            public static PlayerInput Read(Reader reader)
            {
                return new()
                {
                    X = reader.ReadSingle(),
                    Y = reader.ReadSingle()
                };
            }
        }
    }
}