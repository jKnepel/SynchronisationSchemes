using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace jKnepel.SynchronisationSchemes
{
    [CustomEditor(typeof(NetworkTransform))]
    public class NetworkTransformEditor : Editor
    {
        private SerializedProperty _synchronisationChannel;
        private SerializedProperty _synchronisePosition;
        private SerializedProperty _synchroniseRotation;
        private SerializedProperty _synchroniseScale;

        private SerializedProperty _teleportPosition;
        private SerializedProperty _teleportPositionThreshold;
        private SerializedProperty _teleportRotation;
        private SerializedProperty _teleportRotationThreshold;
        private SerializedProperty _teleportScale;
        private SerializedProperty _teleportScaleThreshold;
        
        private SerializedProperty _useInterpolation;
        private SerializedProperty _interpolationInterval;
        
        private SerializedProperty _useExtrapolation;
        private SerializedProperty _extrapolationInterval;

        public void OnEnable() 
        {
            _synchronisationChannel = serializedObject.FindProperty("synchroniseChannel");
            _synchronisePosition = serializedObject.FindProperty("synchronisePosition");
            _synchroniseRotation = serializedObject.FindProperty("synchroniseRotation");
            _synchroniseScale = serializedObject.FindProperty("synchroniseScale");

            _teleportPosition = serializedObject.FindProperty("teleportPosition");
            _teleportPositionThreshold = serializedObject.FindProperty("positionTeleportThreshold");
            _teleportRotation = serializedObject.FindProperty("teleportRotation");
            _teleportRotationThreshold = serializedObject.FindProperty("rotationTeleportThreshold");
            _teleportScale = serializedObject.FindProperty("teleportScale");
            _teleportScaleThreshold = serializedObject.FindProperty("scaleTeleportThreshold");
            
            _useInterpolation = serializedObject.FindProperty("useInterpolation");
            _interpolationInterval = serializedObject.FindProperty("interpolationInterval");
            _useExtrapolation = serializedObject.FindProperty("useExtrapolation");
            _extrapolationInterval = serializedObject.FindProperty("extrapolationInterval");
        }

        public override void OnInspectorGUI()
        {
            var t = (NetworkTransform)target;
            
            EditorGUILayout.PropertyField(_synchronisationChannel, new GUIContent("Synchronisation Channel"));
            t.Type = (NetworkTransform.ComponentType)EditorGUILayout.EnumPopup(new GUIContent("Component Type"), t.Type);
            EditorGUILayout.Space();
            
            GUILayout.Label("Synchronisation", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_synchronisePosition, new GUIContent("Synchronise Position"));
            if (_synchronisePosition.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_teleportPosition, new GUIContent("Teleport Position"));
                if (_teleportPosition.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_teleportPositionThreshold, new GUIContent("Threshold"));
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.PropertyField(_synchroniseRotation, new GUIContent("Synchronise Rotation"));
            if (_synchroniseRotation.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_teleportRotation, new GUIContent("Teleport Rotation"));
                if (_teleportRotation.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_teleportRotationThreshold, new GUIContent("Threshold"));
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.PropertyField(_synchroniseScale, new GUIContent("Synchronise Scale"));
            if (_synchroniseScale.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_teleportScale, new GUIContent("Teleport Scale"));
                if (_teleportScale.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(_teleportScaleThreshold, new GUIContent("Threshold"));
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
            
            EditorGUILayout.Space();
            
            GUILayout.Label("Smoothing", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_useInterpolation, new GUIContent("Use Interpolation"));
            if (_useInterpolation.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_interpolationInterval, new GUIContent("Interval"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.PropertyField(_useExtrapolation, new GUIContent("Use Extrapolation"));
            if (_useExtrapolation.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_extrapolationInterval, new GUIContent("Interval"));
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
        }
    }
}
