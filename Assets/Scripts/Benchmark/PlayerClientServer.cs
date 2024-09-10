using jKnepel.ProteusNet.Networking;
using jKnepel.ProteusNet.Serializing;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes.Benchmark
{
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerClientServer : Player
    {
        private NetworkObject _object;
        private Vector2 _lastInput = Vector2.zero;

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
            else if (!_lastInput.Equals(directionalInput))
            {
                PlayerInput input = new()
                {
                    Horizontal = directionalInput.y,
                    Vertical = directionalInput.x
                };
                Writer writer = new();
                PlayerInput.Write(input, writer);
                _object.SyncNetworkManager.Client.SendByteDataToServer("input", writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
                _lastInput = directionalInput;
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
            var delta = new Vector3(input.Horizontal, 0, input.Vertical);
            _rb.AddForce(forceMult * delta, ForceMode.Force);
        }

        #endregion

        private struct PlayerInput
        {
            public float Horizontal;
            public float Vertical;

            public static void Write(PlayerInput input, Writer writer)
            {
                writer.WriteSingle(input.Horizontal);
                writer.WriteSingle(input.Vertical);
            }

            public static PlayerInput Read(Reader reader)
            {
                return new()
                {
                    Horizontal = reader.ReadSingle(),
                    Vertical = reader.ReadSingle()
                };
            }
        }
    }
}