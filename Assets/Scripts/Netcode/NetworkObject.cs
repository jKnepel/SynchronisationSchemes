using jKnepel.SimpleUnityNetworking.Managing;
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace jKnepel.SynchronisationSchemes
{
    public enum ESynchroniseMode
    {
        PlayMode,
        EditMode,
    }
    
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("SimpleUnityNetworking/Component/Network Object")]
    public class NetworkObject : MonoBehaviour, IEquatable<NetworkObject>
    {
        #region fields and properties
        
        private Transform _parent;
        private string _objectName;
        private int _siblingIndex;
        
        [SerializeField] protected string networkID;
        public string NetworkID => networkID;

        [SerializeField] protected MonoNetworkManager playModeManager;
        public MonoNetworkManager PlayModeNetworkManager
        {
            get => playModeManager;
            set
            {
                if (playModeManager == value) return;
                playModeManager = value;
                OnSyncNetworkManagerUpdated?.Invoke();
            }
        }
        
        [SerializeField] protected ESynchroniseMode synchroniseMode;
        public ESynchroniseMode SynchroniseMode
        {
            get => synchroniseMode;
            set
            {
                if (synchroniseMode == value) return;
                synchroniseMode = value;
                OnSyncNetworkManagerUpdated?.Invoke();
            }
        }

        public bool IsActiveMode => SynchroniseMode switch
        {
            ESynchroniseMode.PlayMode when Application.isPlaying => true,
            ESynchroniseMode.EditMode when !Application.isPlaying => true,
            _ => false
        };

        public virtual bool ShouldSynchronise => IsActiveMode && SyncNetworkManager is not null && SyncNetworkManager.IsServer;

        public INetworkManager SyncNetworkManager => SynchroniseMode switch
        {
            ESynchroniseMode.PlayMode when Application.isPlaying => PlayModeNetworkManager,
            ESynchroniseMode.EditMode when !Application.isPlaying => StaticNetworkManager.NetworkManager,
            _ => null
        };

        public event Action OnNetworkIDUpdated;
        public event Action OnSyncNetworkManagerUpdated;

        #endregion
        
        #region lifecycle

        // TODO : optimise this
#if UNITY_EDITOR
        protected void Awake()
        {
            if (SynchroniseMode == ESynchroniseMode.PlayMode && SyncNetworkManager is null)
                PlayModeNetworkManager = FindObjectOfType<MonoNetworkManager>();
        }

        protected void OnEnable()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            if (IsActiveMode) FindNetworkID();
        }
        
        protected void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }
#else
        protected void Awake()
        {
            if (!IsActiveMode) return;
            if (SynchroniseMode == ESynchroniseMode.PlayMode && SyncNetworkManager is null)
                PlayModeNetworkManager = FindObjectOfType<MonoNetworkManager>();
            FindNetworkID();
        }
        
        protected void OnTransformParentChanged()
        {
            if (!IsActiveMode) return;
            FindNetworkID();
        }

        protected void Update()
        {
            if (!IsActiveMode) return;
            OnHierarchyChanged();
        }
#endif
        
        #endregion
        
        #region public methods

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public bool Equals(NetworkObject other)
        {
            if (other is null)
                return false;
            return GetHashCode() == other.GetHashCode();
        }
        
        #endregion
        
        #region private methods
        
        private void OnHierarchyChanged()
        {
            if (!IsActiveMode) return;
            if (transform.GetSiblingIndex() == _siblingIndex
                && gameObject.name.Equals(_objectName)
                && transform.parent == _parent) 
                return;
            
            FindNetworkID();
        }

        private void FindNetworkID()
        {
            _parent = transform.parent;
            _objectName = gameObject.name;
            _siblingIndex = transform.GetSiblingIndex();
            
            networkID = FindNetworkID(transform);
            OnNetworkIDUpdated?.Invoke();
        }

        private static string FindNetworkID(Transform parent)
        {
            if (parent.parent is null)
                return $"{parent.name}#{parent.GetSiblingIndex()}";
            return FindNetworkID(parent.parent) + $"{parent.name}#{parent.GetSiblingIndex()}";
        }
        
        #endregion
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(NetworkObject), true)]
    public class NetworkObjectEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var t = (NetworkObject)target;
            
            GUI.enabled = false;
            EditorGUILayout.TextField("Network ID", t.NetworkID);
            GUI.enabled = true;

            t.SynchroniseMode = (ESynchroniseMode)EditorGUILayout.EnumPopup("Synchronise Mode", t.SynchroniseMode);
            if (t.SynchroniseMode == ESynchroniseMode.PlayMode)
                t.PlayModeNetworkManager = (MonoNetworkManager)EditorGUILayout.ObjectField("Play Mode Manager", t.PlayModeNetworkManager, typeof(MonoNetworkManager), true);

            serializedObject.ApplyModifiedProperties();
            
            if (!GUI.changed) return;
            EditorUtility.SetDirty(t);
            EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
    }
#endif
}
