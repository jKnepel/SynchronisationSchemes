using jKnepel.SimpleUnityNetworking.Managing;
using System;
using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
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
        
        [SerializeField] private string networkID;
        public string NetworkID => networkID;

        [SerializeField] private MonoNetworkManager playModeManager;
        public MonoNetworkManager PlayModeNetworkManager
        {
            get => playModeManager;
            set => playModeManager = value;
        }
        
        [SerializeField] private ESynchroniseMode synchroniseMode;
        public ESynchroniseMode SynchroniseMode
        {
            get => synchroniseMode;
            set => synchroniseMode = value;
        }

        public bool ShouldSynchronise => SynchroniseMode switch
        {
            ESynchroniseMode.PlayMode when Application.isPlaying => true,
            ESynchroniseMode.EditMode when !Application.isPlaying => true,
            _ => false
        };

        public INetworkManager SyncNetworkManager => SynchroniseMode switch
        {
            ESynchroniseMode.PlayMode when Application.isPlaying => PlayModeNetworkManager,
            ESynchroniseMode.EditMode when !Application.isPlaying => StaticNetworkManager.NetworkManager,
            _ => null
        };

        public event Action OnNetworkIDUpdated;

        #endregion
        
        #region lifecycle

#if UNITY_EDITOR
        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            if (ShouldSynchronise) FindNetworkID();
        }
        
        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        }
#else
        private void Awake()
        {
            if (!ShouldSynchronise) return;
            FindNetworkID();
        }
        
        private void OnTransformParentChanged()
        {
            if (!ShouldSynchronise) return;
            FindNetworkID();
        }

        private void Update()
        {
            // TODO : optimise this
            if (!ShouldSynchronise) return;
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
            if (!ShouldSynchronise) return;
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
        private SerializedProperty _playModeManager;
        private SerializedProperty _synchroniseMode;

        private void Awake()
        {
            _playModeManager = serializedObject.FindProperty("playModeManager");
            _synchroniseMode = serializedObject.FindProperty("synchroniseMode");
        }

        public override void OnInspectorGUI()
        {
            GUI.enabled = false;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("networkID"));
            GUI.enabled = true;

            EditorGUILayout.PropertyField(_synchroniseMode);
            if ((ESynchroniseMode)_synchroniseMode.enumValueIndex == ESynchroniseMode.PlayMode)
                EditorGUILayout.PropertyField(_playModeManager);
            
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
