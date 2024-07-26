using jKnepel.SimpleUnityNetworking.Managing;
using jKnepel.SimpleUnityNetworking.Networking;
using jKnepel.SimpleUnityNetworking.Serialising;
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace jKnepel.SynchronisationSchemes
{
    [AddComponentMenu("SimpleUnityNetworking/Component/Network Object (Authority)")]
    public class NetworkObjectAuthority : NetworkObject
    {
	    #region fields and properties
	    
        private ushort _ownershipSequence;
        private ushort _authoritySequence;

        private string _networkObjectFlag;
        private INetworkManager _syncNetworkManager;
        
        public uint OwnershipID { get; private set; }
        public uint AuthorityID { get; private set; }

        public bool IsLocalServerOwner => _syncNetworkManager is not null && _syncNetworkManager.IsServer && OwnershipID == 0;
        public bool IsLocalServerAuthor => _syncNetworkManager is not null && _syncNetworkManager.IsServer && AuthorityID == 0;
        public bool IsOwner => _syncNetworkManager is not null && _syncNetworkManager.IsClient && OwnershipID == _syncNetworkManager.Client.ClientID;
        public bool IsAuthor => _syncNetworkManager is not null && _syncNetworkManager.IsClient && AuthorityID == _syncNetworkManager.Client.ClientID;

        public override bool ShouldSynchronise => IsActiveMode && (IsAuthor || IsLocalServerAuthor);

        public event Action OnOwnershipChanged;
        public event Action OnAuthorityChanged;
        
        // TODO : remove authority from disconnecting clients

        #endregion
        
        #region lifecycle
        
        private void Awake()
        {
            OnNetworkIDUpdated += NetworkIDUpdated;
            OnSyncNetworkManagerUpdated += NetworkIDUpdated;
            _networkObjectFlag = $"{NetworkID}#Authority";
            _syncNetworkManager = SyncNetworkManager;
            _syncNetworkManager?.Client.RegisterByteData(_networkObjectFlag, UpdateAuthority);
            _syncNetworkManager?.Server.RegisterByteData(_networkObjectFlag, UpdateAuthorityServer);
        }
        
        #endregion
        
        #region public methods
        
		public void RequestOwnership()
		{
			if (OwnershipID != 0 || !IsActiveMode || (!_syncNetworkManager?.IsClient ?? true))
				return;
			
			var clientID = _syncNetworkManager.Client.ClientID;
			_ownershipSequence++;
			if (AuthorityID != clientID) _authoritySequence++;
			Writer writer = new();
			NetworkAuthorityPacket packet = new(NetworkAuthorityPacket.EPacketTypes.RequestOwnership, clientID, _ownershipSequence, _authoritySequence);
			if (_syncNetworkManager.IsHost)
			{
				SetTakeOwnership(clientID, _ownershipSequence);
				SetTakeAuthority(clientID, _authoritySequence);
				NetworkAuthorityPacket.Write(writer, packet);
				_syncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			}
			else
			{
				NetworkAuthorityPacket.Write(writer, packet);
				_syncNetworkManager.Client.SendByteDataToServer(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			}
		}

		public void ReleaseOwnership()
		{
			if (!IsOwner || !IsActiveMode) 
				return;

			_ownershipSequence++;
			Writer writer = new();
			NetworkAuthorityPacket packet = new(NetworkAuthorityPacket.EPacketTypes.ReleaseOwnership, _syncNetworkManager.Client.ClientID, _ownershipSequence, _authoritySequence);
			if (_syncNetworkManager.IsHost)
			{
				SetReleaseOwnership(packet.OwnershipSequence);
				NetworkAuthorityPacket.Write(writer, packet);
				_syncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			}
			else
			{
				NetworkAuthorityPacket.Write(writer, packet);
				_syncNetworkManager.Client.SendByteDataToServer(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			}
		}

		public void RequestAuthority()
		{
			if (OwnershipID != 0 || IsAuthor || !IsActiveMode || (!_syncNetworkManager?.IsClient ?? true))
				return;

			_authoritySequence++;
			Writer writer = new();
			NetworkAuthorityPacket packet = new(NetworkAuthorityPacket.EPacketTypes.RequestAuthority, _syncNetworkManager.Client.ClientID, _ownershipSequence, _authoritySequence);
			if (_syncNetworkManager.IsHost)
			{
				SetTakeAuthority(_syncNetworkManager.Client.ClientID, _authoritySequence);
				NetworkAuthorityPacket.Write(writer, packet);
				_syncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			}
			else
			{
				NetworkAuthorityPacket.Write(writer, packet);
				_syncNetworkManager.Client.SendByteDataToServer(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			}
		}

		public void ReleaseAuthority()
		{
			if (!IsAuthor || IsOwner || !IsActiveMode) 
				return;

			_authoritySequence++;
			Writer writer = new();
			NetworkAuthorityPacket packet = new(NetworkAuthorityPacket.EPacketTypes.ReleaseAuthority, _syncNetworkManager.Client.ClientID, _ownershipSequence, _authoritySequence);
			if (_syncNetworkManager.IsHost)
			{
				SetReleaseAuthority(packet.AuthoritySequence);
				NetworkAuthorityPacket.Write(writer, packet);
				_syncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			}
			else
			{
				NetworkAuthorityPacket.Write(writer, packet);
				_syncNetworkManager.Client.SendByteDataToServer(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			}
		}
		
		#endregion
		
		#region private methods
        
        private void NetworkIDUpdated()
        {
	        _syncNetworkManager?.Client.UnregisterByteData(_networkObjectFlag, UpdateAuthority);
	        _syncNetworkManager?.Server.UnregisterByteData(_networkObjectFlag, UpdateAuthorityServer);
            _networkObjectFlag = $"{NetworkID}#Authority";
            _syncNetworkManager = SyncNetworkManager;
            _syncNetworkManager?.Client.RegisterByteData(_networkObjectFlag, UpdateAuthority);
            _syncNetworkManager?.Server.RegisterByteData(_networkObjectFlag, UpdateAuthorityServer);
        }

        private void UpdateAuthorityServer(ByteData data)
        {
	        NetworkAuthorityPacket packet;

	        try
	        {
		        Reader reader = new(data.Data);
		        packet = NetworkAuthorityPacket.Read(reader);
	        }
	        catch (IndexOutOfRangeException) { return; }
	        
	        switch (packet.PacketType)
	        {
		        case NetworkAuthorityPacket.EPacketTypes.RequestOwnership:
			        if (OwnershipID != 0 || !IsOwnershipNewer(packet.OwnershipSequence))
			        {
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.RequestOwnership, OwnershipID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        _syncNetworkManager.Server.SendByteDataToClient(data.SenderID, _networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			        }
			        else
			        {
				        SetTakeOwnership(data.SenderID, packet.OwnershipSequence);
				        SetTakeAuthority(data.SenderID, packet.AuthoritySequence);
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.RequestOwnership, OwnershipID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        _syncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			        }

			        break;
		        case NetworkAuthorityPacket.EPacketTypes.ReleaseOwnership:
			        if (OwnershipID != data.SenderID || !IsOwnershipNewer(packet.OwnershipSequence))
			        {
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.RequestOwnership, OwnershipID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        _syncNetworkManager.Server.SendByteDataToClient(data.SenderID, _networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			        }
			        else
			        {
				        SetReleaseOwnership(packet.OwnershipSequence);
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.ReleaseOwnership, OwnershipID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        _syncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			        }

			        break;
		        case NetworkAuthorityPacket.EPacketTypes.RequestAuthority:
			        if (OwnershipID != 0 || AuthorityID == data.SenderID || !IsAuthorityNewer(packet.AuthoritySequence))
			        {
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.RequestAuthority, AuthorityID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        _syncNetworkManager.Server.SendByteDataToClient(data.SenderID, _networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			        }
			        else
			        {
				        SetTakeAuthority(data.SenderID, packet.AuthoritySequence);
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.RequestAuthority, AuthorityID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        _syncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			        }

			        break;
		        case NetworkAuthorityPacket.EPacketTypes.ReleaseAuthority:
			        if (AuthorityID != data.SenderID || !IsAuthorityNewer(packet.AuthoritySequence))
			        {
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.RequestAuthority, AuthorityID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        _syncNetworkManager.Server.SendByteDataToClient(data.SenderID, _networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			        }
			        else
			        {
				        SetReleaseAuthority(packet.AuthoritySequence);
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.ReleaseAuthority, AuthorityID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        _syncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer(), ENetworkChannel.ReliableOrdered);
			        }

			        break;
	        }
        }
        
        private void UpdateAuthority(ByteData data)
        {
	        NetworkAuthorityPacket packet;

	        try
	        {
		        Reader reader = new(data.Data);
		        packet = NetworkAuthorityPacket.Read(reader);
	        }
	        catch (IndexOutOfRangeException) { return; }
	        
	        switch (packet.PacketType)
	        {
		        case NetworkAuthorityPacket.EPacketTypes.RequestOwnership:
			        SetTakeOwnership(packet.ClientID, packet.OwnershipSequence);
			        SetTakeAuthority(packet.ClientID, packet.AuthoritySequence);
			        break;
		        case NetworkAuthorityPacket.EPacketTypes.ReleaseOwnership:
			        SetReleaseOwnership(packet.OwnershipSequence);
			        break;
		        case NetworkAuthorityPacket.EPacketTypes.RequestAuthority:
			        SetTakeAuthority(packet.ClientID, packet.AuthoritySequence);
			        break;
		        case NetworkAuthorityPacket.EPacketTypes.ReleaseAuthority:
			        SetReleaseAuthority(packet.AuthoritySequence);
			        break;
	        }
        }
        
        private void SetTakeOwnership(uint clientID, ushort ownershipSequence)
        {
            OwnershipID = clientID;
            _ownershipSequence = ownershipSequence;
            OnOwnershipChanged?.Invoke();
        }

        private void SetReleaseOwnership(ushort ownershipSequence)
        {
            OwnershipID = 0;
            _ownershipSequence = ownershipSequence;
            OnOwnershipChanged?.Invoke();
        }

        private void SetTakeAuthority(uint clientID, ushort authoritySequence)
        {
            AuthorityID = clientID;
            _authoritySequence = authoritySequence;
            OnAuthorityChanged?.Invoke();
        }

        private void SetReleaseAuthority(ushort authoritySequence)
        {
            AuthorityID = 0;
            _authoritySequence = authoritySequence;
            OnAuthorityChanged?.Invoke();
        }
        
        private const ushort HALF_USHORT = ushort.MaxValue / 2;
        private bool IsOwnershipNewer(ushort ownershipSequence)
        {
	        return (ownershipSequence > _ownershipSequence && ownershipSequence - _ownershipSequence <= HALF_USHORT)
	               || (ownershipSequence < _ownershipSequence && _ownershipSequence - ownershipSequence > HALF_USHORT);
        }

        private bool IsAuthorityNewer(ushort authoritySequence)
        {
	        return (authoritySequence > _authoritySequence && authoritySequence - _authoritySequence <= HALF_USHORT)
	               || (authoritySequence < _authoritySequence && _authoritySequence - authoritySequence > HALF_USHORT);
        }
        
        #endregion
        
        private struct NetworkAuthorityPacket
        {
	        public enum EPacketTypes : byte
	        {
		        RequestOwnership = 0,
		        ReleaseOwnership = 1,
		        RequestAuthority = 2,
		        ReleaseAuthority = 3
	        }

	        public EPacketTypes PacketType;
	        public uint ClientID;
	        public ushort OwnershipSequence;
	        public ushort AuthoritySequence;

	        public NetworkAuthorityPacket(EPacketTypes packetType, uint clientID, ushort ownershipSequence, ushort authoritySequence)
	        {
		        PacketType = packetType;
		        ClientID = clientID;
		        OwnershipSequence = ownershipSequence;
		        AuthoritySequence = authoritySequence;
	        }

	        public static NetworkAuthorityPacket Read(Reader reader)
	        {
		        return new()
		        {
			        PacketType = (EPacketTypes)reader.ReadByte(),
			        ClientID = reader.ReadUInt32(),
			        OwnershipSequence = reader.ReadUInt16(),
			        AuthoritySequence = reader.ReadUInt16()
		        };
	        }

	        public static void Write(Writer writer, NetworkAuthorityPacket packet)
	        {
		        writer.WriteByte((byte)packet.PacketType); 
		        writer.WriteUInt32(packet.ClientID);
		        writer.WriteUInt16(packet.OwnershipSequence);
		        writer.WriteUInt16(packet.AuthoritySequence);
	        }
        }
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(NetworkObjectAuthority), true)]
    public class NetworkObjectAuthorityEditor : NetworkObjectEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var t = (NetworkObjectAuthority)target;

            EditorGUILayout.Space();
            GUILayout.Label("Authority:", EditorStyles.boldLabel);
            
            using (new GUILayout.HorizontalScope())
            {
	            switch (t.IsOwner)
	            {
		            case true when GUILayout.Button("Release Ownership", GUILayout.MinWidth(150)):
			            t.ReleaseOwnership();
			            break;
		            case false when GUILayout.Button("Request Ownership", GUILayout.MinWidth(150)):
			            t.RequestOwnership();
			            break;
	            }
	            GUI.enabled = false;
                EditorGUILayout.Space();
	            GUILayout.Label(new GUIContent("Owner ID:", "The client ID of the owning client"));
                EditorGUILayout.IntField((int)t.OwnershipID);
                EditorGUILayout.Space();
	            GUILayout.Label("Is Owner:");
                EditorGUILayout.Toggle(t.IsOwner);
	            GUI.enabled = true;
            }

            using (new GUILayout.HorizontalScope())
            {
	            switch (t.IsAuthor)
	            {
		            case true when GUILayout.Button("Release Authority", GUILayout.MinWidth(150)):
			            t.ReleaseAuthority();
			            break;
		            case false when GUILayout.Button("Request Authority", GUILayout.MinWidth(150)):
			            t.RequestAuthority();
			            break;
	            }
	            GUI.enabled = false;
	            EditorGUILayout.Space();
	            GUILayout.Label(new GUIContent("Author ID:", "The client ID of the authoritative client"));
	            EditorGUILayout.IntField((int)t.AuthorityID);
	            EditorGUILayout.Space();
	            GUILayout.Label("Is Author:");
	            EditorGUILayout.Toggle(t.IsAuthor);
	            GUI.enabled = true;
            }
        }
    }
#endif
}
