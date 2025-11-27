/*
 * Copyright (C) 2025 ThizThizzyDizzy (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using DizzyRPC.Attribute;
using UnityEditor;
using UnityEngine;
using VRC.Udon.Editor.ProgramSources.UdonGraphProgram;
using Object = UnityEngine.Object;

namespace DizzyRPC.Editor
{
    public class RPCGraphDataStorage : ScriptableObject
    {
        [SerializeField]
        public RPCGraphData[] graphData = new RPCGraphData[0];
    }

    public class SerializedRPCGraphDataStorage : SerializedObject
    {
        public readonly SerializedProperty graphData;

        public SerializedRPCGraphDataStorage(Object obj) : base(obj)
        {
            graphData = FindProperty(nameof(RPCGraphDataStorage.graphData));
        }

        public RPCGraphDataSerializedProperty GetGraphData(UdonGraphProgramAsset graphAsset)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(graphAsset, out var guid, out long _))
            {
                Debug.LogError("Tried to get graph data for an Udon Graph Program Asset that doesn't exist!");
                return null;
            }

            int dataIndex = -1;
            for (int i = 0; i < graphData.arraySize; i++)
            {
                if (graphData.GetArrayElementAtIndex(i).FindPropertyRelative("guid").stringValue == AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(graphAsset)))
                {
                    dataIndex = i;
                    break;
                }
            }

            // If it doesn't exist, you might want to create it. For this example, we assume it does.
            if (dataIndex == -1)
            {
                dataIndex = graphData.arraySize;
                graphData.InsertArrayElementAtIndex(dataIndex);
                SerializedProperty newGraphDataProp = graphData.GetArrayElementAtIndex(dataIndex);
                newGraphDataProp.FindPropertyRelative("guid").stringValue = guid;
                // Initialize arrays to ensure they are empty
                newGraphDataProp.FindPropertyRelative("rpcMethods").ClearArray();
                newGraphDataProp.FindPropertyRelative("rpcHooks").ClearArray();
            }

            return new RPCGraphDataSerializedProperty(graphData.GetArrayElementAtIndex(dataIndex));
        }

        public void OnSave()
        {
            for (int i = 0; i < graphData.arraySize; i++)
            {
                var guid = graphData.GetArrayElementAtIndex(i).FindPropertyRelative("guid").stringValue;
                if (AssetDatabase.GUIDToAssetPath(guid) == "")
                {
                    graphData.DeleteArrayElementAtIndex(i);
                    i--;
                }
            }
        }
    }

    [Serializable]
    public class RPCGraphData
    {
        public string guid;
        public bool singleton;
        public bool router;
        public string routerTypeName = typeof(int).AssemblyQualifiedName;
        public string routerIdType;
        public Type RouterIdType { get => Type.GetType(routerIdType); set => routerIdType = value.AssemblyQualifiedName; }
        public List<RPCGraphMethodData> rpcMethods = new();
        public List<RPCGraphHookData> rpcHooks = new();

        public RPCGraphData(string guid)
        {
            this.guid = guid;
        }
    }

    public class RPCGraphDataSerializedProperty
    {
        private readonly SerializedProperty prop;

        private readonly SerializedProperty _guid;
        private readonly SerializedProperty _singleton;
        private readonly SerializedProperty _router;
        private readonly SerializedProperty _routerTypeName;
        private readonly SerializedProperty _routerIdType;
        private readonly SerializedProperty _rpcMethods;
        private readonly SerializedProperty _rpcHooks;

        public RPCGraphMethodDataSerializedProperty[] rpcMethods;
        public RPCGraphHookDataSerializedProperty[] rpcHooks;
        private string guid { get => _guid.stringValue; set => _guid.stringValue = value; }
        public bool singleton { get => _singleton.boolValue; set => _singleton.boolValue = value; }
        public bool router { get => _router.boolValue; set => _router.boolValue = value; }
        public string routerTypeName { get => _routerTypeName.stringValue; set => _routerTypeName.stringValue = value; }
        public Type routerIdType { get => Type.GetType(_routerIdType.stringValue); set => _routerIdType.stringValue = value.AssemblyQualifiedName; }

        public RPCGraphDataSerializedProperty(SerializedProperty prop)
        {
            this.prop = prop;
            _guid = prop.FindPropertyRelative(nameof(RPCGraphData.guid));
            _singleton = prop.FindPropertyRelative(nameof(RPCGraphData.singleton));
            _router = prop.FindPropertyRelative(nameof(RPCGraphData.router));
            _routerTypeName = prop.FindPropertyRelative(nameof(RPCGraphData.routerTypeName));
            _routerIdType = prop.FindPropertyRelative(nameof(RPCGraphData.routerIdType));
            _rpcMethods = prop.FindPropertyRelative(nameof(RPCGraphData.rpcMethods));
            _rpcHooks = prop.FindPropertyRelative(nameof(RPCGraphData.rpcHooks));
            RefreshMethodsAndHooksLists();
        }

        private void RefreshMethodsAndHooksLists()
        {
            rpcMethods = new RPCGraphMethodDataSerializedProperty[_rpcMethods.arraySize];
            for (int i = 0; i < _rpcMethods.arraySize; i++)
            {
                rpcMethods[i] = new(_rpcMethods.GetArrayElementAtIndex(i));
            }

            rpcHooks = new RPCGraphHookDataSerializedProperty[_rpcHooks.arraySize];
            for (int i = 0; i < _rpcHooks.arraySize; i++)
            {
                rpcHooks[i] = new(_rpcHooks.GetArrayElementAtIndex(i));
            }
        }

        public void AddRPCMethod(string eventName)
        {
            _rpcMethods.InsertArrayElementAtIndex(_rpcMethods.arraySize);
            RefreshMethodsAndHooksLists();
            rpcMethods[rpcMethods.Length - 1].name = eventName;
        }

        public void RemoveRPCMethod(RPCGraphMethodDataSerializedProperty method)
        {
            for (int i = 0; i < rpcMethods.Length; i++)
            {
                if (rpcMethods[i] == method)
                {
                    _rpcMethods.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            RefreshMethodsAndHooksLists();
        }

        public void AddRPCHook(string eventName)
        {
            _rpcHooks.InsertArrayElementAtIndex(_rpcHooks.arraySize);
            RefreshMethodsAndHooksLists();
            rpcHooks[rpcHooks.Length - 1].name = eventName;
        }

        public void RemoveRPCHook(RPCGraphHookDataSerializedProperty hook)
        {
            for (int i = 0; i < rpcHooks.Length; i++)
            {
                if (rpcHooks[i] == hook)
                {
                    _rpcHooks.DeleteArrayElementAtIndex(i);
                    break;
                }
            }

            RefreshMethodsAndHooksLists();
        }
    }

    /// <summary>
    /// This must match the fields in RPCMethodAttribute
    /// </summary>
    [Serializable]
    public class RPCGraphMethodData : RPCMethodDefinition
    {
        public string name;
        public int rateLimitPerSecond = -1;
        public bool enforceSecure = false;
        public bool allowDropping = true;
        public bool requireLowLatency = false;
        public bool ignoreDuplicates = false;
        public RPCSyncMode mode = RPCSyncMode.Automatic;

        public string[] parameterTypes;
        public string[] parameterNames;

        public int RateLimitPerSecond => rateLimitPerSecond;
        public bool EnforceSecure => enforceSecure;
        public bool AllowDropping => allowDropping;
        public bool RequireLowLatency => requireLowLatency;
        public bool IgnoreDuplicates => ignoreDuplicates;
        public RPCSyncMode Mode => mode;

        public RPCGraphMethodData(string name)
        {
            this.name = name;
        }
    }

    public class RPCGraphMethodDataSerializedProperty
    {
        private readonly SerializedProperty prop;

        private readonly SerializedProperty _name;
        private readonly SerializedProperty _rateLimitPerSecond;
        private readonly SerializedProperty _enforceSecure;
        private readonly SerializedProperty _allowDropping;
        private readonly SerializedProperty _requireLowLatency;
        private readonly SerializedProperty _ignoreDuplicates;
        private readonly SerializedProperty _mode;
        private readonly SerializedProperty _parameterNames;
        private readonly SerializedProperty _parameterTypes;

        public string name { get => _name.stringValue; set => _name.stringValue = value; }
        public int rateLimitPerSecond { get => _rateLimitPerSecond.intValue; set => _rateLimitPerSecond.intValue = value; }
        public bool enforceSecure { get => _enforceSecure.boolValue; set => _enforceSecure.boolValue = value; }
        public bool allowDropping { get => _allowDropping.boolValue; set => _allowDropping.boolValue = value; }
        public bool requireLowLatency { get => _requireLowLatency.boolValue; set => _requireLowLatency.boolValue = value; }
        public bool ignoreDuplicates { get => _ignoreDuplicates.boolValue; set => _ignoreDuplicates.boolValue = value; }
        public RPCSyncMode mode { get => (RPCSyncMode)_mode.enumValueIndex; set => _mode.enumValueIndex = (int)value; }
        public SerializedProperty[] parameterNames;
        public SerializedProperty[] parameterTypes;

        public RPCGraphMethodDataSerializedProperty(SerializedProperty prop)
        {
            this.prop = prop;
            _name = prop.FindPropertyRelative(nameof(RPCGraphMethodData.name));
            _rateLimitPerSecond = prop.FindPropertyRelative(nameof(RPCGraphMethodData.rateLimitPerSecond));
            _enforceSecure = prop.FindPropertyRelative(nameof(RPCGraphMethodData.enforceSecure));
            _allowDropping = prop.FindPropertyRelative(nameof(RPCGraphMethodData.allowDropping));
            _requireLowLatency = prop.FindPropertyRelative(nameof(RPCGraphMethodData.requireLowLatency));
            _ignoreDuplicates = prop.FindPropertyRelative(nameof(RPCGraphMethodData.ignoreDuplicates));
            _mode = prop.FindPropertyRelative(nameof(RPCGraphMethodData.mode));
            _parameterNames = prop.FindPropertyRelative(nameof(RPCGraphMethodData.parameterNames));
            _parameterTypes = prop.FindPropertyRelative(nameof(RPCGraphMethodData.parameterTypes));

            RefreshParametersList();
        }

        private void RefreshParametersList()
        {
            parameterNames = new SerializedProperty[_parameterNames.arraySize];
            for (int i = 0; i < _parameterNames.arraySize; i++)
            {
                parameterNames[i] = _parameterNames.GetArrayElementAtIndex(i);
            }

            parameterTypes = new SerializedProperty[_parameterTypes.arraySize];
            for (int i = 0; i < _parameterTypes.arraySize; i++)
            {
                parameterTypes[i] = _parameterTypes.GetArrayElementAtIndex(i);
            }
        }

        public void EnsureParameterCount(int parameterCount)
        {
            if (parameterCount < 0) parameterCount = 0;
            while (_parameterNames.arraySize < parameterCount) _parameterNames.InsertArrayElementAtIndex(_parameterNames.arraySize);
            while (_parameterNames.arraySize > parameterCount) _parameterNames.DeleteArrayElementAtIndex(_parameterNames.arraySize - 1);

            while (_parameterTypes.arraySize < parameterCount) _parameterTypes.InsertArrayElementAtIndex(_parameterTypes.arraySize);
            while (_parameterTypes.arraySize > parameterCount) _parameterTypes.DeleteArrayElementAtIndex(_parameterTypes.arraySize - 1);

            RefreshParametersList();
        }
    }

    /// <summary>
    /// This must match the fields in RPCHookAttribute
    /// </summary>
    [Serializable]
    public class RPCGraphHookData
    {
        public string name;
        public string fullTypeName;
        public string methodName;

        public RPCGraphHookData(string name)
        {
            this.name = name;
        }
    }

    public class RPCGraphHookDataSerializedProperty
    {
        private readonly SerializedProperty prop;

        private readonly SerializedProperty _name;
        private readonly SerializedProperty _fullTypeName;
        private readonly SerializedProperty _methodName;

        public string name { get => _name.stringValue; set => _name.stringValue = value; }
        public string fullTypeName { get => _fullTypeName.stringValue; set => _fullTypeName.stringValue = value; }
        public string methodName { get => _methodName.stringValue; set => _methodName.stringValue = value; }

        public RPCGraphHookDataSerializedProperty(SerializedProperty prop)
        {
            this.prop = prop;
            _name = prop.FindPropertyRelative(nameof(RPCGraphHookData.name));
            _fullTypeName = prop.FindPropertyRelative(nameof(RPCGraphHookData.fullTypeName));
            _methodName = prop.FindPropertyRelative(nameof(RPCGraphHookData.methodName));
        }
    }
}