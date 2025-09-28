using System;
using System.Collections.Generic;
using DizzyRPC.Attribute;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;
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
        
        // All types that can be used for VRC custom network events
        private static readonly Type[] validRouterIdTypes = { typeof(short), typeof(ushort), typeof(char), typeof(sbyte), typeof(byte), typeof(long), typeof(ulong), typeof(double), typeof(bool), typeof(float), typeof(int), typeof(uint), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion), typeof(Color), typeof(Color32), typeof(short[]), typeof(ushort[]), typeof(char[]), typeof(sbyte[]), typeof(byte[]), typeof(long[]), typeof(ulong[]), typeof(double[]), typeof(bool[]), typeof(float[]), typeof(int[]), typeof(uint[]), typeof(Vector2[]), typeof(Vector3[]), typeof(Vector4[]), typeof(Quaternion[]), typeof(Color[]), typeof(Color32[]), typeof(string), typeof(VRCUrl), typeof(VRCUrl[]), typeof(string[]) };

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

                    if(!isValid) GUI.backgroundColor = new Color(1.0f, 0f, 0f, 1.0f);

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
                        foreach (var type in validRouterIdTypes)
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
                
                if (data.router&&data.routerIdType==null) EditorGUILayout.HelpBox("Routing ID type must be set!", MessageType.Error);
                if (data.router&&!data.singleton) EditorGUILayout.HelpBox("A router must also be a singleton!", MessageType.Error);
                if (!data.singleton && data.rpcMethods.Length>0)
                {
                    bool hasRouter = false;
                    foreach (var router in RPCCompiler.Routers)
                    {
                        if (router.routableType == typeof(UdonBehaviour) && router.routableGraphName == program.name)
                        {
                            hasRouter = true;
                        }
                    }
                    if(!hasRouter) EditorGUILayout.HelpBox("This program is not a singleton, and has no router!", MessageType.Error);
                }

                List<string> existingEventNames = new();
                foreach (var eventNode in program.graphData.EventNodes)
                {
                    var name = eventNode.GetCustomEventName();
                    if(name!=null)existingEventNames.Add(name);
                }

                EditorGUILayout.Separator();
                EditorGUILayout.LabelField("RPC Methods", EditorStyles.miniBoldLabel);
                foreach (var method in data.rpcMethods)
                {
                    var originalColor = GUI.backgroundColor;

                    if (!existingEventNames.Contains(method.name)) GUI.backgroundColor = new Color(1.0f, 0f, 0f, 1.0f);

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(method.name);
                    if (GUILayout.Button("Remove"))
                    {
                        data.RemoveRPCMethod(method);
                        SaveRPCGraphData();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    if (!method.name.StartsWith("_"))
                    {
                        EditorGUILayout.HelpBox("RPCMethod name must start with _!", MessageType.Error);
                    }

                    GUILayout.BeginHorizontal();
                    method.rateLimitPerSecond = GUILayout.Toggle(method.rateLimitPerSecond > -1, "Rate Limit Per second") ? Mathf.Max(0, method.rateLimitPerSecond) : -1;
                    if (method.rateLimitPerSecond > -1) method.rateLimitPerSecond = EditorGUILayout.IntField(method.rateLimitPerSecond);
                    GUILayout.EndHorizontal();
                    method.enforceSecure = GUILayout.Toggle(method.enforceSecure, "Enforce Secure");
                    method.allowDropping = GUILayout.Toggle(method.allowDropping, "Allow Dropping");
                    method.requireLowLatency = GUILayout.Toggle(method.requireLowLatency, "Require Low Latency");
                    method.ignoreDuplicates = GUILayout.Toggle(method.ignoreDuplicates, "Ignore Duplicates");
                    method.mode = (RPCSyncMode)EditorGUILayout.EnumPopup(new GUIContent("Sync Mode"), method.mode);
                    EditorGUILayout.EndVertical();
                    GUI.backgroundColor = originalColor;
                }

                bool canAddMethod = false;
                foreach (var name in existingEventNames)
                {
                    bool exists = false;
                    foreach (var method in data.rpcMethods) exists |= method.name == name;
                    canAddMethod |= !exists;
                }

                EditorGUILayout.Space();
                EditorGUI.BeginDisabledGroup(!canAddMethod);
                if (EditorGUILayout.DropdownButton(new GUIContent("Add RPC Method"), FocusType.Keyboard))
                {
                    var menu = new GenericMenu();
                    foreach (var eventNode in program.graphData.EventNodes)
                    {
                        if (eventNode.nodeValues.Length < 1) continue;
                        string eventName = eventNode.GetCustomEventName();
                        if (eventName == null) continue;

                        bool alreadyExists = false;
                        foreach (var method in data.rpcMethods) alreadyExists |= method.name == eventName;
                        if (alreadyExists) continue;

                        menu.AddItem(new GUIContent(eventName), false, () =>
                        {
                            data.AddRPCMethod(eventName);
                            SaveRPCGraphData();
                        });
                    }

                    menu.ShowAsContext();
                }

                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Separator();
                if (data.rpcHooks.Length > 0 || data.singleton)
                {
                    EditorGUILayout.LabelField("RPC Hooks", EditorStyles.miniBoldLabel);
                    foreach (var hook in data.rpcHooks)
                    {
                        var originalColor = GUI.backgroundColor;

                        if (!existingEventNames.Contains(hook.name) || !data.singleton) GUI.backgroundColor = new Color(1.0f, 0f, 0f, 1.0f);

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(hook.name);
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

                        if (selectedRPC != null)
                        {
                            foreach (var node in program.graphData.EventNodes)
                            {
                                if (node.GetCustomEventName() == hook.name)
                                {
                                    int parameterCount = int.Parse(node.fullName.Substring(13, 1));
                                    hook.EnsureParameterCount(parameterCount);
                                    for (int i = 0; i < parameterCount; i++)
                                    {
                                        var backgroundColorWas = GUI.backgroundColor;

                                        int index = i;
                                        var type = Type.GetType(node.nodeValues[i + 2].stringValue.Split("|", 2)[1]);
                                        string parameterName = hook.parameterNames[i].stringValue;
                                        bool parameterValid = false;

                                        foreach (var parameter in selectedRPC.methodParameters)
                                        {
                                            if (parameter.type == type && parameter.name == parameterName)
                                            {
                                                parameterValid = true;
                                            }
                                        }

                                        if (!parameterValid) GUI.backgroundColor = new Color(1.0f, 0f, 0f, 1.0f);

                                        EditorGUILayout.BeginHorizontal();
                                        EditorGUILayout.LabelField($"Parameter {i + 1} ({type.FullName})");
                                        if (EditorGUILayout.DropdownButton(new GUIContent(string.IsNullOrEmpty(parameterName) ? "(Unset)" : $"{parameterName}{(parameterValid ? "" : " (INVALID)")}"), FocusType.Keyboard))
                                        {
                                            var menu = new GenericMenu();
                                            foreach (var parameter in selectedRPC.methodParameters)
                                            {
                                                if (parameter.type == type)
                                                {
                                                    menu.AddItem(new GUIContent($"{parameter.name}"), false, () =>
                                                    {
                                                        hook.parameterNames[index].stringValue = parameter.name;
                                                        SaveRPCGraphData();
                                                    });
                                                }
                                            }

                                            menu.ShowAsContext();
                                        }

                                        EditorGUILayout.EndHorizontal();
                                        GUI.backgroundColor = backgroundColorWas;
                                    }
                                }
                            }
                        }

                        EditorGUILayout.EndVertical();
                        GUI.backgroundColor = originalColor;
                    }
                }

                bool canAddHook = false;
                foreach (var name in existingEventNames)
                {
                    bool exists = false;
                    foreach (var hook in data.rpcHooks) exists |= hook.name == name;
                    canAddHook |= !exists;
                }

                if (data.rpcHooks.Length > 0 || data.singleton)
                {
                    EditorGUILayout.Space();
                    EditorGUI.BeginDisabledGroup(!canAddHook);
                    if (EditorGUILayout.DropdownButton(new GUIContent("Add RPC Hook"), FocusType.Keyboard))
                    {
                        var menu = new GenericMenu();
                        foreach (var eventNode in program.graphData.EventNodes)
                        {
                            if (eventNode.nodeValues.Length < 1) continue;
                            string eventName = eventNode.GetCustomEventName();
                            if (eventName == null) continue;

                            bool alreadyExists = false;
                            foreach (var hook in data.rpcHooks) alreadyExists |= hook.name == eventName;
                            if (alreadyExists) continue;

                            menu.AddItem(new GUIContent(eventName), false, () =>
                            {
                                data.AddRPCHook(eventName);
                                SaveRPCGraphData();
                            });
                        }

                        menu.ShowAsContext();
                    }
                    EditorGUI.EndDisabledGroup();
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