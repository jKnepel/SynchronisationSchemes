using jKnepel.SimpleUnityNetworking.Managing;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace jKnepel.SynchronisationSchemes
{
    public class NetworkUpdate
    {
        public readonly string NetworkIdentifier;
        public readonly List<(byte, object)> UpdateValues;

        public NetworkUpdate(string networkIdentifier)
        {
            NetworkIdentifier = networkIdentifier;
            UpdateValues = new();
        }

        public void Add(byte flag, object data)
        {
            UpdateValues.Add((flag, data));
        }

        public override int GetHashCode()
        {
            return NetworkIdentifier.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkUpdate update && update.NetworkIdentifier.Equals(NetworkIdentifier);
        }
    }
    
    public static class NetworkManagerExtensions
    {
        private static readonly ConcurrentDictionary<INetworkManager, HashSet<NetworkUpdate>> _networkUpdates = new();
        
        public static void AddNetworkUpdate(this INetworkManager networkManager, NetworkUpdate update)
        {
            if (!_networkUpdates.TryGetValue(networkManager, out var updates))
            {
                _networkUpdates.TryAdd(networkManager, new() { update });
                return;
            }

            if (updates.TryGetValue(update, out _))
                updates.Remove(update);
            
            updates.Add(update);
        }
        
        public static void RemoveNetworkUpdate(this INetworkManager networkManager, NetworkUpdate update)
        {
            if (!_networkUpdates.TryGetValue(networkManager, out var updates))
                return;

            updates.Remove(update);
        }

        public static bool TryGetNetworkUpdates(this INetworkManager networkManager, out HashSet<NetworkUpdate> updates)
        {
            return _networkUpdates.TryGetValue(networkManager, out updates);
        }
    }
}
