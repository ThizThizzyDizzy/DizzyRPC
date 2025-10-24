/*
 * Copyright (C) 2025 ThizThizzyDizzu (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System;
using System.Linq;
using DizzyRPC.Attribute;
using UnityEditor;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Editor.ProgramSources.UdonGraphProgram;
using VRC.Udon.Graph;

namespace DizzyRPC.Editor
{
    [InitializeOnLoad]
    public static class RPCGraphEditor
    {
        public static readonly RPCGraphDataStorage graphDataStorage;
        public static readonly SerializedRPCGraphDataStorage serializedGraphDataStorage;

        static RPCGraphEditor()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnPostHeaderGUI;
            graphDataStorage = AssetDatabase.LoadAssetAtPath<RPCGraphDataStorage>("Assets/DizzyRPC/RPCGraphData.asset");
            if (graphDataStorage == null)
            {
                graphDataStorage = ScriptableObject.CreateInstance<RPCGraphDataStorage>();
                AssetDatabase.CreateAsset(graphDataStorage, "Assets/DizzyRPC/RPCGraphData.asset");
            }

            serializedGraphDataStorage = new SerializedRPCGraphDataStorage(graphDataStorage);
        }

        public static string GetCustomEventName(this UdonNodeData eventNode)
        {
            if (!eventNode.fullName.StartsWith("Event_Custom")) return null; // Nasty string comparison, but that's what EventNodes does anyway, so par for the course I guess
            var line = eventNode.nodeValues[0].stringValue;
            if (string.IsNullOrEmpty(line)) return null;
            return line.Split("|", 2)[1];
        }

        private static void SaveRPCGraphData()
        {
            serializedGraphDataStorage.OnSave();
            serializedGraphDataStorage.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(graphDataStorage);
            RPCCompiler.OnGraphRPCSettingsChanged();
        }

        private static void OnPostHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.target is UdonGraphProgramAsset program)
            {
                serializedGraphDataStorage.Update();
                var data = serializedGraphDataStorage.GetGraphData(program);
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.LabelField("DizzyRPC Settings", EditorStyles.boldLabel);

                EditorGUILayout.LabelField($"{program.name} Routing Settings", EditorStyles.miniBoldLabel);
                data.singleton = GUILayout.Toggle(data.singleton, "Singleton");

                EditorGUILayout.BeginHorizontal();
                data.router = GUILayout.Toggle(data.router, "Router");
                if (data.router)
                {
                    var backgroundColorWas = GUI.backgroundColor;

                    bool isValid = false;

                    string typeName = null;
                    foreach (var type in RPCCompiler.RoutableRPCContainers)
                    {
                        if (type == data.routerTypeName)
                        {
                            typeName = type;
                            isValid = true;
                        }
                    }

                    if (!isValid) GUI.backgroundColor = new Color(1.0f, 0f, 0f, 1.0f);

                    EditorGUI.BeginDisabledGroup(!data.singleton);
                    if (EditorGUILayout.DropdownButton(new GUIContent(isValid ? $"{typeName}" : $"Select a program to route RPCs to{(data.routerTypeName != "" ? $" (Invalid type: {data.routerTypeName})" : "")}"), FocusType.Keyboard))
                    {
                        var menu = new GenericMenu();
                        foreach (var type in RPCCompiler.RoutableRPCContainers)
                        {
                            menu.AddItem(new GUIContent($"{type}"), false, () =>
                            {
                                data.routerTypeName = type;
                                SaveRPCGraphData();
                            });
                        }

                        menu.ShowAsContext();
                    }

                    EditorGUI.EndDisabledGroup();
                    GUI.backgroundColor = backgroundColorWas;

                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Routing ID type");
                    if (EditorGUILayout.DropdownButton(new GUIContent($"{data.routerIdType?.FullName}"), FocusType.Keyboard))
                    {
                        var menu = new GenericMenu();
                        foreach (var type in RPCCompiler.fullySupportedTypes)
                        {
                            menu.AddItem(new GUIContent($"{type.FullName}"), false, () =>
                            {
                                data.routerIdType = type;
                                SaveRPCGraphData();
                            });
                        }

                        menu.ShowAsContext();
                    }
                }

                EditorGUILayout.EndHorizontal();

                if (data.router && data.routerIdType == null) EditorGUILayout.HelpBox("Routing ID type must be set!", MessageType.Error);
                if (data.router && !data.singleton) EditorGUILayout.HelpBox("A router must also be a singleton!", MessageType.Error);
                if (!data.singleton && data.rpcMethods.Length > 0)
                {
                    bool hasRouter = false;
                    foreach (var router in RPCCompiler.Routers)
                    {
                        if (router.routableType == typeof(UdonBehaviour) && router.routableGraphName == program.name)
                        {
                            hasRouter = true;
                        }
                    }

                    if (!hasRouter) EditorGUILayout.HelpBox("This program is not a singleton, and has no router!", MessageType.Error);
                }

                EditorGUILayout.Separator();
                EditorGUILayout.LabelField("RPC Methods", EditorStyles.miniBoldLabel);
                foreach (var method in data.rpcMethods)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    method.name = EditorGUILayout.TextField("Name", method.name);
                    if (GUILayout.Button("Remove"))
                    {
                        data.RemoveRPCMethod(method);
                        SaveRPCGraphData();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    method.rateLimitPerSecond = GUILayout.Toggle(method.rateLimitPerSecond > -1, "Rate Limit Per second") ? Mathf.Max(0, method.rateLimitPerSecond) : -1;
                    if (method.rateLimitPerSecond > -1) method.rateLimitPerSecond = EditorGUILayout.IntField(method.rateLimitPerSecond);
                    GUILayout.EndHorizontal();
                    method.enforceSecure = GUILayout.Toggle(method.enforceSecure, "Enforce Secure");
                    method.allowDropping = GUILayout.Toggle(method.allowDropping, "Allow Dropping");
                    method.requireLowLatency = GUILayout.Toggle(method.requireLowLatency, "Require Low Latency");
                    method.ignoreDuplicates = GUILayout.Toggle(method.ignoreDuplicates, "Ignore Duplicates");
                    method.mode = (RPCSyncMode)EditorGUILayout.EnumPopup(new GUIContent("Sync Mode"), method.mode);

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("-"))
                    {
                        method.EnsureParameterCount(method.parameterNames.Length - 1);
                    }

                    if (GUILayout.Button("+"))
                    {
                        method.EnsureParameterCount(method.parameterNames.Length + 1);
                    }

                    EditorGUILayout.EndHorizontal();
                    for (int i = 0; i < method.parameterNames.Length; i++)
                    {
                        int idx = i;
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        method.parameterNames[i].stringValue = EditorGUILayout.TextField($"Parameter {i + 1}", method.parameterNames[i].stringValue);

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Type");
                        if (EditorGUILayout.DropdownButton(new GUIContent($"{Type.GetType(method.parameterTypes[i].stringValue)?.FullName}"), FocusType.Keyboard))
                        {
                            var menu = new GenericMenu();
                            foreach (var type in RPCCompiler.fullySupportedTypes)
                            {
                                menu.AddItem(new GUIContent($"{type.FullName}"), false, () =>
                                {
                                    method.parameterTypes[idx].stringValue = type.AssemblyQualifiedName;
                                    SaveRPCGraphData();
                                });
                            }

                            menu.ShowAsContext();
                        }

                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                    }

                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.Space();
                if (GUILayout.Button("Add RPC Method"))
                {
                    string newMethodName = $"NewRPCMethod";
                    int i = 0;
                    while (data.rpcMethods.Any(method => method.name == newMethodName)) newMethodName = $"NewRPCMethod_{i++}";
                    data.AddRPCMethod(newMethodName);
                    SaveRPCGraphData();
                }

                EditorGUILayout.Separator();
                if (data.rpcHooks.Length > 0 || data.singleton)
                {
                    EditorGUILayout.LabelField("RPC Hooks", EditorStyles.miniBoldLabel);
                    foreach (var hook in data.rpcHooks)
                    {
                        var originalColor = GUI.backgroundColor;

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.BeginHorizontal();
                        hook.name = EditorGUILayout.TextField("Name", hook.name);
                        if (GUILayout.Button("Remove"))
                        {
                            data.RemoveRPCHook(hook);
                            SaveRPCGraphData();
                            break;
                        }

                        EditorGUILayout.EndHorizontal();

                        if (!data.singleton) EditorGUILayout.HelpBox("RPC Hooks can only be defined in Singletons!", MessageType.Error);

                        bool isValid = false;

                        string typeName = null;
                        RPCCompiler.GeneratedRPC selectedRPC = null;
                        foreach (var rpc in RPCCompiler.RPCs)
                        {
                            if (hook.fullTypeName == rpc.TypeName || hook.fullTypeName == rpc.FullTypeName)
                            {
                                typeName = rpc.TypeName;
                                if (hook.methodName == rpc.methodName)
                                {
                                    isValid = true;
                                    selectedRPC = rpc;
                                }
                            }
                        }

                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label("Hook RPC:");
                        if (!isValid) GUI.backgroundColor = new Color(1.0f, 0f, 0f, 1.0f);
                        if (EditorGUILayout.DropdownButton(new GUIContent(isValid ? $"{typeName}.{hook.methodName}" : $"Select an RPC to Hook into{(hook.fullTypeName != "" ? $" (Invalid RPC: {hook.fullTypeName}.{hook.methodName})" : "")}"), FocusType.Keyboard))
                        {
                            var menu = new GenericMenu();
                            foreach (var rpc in RPCCompiler.RPCs)
                            {
                                menu.AddItem(new GUIContent($"{rpc.TypeName}.{rpc.methodName}"), false, () =>
                                {
                                    hook.fullTypeName = rpc.FullTypeName;
                                    hook.methodName = rpc.methodName;
                                    SaveRPCGraphData();
                                });
                            }

                            menu.ShowAsContext();
                        }

                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        GUI.backgroundColor = originalColor;
                    }
                }

                if (data.rpcHooks.Length > 0 || data.singleton)
                {
                    EditorGUILayout.Space();
                    if (GUILayout.Button("Add RPC Hook"))
                    {
                        string newHookName = $"NewRPCHook";
                        int i = 0;
                        while (data.rpcMethods.Any(method => method.name == newHookName)) newHookName = $"NewRPCHook_{i++}";
                        data.AddRPCHook(newHookName);
                        SaveRPCGraphData();
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    SaveRPCGraphData();
                }

                EditorGUILayout.Separator();
            }
        }
    }
}