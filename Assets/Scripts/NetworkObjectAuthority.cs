using jKnepel.SimpleUnityNetworking.Serialising;
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
        
        public uint OwnershipID { get; private set; }
        public uint AuthorityID { get; private set; }
        public bool IsOwner => (SyncNetworkManager?.IsClient ?? false) && OwnershipID == SyncNetworkManager.Client.ClientID;
        public bool IsAuthor => (SyncNetworkManager?.IsClient ?? false) && AuthorityID == SyncNetworkManager.Client.ClientID;
        
        #endregion
        
        #region lifecycle
        
        private void Awake()
        {
            OnNetworkIDUpdated += NetworkIDUpdated;
            _networkObjectFlag = $"{NetworkID}#Authority";
            SyncNetworkManager?.Client.RegisterByteData(_networkObjectFlag, UpdateAuthority);
            SyncNetworkManager?.Server.RegisterByteData(_networkObjectFlag, UpdateAuthorityServer);
        }
        
        #endregion
        
        #region public methods
        
		public void RequestOwnership()
		{
			if (OwnershipID != 0 || !ShouldSynchronise || (!SyncNetworkManager?.IsClient ?? true))
				return;
			
			var clientID = SyncNetworkManager.Client.ClientID;
			_ownershipSequence++;
			if (AuthorityID != clientID) _authoritySequence++;
			Writer writer = new();
			NetworkAuthorityPacket packet = new(NetworkAuthorityPacket.EPacketTypes.TakeOwnership, clientID, _ownershipSequence, _authoritySequence);
			if (SyncNetworkManager.IsHost)
			{
				SetTakeOwnership(clientID, _ownershipSequence);
				SetTakeAuthority(clientID, _authoritySequence);
				NetworkAuthorityPacket.Write(writer, packet);
				SyncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer());
			}
			else
			{
				NetworkAuthorityPacket.Write(writer, packet);
				SyncNetworkManager.Client.SendByteDataToServer(_networkObjectFlag, writer.GetBuffer());
			}
		}

		public void ReleaseOwnership()
		{
			if (!IsOwner || !ShouldSynchronise) 
				return;

			Writer writer = new();
			_ownershipSequence++;
			NetworkAuthorityPacket packet = new(NetworkAuthorityPacket.EPacketTypes.ReleaseOwnership, SyncNetworkManager.Client.ClientID, _ownershipSequence, _authoritySequence);
			if (SyncNetworkManager.IsHost)
			{
				SetReleaseOwnership(packet.OwnershipSequence);
				NetworkAuthorityPacket.Write(writer, packet);
				SyncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer());
			}
			else
			{
				NetworkAuthorityPacket.Write(writer, packet);
				SyncNetworkManager.Client.SendByteDataToServer(_networkObjectFlag, writer.GetBuffer());
			}
		}

		public void RequestAuthority()
		{
			if (OwnershipID != 0 || IsAuthor || !ShouldSynchronise)
				return;

			_authoritySequence++;
			Writer writer = new();
			NetworkAuthorityPacket packet = new(NetworkAuthorityPacket.EPacketTypes.TakeAuthority, SyncNetworkManager.Client.ClientID, _ownershipSequence, _authoritySequence);
			if (SyncNetworkManager.IsHost)
			{
				SetTakeAuthority(SyncNetworkManager.Client.ClientID, _authoritySequence);
				NetworkAuthorityPacket.Write(writer, packet);
				SyncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer());
			}
			else
			{
				NetworkAuthorityPacket.Write(writer, packet);
				SyncNetworkManager.Client.SendByteDataToServer(_networkObjectFlag, writer.GetBuffer());
			}
		}

		public void ReleaseAuthority()
		{
			if (!IsAuthor || IsOwner || !ShouldSynchronise) 
				return;

			_authoritySequence++;
			Writer writer = new();
			NetworkAuthorityPacket packet = new(NetworkAuthorityPacket.EPacketTypes.ReleaseAuthority, SyncNetworkManager.Client.ClientID, _ownershipSequence, _authoritySequence);
			if (SyncNetworkManager.IsHost)
			{
				SetReleaseAuthority(packet.AuthoritySequence);
				NetworkAuthorityPacket.Write(writer, packet);
				SyncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer());
			}
			else
			{
				NetworkAuthorityPacket.Write(writer, packet);
				SyncNetworkManager.Client.SendByteDataToServer(_networkObjectFlag, writer.GetBuffer());
			}
		}
		
		#endregion
		
		#region private methods
        
        private void NetworkIDUpdated()
        {
            SyncNetworkManager?.Client.UnregisterByteData(_networkObjectFlag, UpdateAuthority);
            SyncNetworkManager?.Server.UnregisterByteData(_networkObjectFlag, UpdateAuthorityServer);
            _networkObjectFlag = $"{NetworkID}#Authority";
            SyncNetworkManager?.Client.RegisterByteData(_networkObjectFlag, UpdateAuthority);
            SyncNetworkManager?.Server.RegisterByteData(_networkObjectFlag, UpdateAuthorityServer);
        }

        private void UpdateAuthorityServer(uint sender, byte[] data)
        {
	        Reader reader = new(data);
	        var packet = NetworkAuthorityPacket.Read(reader);
	        Debug.Log(packet.PacketType);
	        switch (packet.PacketType)
	        {
		        case NetworkAuthorityPacket.EPacketTypes.TakeOwnership:
			        if (OwnershipID != 0 || !IsOwnershipNewer(packet.OwnershipSequence))
			        {
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.TakeOwnership, OwnershipID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        SyncNetworkManager.Server.SendByteDataToClient(sender, _networkObjectFlag, writer.GetBuffer());
			        }
			        else
			        {
				        SetTakeOwnership(sender, packet.OwnershipSequence);
				        SetTakeAuthority(sender, packet.AuthoritySequence);
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.TakeOwnership, OwnershipID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        SyncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer());
			        }

			        break;
		        case NetworkAuthorityPacket.EPacketTypes.ReleaseOwnership:
			        if (OwnershipID != sender || !IsOwnershipNewer(packet.OwnershipSequence))
			        {
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.TakeOwnership, OwnershipID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        SyncNetworkManager.Server.SendByteDataToClient(sender, _networkObjectFlag, writer.GetBuffer());
			        }
			        else
			        {
				        SetReleaseOwnership(packet.OwnershipSequence);
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.ReleaseOwnership, OwnershipID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        SyncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer());
			        }

			        break;
		        case NetworkAuthorityPacket.EPacketTypes.TakeAuthority:
			        if (OwnershipID != 0 || AuthorityID == sender || !IsAuthorityNewer(packet.AuthoritySequence))
			        {
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.TakeAuthority, AuthorityID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        SyncNetworkManager.Server.SendByteDataToClient(sender, _networkObjectFlag, writer.GetBuffer());
			        }
			        else
			        {
				        SetTakeAuthority(sender, packet.AuthoritySequence);
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.TakeAuthority, AuthorityID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        SyncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer());
			        }

			        break;
		        case NetworkAuthorityPacket.EPacketTypes.ReleaseAuthority:
			        if (AuthorityID != sender || !IsAuthorityNewer(packet.AuthoritySequence))
			        {
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.TakeAuthority, AuthorityID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        SyncNetworkManager.Server.SendByteDataToClient(sender, _networkObjectFlag, writer.GetBuffer());
			        }
			        else
			        {
				        SetReleaseAuthority(packet.AuthoritySequence);
				        Writer writer = new();
				        NetworkAuthorityPacket answer = new(NetworkAuthorityPacket.EPacketTypes.ReleaseAuthority, AuthorityID, _ownershipSequence, _authoritySequence);
				        NetworkAuthorityPacket.Write(writer, answer);
				        SyncNetworkManager.Server.SendByteDataToAll(_networkObjectFlag, writer.GetBuffer());
			        }

			        break;
	        }
        }
        
        private void UpdateAuthority(uint sender, byte[] data)
        {
            Reader reader = new(data);
            var packet = NetworkAuthorityPacket.Read(reader);
            switch (packet.PacketType)
            {
                case NetworkAuthorityPacket.EPacketTypes.TakeOwnership:
                    SetTakeOwnership(packet.ClientID, packet.OwnershipSequence);
                    SetTakeAuthority(packet.ClientID, packet.AuthoritySequence);
                    break;
                case NetworkAuthorityPacket.EPacketTypes.ReleaseOwnership:
                    SetReleaseOwnership(packet.OwnershipSequence);
                    break;
                case NetworkAuthorityPacket.EPacketTypes.TakeAuthority:
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
        }

        private void SetReleaseOwnership(ushort ownershipSequence)
        {
            OwnershipID = 0;
            _ownershipSequence = ownershipSequence;
        }

        private void SetTakeAuthority(uint clientID, ushort authoritySequence)
        {
            AuthorityID = clientID;
            _authoritySequence = authoritySequence;
        }

        private void SetReleaseAuthority(ushort authoritySequence)
        {
            AuthorityID = 0;
            _authoritySequence = authoritySequence;
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
		        TakeOwnership = 0,
		        ReleaseOwnership = 1,
		        TakeAuthority = 2,
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
            
            GUI.enabled = false;
            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.IntField(new GUIContent("Ownership ID"), (int)t.OwnershipID);
                EditorGUILayout.Space();
                EditorGUILayout.ToggleLeft("Is Owner", t.IsOwner);
            }

            using (new GUILayout.HorizontalScope())
            {
                EditorGUILayout.IntField(new GUIContent("Authority ID"), (int)t.AuthorityID);
                EditorGUILayout.Space();
                EditorGUILayout.ToggleLeft("Is Author", t.IsAuthor);
            }
            GUI.enabled = true;
            
            switch (t.IsOwner)
            {
	            case true when GUILayout.Button("Release Ownership"):
		            t.ReleaseOwnership();
		            break;
	            case false when GUILayout.Button("Request Ownership"):
		            t.RequestOwnership();
		            break;
            }
            
            switch (t.IsAuthor)
            {
	            case true when GUILayout.Button("Release Authority"):
		            t.ReleaseAuthority();
		            break;
	            case false when GUILayout.Button("Request Authority"):
		            t.RequestAuthority();
		            break;
            }
        }
    }
#endif
}
