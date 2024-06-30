using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Modules;
using jKnepel.SimpleUnityNetworking.Networking;
using jKnepel.SimpleUnityNetworking.Serialising;
using System.Linq;

namespace jKnepel.SynchronisationSchemes
{
    public class SynchroModule : Module
    {
        public override string Name => "SynchroModule";

        public SynchroModule(INetworkManager networkManager, ModuleConfiguration moduleConfig)
            : base(networkManager, moduleConfig)
        {
            NetworkManager.OnTickStarted += SendNetworkUpdates;
        }

        protected override void Dispose(bool disposing) { }

        private void SendNetworkUpdates(uint _)
        {
            if (!NetworkManager.IsClient
                || !NetworkManager.TryGetNetworkUpdates(out var updates))
                return;

            Writer writer = new(NetworkManager.SerialiserSettings);
            foreach (var update in updates.ToList())
            {
                foreach (var data in update.UpdateValues)
                {
                    writer.WriteByte(data.Item1);
                    writer.Write(data.Item2);
                }
                NetworkManager.Client.SendByteDataToAll(update.NetworkIdentifier, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
                writer.Clear();
                updates.Remove(update);
            }
        }

#if UNITY_EDITOR
        public override bool HasGUI => false;
#endif
    }
}
