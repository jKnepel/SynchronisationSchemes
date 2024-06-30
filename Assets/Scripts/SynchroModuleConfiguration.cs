using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Modules;
using UnityEngine;

namespace jKnepel.SynchronisationSchemes
{
    [CreateAssetMenu(fileName = "SynchroModuleConfiguration", menuName = "SimpleUnityNetworking/Modules/SynchroModuleConfiguration")]
    public class SynchroModuleConfiguration : ModuleConfiguration
    {
        public override Module GetModule(INetworkManager networkManager)
        {
            return new SynchroModule(networkManager, this);
        }
    }
}
