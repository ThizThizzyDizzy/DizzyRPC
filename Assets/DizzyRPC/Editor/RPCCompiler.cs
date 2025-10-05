using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DizzyRPC.Attribute;
using DizzyRPC.Debugger;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Editor.ProgramSources.UdonGraphProgram;
using VRRefAssist;
using Assembly = System.Reflection.Assembly;

namespace DizzyRPC.Editor
{
    public static class RPCCompiler
    {
        private static readonly List<GeneratedSingleton> generatedSingletons = new();
        private static readonly List<GeneratedRouter> generatedRouters = new();
        private static readonly List<GeneratedRPC> generatedRPCs = new();
        private static readonly List<string> routableRPCContainers = new();

        public static readonly Type[] fullySupportedTypes = { typeof(short), typeof(ushort), typeof(char), typeof(sbyte), typeof(byte), typeof(long), typeof(ulong), typeof(double), typeof(bool), typeof(float), typeof(int), typeof(uint), typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion), typeof(Color), typeof(Color32), typeof(short[]), typeof(ushort[]), typeof(char[]), typeof(sbyte[]), typeof(byte[]), typeof(long[]), typeof(ulong[]), typeof(double[]), typeof(bool[]), typeof(float[]), typeof(int[]), typeof(uint[]), typeof(Vector2[]), typeof(Vector3[]), typeof(Vector4[]), typeof(Quaternion[]), typeof(Color[]), typeof(Color32[]), typeof(string), typeof(string[]) };

        private static bool hasCompiled = false;

        public static GeneratedSingleton[] Singletons
        {
            get
            {
                if (!hasCompiled) CompileRPCs();
                return generatedSingletons.ToArray();
            }
        }

        public static GeneratedRouter[] Routers
        {
            get
            {
                if (!hasCompiled) CompileRPCs();
                return generatedRouters.ToArray();
            }
        }

        public static GeneratedRPC[] RPCs
        {
            get
            {
                if (!hasCompiled) CompileRPCs();
                return generatedRPCs.ToArray();
            }
        }

        public static string[] RoutableRPCContainers
        {
            get
            {
                if (!hasCompiled) CompileRPCs();
                return routableRPCContainers.ToArray();
            }
        }

        private static bool needsRegen = false;

        [InitializeOnLoadMethod]
        private static void OnLoad()
        {
            EditorApplication.update += OnUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            if (RPCCompilerSettings.AutoRecompileInEditor && !EditorApplication.isPlayingOrWillChangePlaymode) needsRegen = true;
        }

        public static void OnGraphRPCSettingsChanged()
        {
            if (RPCCompilerSettings.AutoRecompileInEditor && !EditorApplication.isPlayingOrWillChangePlaymode) needsRegen = true;
        }

        private static void OnUpdate()
        {
            if (RPCCompilerSettings.AutoRecompileForPlayMode && EditorPrefs.GetBool("DizzyRPC_ContinueEnteringPlayMode"))
            {
                EditorPrefs.DeleteKey("DizzyRPC_ContinueEnteringPlayMode");
                EditorApplication.EnterPlaymode();
                return;
            }

            if (needsRegen)
            {
                Debug.Log("[DizzyRPC] Automatically recompiling DizzyRPC!");
                needsRegen = false;
                Compile(GenerationMode.Editor);
            }
        }

        [RunOnBuild]
        private static void OnBuild()
        {
            if (RPCCompilerSettings.AutoRecompileOnBuild)
            {
                Compile(GenerationMode.Build);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!RPCCompilerSettings.AutoRecompileForPlayMode) return;
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    if (Compile(GenerationMode.Build)) EditorPrefs.SetBool("DizzyRPC_ContinueEnteringPlayMode", true);
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    Compile(GenerationMode.Editor);
                    break;
            }
        }

        [MenuItem("Tools/DizzyRPC/Compile RPCs/Compile for Editor")]
        private static bool CompileEditor() => Compile(GenerationMode.Editor);

        [MenuItem("Tools/DizzyRPC/Compile RPCs/Compile for Build")]
        private static bool CompileBuild()
        {
            if (RPCCompilerSettings.AutoRecompileInEditor)
            {
                EditorUtility.DisplayDialog("DizzyRPC", "DizzyRPC is configured to automatically compile for editor when any changes are made.\nYou must disable that setting in order to manually compile for build.", "OK");
                return false;
            }

            return Compile(GenerationMode.Build);
        }

        [MenuItem("Tools/DizzyRPC/Compile RPCs/Remove all generated Code")]
        private static bool Clean()
        {
            if (!EditorUtility.DisplayDialog("DizzyRPC", $"Remove ALL code generated by DizzyRPC?\nThis will cause compilation errors if you are using any RPCs!{(RPCCompilerSettings.AutoRecompileInEditor || RPCCompilerSettings.AutoRecompileForPlayMode || RPCCompilerSettings.AutoRecompileOnBuild ? "\nThis will also disable ALL automatic RPC compilation settings." : "")}", "Yes", "No")) return false;
            RPCCompilerSettings.AutoRecompileInEditor = false;
            RPCCompilerSettings.AutoRecompileForPlayMode = false;
            RPCCompilerSettings.AutoRecompileOnBuild = false;
            return Compile(GenerationMode.Clean);
        }

        private static bool Compile(GenerationMode mode)
        {
            if (mode == GenerationMode.Clean)
            {
                generatedSingletons.Clear();
                generatedRouters.Clear();
                generatedRPCs.Clear();
                routableRPCContainers.Clear();
            }
            else
            {
                CompileRPCs();
            }

            bool anySharpChanges = false;

            try
            {
                AssetDatabase.StartAssetEditing();

                anySharpChanges |= GenerateRPCs(typeof(RPCChannel), FindMonoAssetPath(typeof(RPCChannel)), GenerationTarget.Channel, mode);

                foreach (var type in Assembly.GetAssembly(typeof(RPCMethodAttribute)).GetTypes().Where((type) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any((method) => method.GetCustomAttribute<RPCMethodAttribute>() != null)))
                {
                    anySharpChanges |= GenerateRPCs(type, FindMonoAssetPath(type), GenerationTarget.MethodContainer, mode);
                }

                foreach (var graph in RPCGraphEditor.graphDataStorage.graphData)
                {
                    var program = AssetDatabase.LoadAssetAtPath<UdonGraphProgramAsset>(AssetDatabase.GUIDToAssetPath(graph.guid));
                    if (program == null)
                    {
                        Debug.LogWarning($"[DizzyRPC] Skipping graph {graph.guid} because no matching Udon Graph Program Asset was found.");
                        continue;
                    }

                    GenerateRPCs(graph.guid, program, mode);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            return anySharpChanges;
        }


        private static string FindMonoAssetPath(Type type)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
                if (monoScript != null && monoScript.GetClass() == type)
                {
                    return assetPath;
                }
            }

            throw new Exception($"Could not find MonoScript asset for type {type.FullName}!");
        }

        private static void CompileRPCs()
        {
            hasCompiled = true;

            generatedSingletons.Clear();
            generatedRouters.Clear();
            generatedRPCs.Clear();
            routableRPCContainers.Clear();

            // Generate the RPC code
            var assembly = Assembly.GetAssembly(typeof(RPCMethodAttribute));
            List<Type> singletonTypes = new();
            List<Type> types = new(assembly.GetTypes().Where((t) => t.IsClass && !t.IsAbstract));

            var graphDataObject = AssetDatabase.LoadAssetAtPath<RPCGraphDataStorage>("Assets/DizzyRPC/RPCGraphData.asset");

            // Routable RPC Containers (These must be first to ensure they are set even if compilation fails)
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var rpcMethod = method.GetCustomAttribute<RPCMethodAttribute>();
                    if (rpcMethod == null) continue;

                    bool isSingleton = false;
                    // Check for VRRefAssist Singleton attribute
                    foreach (var att in type.GetCustomAttributes())
                    {
                        var typ = att.GetType();
                        if (typ.FullName == "VRRefAssist.Singleton")
                        {
                            isSingleton = true;
                            break;
                        }
                    }

                    if (isSingleton) continue;

                    routableRPCContainers.Add(type.FullName);
                }
            }

            // Udon Graph Routable RPC Containers
            foreach (var graph in RPCGraphEditor.graphDataStorage.graphData)
            {
                if (graph.rpcMethods.Count > 0 && !graph.singleton)
                {
                    var program = AssetDatabase.LoadAssetAtPath<UdonGraphProgramAsset>(AssetDatabase.GUIDToAssetPath(graph.guid));
                    routableRPCContainers.Add(program.name);
                }
            }

            // Routers
            foreach (var type in types)
            {
                if (IsDerivedFromGeneric(type, typeof(RPCRouter<,>), out var actualBase))
                {
                    var genericArgs = actualBase.GetGenericArguments();
                    generatedRouters.Add(new GeneratedRouter()
                    {
                        id = generatedRouters.Count,
                        routerType = type,
                        routableType = genericArgs[0],
                        idType = genericArgs[1]
                    });
                }

                // U# routers routing Udon Graph RPCs
                if (IsDerivedFromGeneric(type, typeof(RPCRouter<>), out var actualBaseGraph))
                {
                    var attr = type.GetCustomAttribute<RPCGraphRouterAttribute>();
                    if (attr == null) throw new Exception($"[DizzyRPC] RPC Router {type.FullName} Must have an [RPCGraphRouter] attribute!");
                    var genericArgs = actualBaseGraph.GetGenericArguments();
                    generatedRouters.Add(new GeneratedRouter()
                    {
                        id = generatedRouters.Count,
                        routerType = type,
                        routableType = typeof(UdonBehaviour),
                        routableGraphName = attr.name,
                        idType = genericArgs[0]
                    });
                }
            }

            // Udon Graph Routers
            foreach (var graph in graphDataObject.graphData)
            {
                if (!graph.router) continue;

                var path = AssetDatabase.GUIDToAssetPath(graph.guid);
                if (path == "") continue;

                UdonGraphProgramAsset program = AssetDatabase.LoadAssetAtPath<UdonGraphProgramAsset>(path);

                Type routableType = typeof(UdonBehaviour);
                foreach (var type in types)
                {
                    if (type.FullName == graph.routerTypeName) routableType = type;
                }

                generatedRouters.Add(new GeneratedRouter()
                {
                    id = generatedRouters.Count,
                    routerType = typeof(UdonBehaviour),
                    routableType = routableType,
                    routableGraphName = graph.routerTypeName,
                    idType = graph.RouterIdType
                });
            }

            // Singletons & RPCs
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var rpcMethod = method.GetCustomAttribute<RPCMethodAttribute>();
                    if (rpcMethod == null) continue;

                    if (method.GetCustomAttribute<NetworkCallableAttribute>() != null) throw new Exception($"[DizzyRPC] RPCMethod {type.FullName}.{method.Name} must not have the NetworkCallable attribute!");
                    if (!method.IsPublic) throw new Exception($"[DizzyRPC] RPCMethod {type.FullName}.{method.Name} must be public!");
                    if (!method.Name.StartsWith("_")) throw new Exception($"[DizzyRPC] The name of RPCMethod {type.FullName}.{method.Name} must begin with _!");

                    List<GeneratedRPCParameter> parameters = new();
                    foreach (var param in method.GetParameters()) parameters.Add(new(param));

                    var mode = rpcMethod.CalculateMode();

                    bool generated = false;

                    // Check for VRRefAssist Singleton attribute
                    foreach (var att in type.GetCustomAttributes())
                    {
                        var typ = att.GetType();
                        if (typ.FullName == "VRRefAssist.Singleton")
                        {
                            if (!singletonTypes.Contains(type))
                            {
                                generatedSingletons.Add(new GeneratedSingleton()
                                {
                                    id = generatedSingletons.Count,
                                    type = type
                                });
                                singletonTypes.Add(type);
                            }

                            generatedRPCs.Add(new GeneratedRPC()
                            {
                                id = (ushort)generatedRPCs.Count,
                                isUniqueType = types.Count((t) => t.Name == type.Name) == 1,
                                methodName = method.Name,
                                methodParameters = parameters,
                                singleton = generatedSingletons[singletonTypes.IndexOf(type)],
                                type = type,
                                mode = mode,
                                ignoreDuplicates = rpcMethod.IgnoreDuplicates
                            });
                            generated = true;
                            break;
                        }
                    }

                    if (generated) continue;

                    // Check for RPC router
                    foreach (var router in generatedRouters)
                    {
                        if (router.routableType == type)
                        {
                            string routedParameterName = "_id";
                            while (parameters.Any(p => p.name == routedParameterName)) routedParameterName = $"_{routedParameterName}";
                            var routerParameter = new GeneratedRPCParameter()
                            {
                                name = routedParameterName,
                                type = router.idType
                            };
                            var rpc = new GeneratedRPC()
                            {
                                id = (ushort)generatedRPCs.Count,
                                isUniqueType = types.Count((t) => t.Name == type.Name) == 1,
                                methodName = method.Name,
                                methodParameters = parameters,
                                routerParameter = routerParameter,
                                router = router,
                                type = type,
                                mode = mode,
                                ignoreDuplicates = rpcMethod.IgnoreDuplicates
                            };
                            generatedRPCs.Add(rpc);
                            generated = true;
                            break;
                        }
                    }

                    if (generated) continue;

                    throw new Exception($"Could not map RPCs to {method.Name} in {type.Name}!");
                }
            }

            // Udon Graph Singletons & RPCs
            foreach (var graph in graphDataObject.graphData)
            {
                var path = AssetDatabase.GUIDToAssetPath(graph.guid);
                if (path == "") continue;

                GeneratedSingleton singleton = null;
                if (graph.singleton)
                {
                    generatedSingletons.Add(singleton = new GeneratedSingleton()
                    {
                        id = generatedSingletons.Count,
                        type = typeof(UdonBehaviour),
                        udonGraphGuid = graph.guid
                    });
                }

                UdonGraphProgramAsset program = AssetDatabase.LoadAssetAtPath<UdonGraphProgramAsset>(path);

                foreach (var method in graph.rpcMethods)
                {
                    List<GeneratedRPCParameter> parameters = new();
                    for (int i = 0; i < method.parameterNames.Length; i++)
                    {
                        parameters.Add(new GeneratedRPCParameter()
                        {
                            name = method.parameterNames[i],
                            type = Type.GetType(method.parameterTypes[i])
                        });
                    }

                    var mode = method.CalculateMode();

                    bool generated = false;

                    if (graph.singleton)
                    {
                        generatedRPCs.Add(new GeneratedRPC()
                        {
                            id = (ushort)generatedRPCs.Count,
                            methodName = method.name,
                            methodParameters = parameters,
                            singleton = singleton,
                            type = typeof(UdonBehaviour),
                            graphName = program.name,
                            mode = mode,
                            ignoreDuplicates = method.ignoreDuplicates
                        });
                        generated = true;
                    }

                    if (generated) continue;

                    // Check for RPC router
                    foreach (var router in generatedRouters)
                    {
                        if (router.routableType == typeof(UdonBehaviour) && router.routableGraphName == program.name)
                        {
                            string routedParameterName = "_id";
                            while (parameters.Any(p => p.name == routedParameterName)) routedParameterName = $"_{routedParameterName}";
                            var routerParameter = new GeneratedRPCParameter()
                            {
                                name = routedParameterName,
                                type = router.idType
                            };
                            var rpc = new GeneratedRPC()
                            {
                                id = (ushort)generatedRPCs.Count,
                                methodName = method.name,
                                methodParameters = parameters,
                                routerParameter = routerParameter,
                                router = router,
                                type = typeof(UdonBehaviour),
                                graphName = program.name,
                                mode = mode,
                                ignoreDuplicates = method.IgnoreDuplicates
                            };
                            generatedRPCs.Add(rpc);
                            generated = true;
                            break;
                        }
                    }

                    if (generated) continue;

                    throw new Exception($"Could not map RPCs to {method.name} in {program.name}!");
                }
            }

            // RPC Hooks
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var hook = method.GetCustomAttribute<RPCHookAttribute>();
                    if (hook == null) continue;

                    if (method.ReturnType != typeof(bool)) throw new Exception($"RPCHook {type}.{method.Name} return type must be bool!");

                    bool found = false;
                    foreach (var rpc in generatedRPCs)
                    {
                        if (hook.FullTypeName == rpc.FullTypeName && hook.methodName == rpc.methodName)
                        {
                            found = true;

                            List<GeneratedRPCParameter> hookParameters = new List<GeneratedRPCParameter>();

                            foreach (var parameter in method.GetParameters())
                            {
                                hookParameters.Add(new GeneratedRPCParameter()
                                {
                                    type = parameter.ParameterType,
                                    name = parameter.Name
                                });
                                if (!rpc.methodParameters.Any(param => param.name == parameter.Name && param.type == parameter.ParameterType))
                                {
                                    throw new Exception($"Cannot compile RPC hook {type.FullName}.{method.Name} for {hook.FullTypeName}.{hook.methodName}! Parameter {parameter.Name} of type {parameter.ParameterType.FullName} is not present on the target RPC method! RPC hooks must only use parameters that exist on the RPC method.");
                                }
                            }

                            GeneratedSingleton hookSingleton = null;
                            foreach (var singleton in generatedSingletons)
                            {
                                if (singleton.type == type)
                                {
                                    hookSingleton = singleton;
                                    break;
                                }
                            }

                            if (hookSingleton == null)
                            {
                                generatedSingletons.Add(hookSingleton = new GeneratedSingleton()
                                {
                                    id = generatedSingletons.Count,
                                    type = type
                                });
                            }

                            rpc.hooks.Add(new GeneratedRPCHook() { rpc = rpc, singleton = hookSingleton, methodName = method.Name, methodParameters = hookParameters });
                            break;
                        }
                    }

                    if (!found) throw new Exception($"[DizzyRPC] Could not generate RPC hook: {hook.type.FullName}.{hook.methodName}! (No matching RPC was found)");
                }
            }

            // Udon Graph RPC Hooks
            foreach (var graph in graphDataObject.graphData)
            {
                var path = AssetDatabase.GUIDToAssetPath(graph.guid);
                if (path == "") continue;

                GeneratedSingleton hookSingleton = null;
                foreach (var singleton in generatedSingletons)
                {
                    if (singleton.type == typeof(UdonBehaviour))
                    {
                        hookSingleton = singleton;
                        break;
                    }
                }

                UdonGraphProgramAsset program = AssetDatabase.LoadAssetAtPath<UdonGraphProgramAsset>(path);

                foreach (var hook in graph.rpcHooks)
                {
                    bool found = false;
                    foreach (var rpc in generatedRPCs)
                    {
                        if (hook.fullTypeName == rpc.FullTypeName && hook.methodName == rpc.methodName)
                        {
                            found = true;

                            if (hookSingleton == null) throw new Exception($"Cannot compile RPC hook {hook.methodName}! Udon Graph program {program.name} is not a singleton!");

                            rpc.hooks.Add(new GeneratedRPCHook()
                            {
                                rpc = rpc,
                                singleton = hookSingleton, 
                                methodName = hook.name,
                                methodParameters = rpc.AllParameters
                            });
                        }
                    }

                    if (!found) throw new Exception($"[DizzyRPC] Could not generate RPC hook {program.name}.{hook.name} for {hook.fullTypeName}.{hook.methodName}! (No matching RPC was found)");
                }
            }
        }

        private static bool IsDerivedFromGeneric(Type type, Type genericBase, out Type actualBase)
        {
            while ((type = type.BaseType) != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == genericBase)
                {
                    actualBase = type;
                    return true;
                }
            }

            actualBase = default;
            return false;
        }

        private static bool GenerateRPCs(Type type, string path, GenerationTarget target, GenerationMode mode)
        {
            List<string> generatedLines = new();

            if (mode != GenerationMode.Clean)
            {
                switch (target)
                {
                    case GenerationTarget.Channel:
                        foreach (GeneratedRPC rpc in generatedRPCs)
                        {
                            generatedLines.Add($"public const int {rpc.SharpIdConstName} = {rpc.id};");
                        }

                        generatedLines.Add("");
                        if (mode != GenerationMode.Build)
                        {
                            generatedLines.Add("private void _DecodeRPC(int id, byte[] data) {}");
                            break;
                        }

                        foreach (GeneratedSingleton singleton in generatedSingletons)
                        {
                            generatedLines.Add($"[{typeof(SerializeField).FullName}] private {singleton.type.FullName} {singleton.SharpFieldName};");
                        }

                        generatedLines.Add("");
                        foreach (GeneratedRouter router in generatedRouters)
                        {
                            generatedLines.Add($"[{typeof(SerializeField).FullName}] private {router.routerType.FullName} {router.SharpFieldName};");
                        }

                        generatedLines.Add("");
                        generatedLines.Add("private void _DecodeRPC(int id, byte[] data) {");
                        generatedLines.Add("    switch(id){");
                        foreach (GeneratedRPC rpc in generatedRPCs)
                        {
                            if (rpc.mode != RPCSyncMode.Variable) continue;
                            generatedLines.Add($"        case {rpc.id}: _DecodeRPC_{rpc.id}(data); break;");
                        }

                        generatedLines.Add("    }");
                        generatedLines.Add("}");
                        generatedLines.Add("");


                        foreach (GeneratedRPC rpc in generatedRPCs)
                        {
                            foreach (var parameter in rpc.AllParameters)
                            {
                                generatedLines.Add($"private {parameter.SharpParameterType} {parameter.SharpFieldName(rpc)};");
                            }

                            switch (rpc.mode)
                            {
                                case RPCSyncMode.Event:
                                    generatedLines.Add($"[{typeof(NetworkCallableAttribute).Namespace}.NetworkCallable]");
                                    generatedLines.Add($"public void RPC_{rpc.id}({rpc.AllParameters.AsSharpMethodParameters()}) {{");
                                    foreach (var param in rpc.AllParameters)
                                    {
                                        generatedLines.Add($"    {param.SharpFieldName(rpc)} = {param.SharpParameterName};");
                                    }

                                    break;
                                case RPCSyncMode.Variable:
                                    generatedLines.Add($"private void _DecodeRPC_{rpc.id}(byte[] data) {{");
                                    generatedLines.Add($"    int _data_position = 0;");
                                    foreach (var param in rpc.AllParameters)
                                    {
                                        generatedLines.Add($"    {param.SharpFieldName(rpc)} = Decode{param.type.Name.Replace("[]", "Array")}(data, ref _data_position);");
                                    }

                                    break;
                                default:
                                    throw new Exception($"Invalid RPCSyncMode at generation time! ({(int)rpc.mode} - {Enum.GetName(typeof(RPCSyncMode), rpc.mode)})");
                            }

                            generatedLines.Add($"    debugger.{nameof(RPCDebugger._OnReceiveRPC)}({typeof(NetworkCalling).FullName}.{nameof(NetworkCalling.CallingPlayer)}, {rpc.id}{rpc.AllParameters.AsSharpCallParameters(rpc, ", ")});");
                            foreach (var hook in rpc.hooks)
                            {
                                if (hook.singleton.type == typeof(UdonBehaviour))
                                {
                                    generatedLines.Add($"    {hook.singleton.SharpFieldName}.SetProgramVariable(\"_RPC_PostHook\", nameof({hook.SharpPostHookMethodName}));");
                                    foreach (var parameter in hook.methodParameters)
                                    {
                                        generatedLines.Add($"    {hook.singleton.SharpFieldName}.SetProgramVariable(\"{parameter.GraphParameterName(hook)}\", {parameter.SharpFieldName(rpc)});");
                                    }

                                    generatedLines.Add($"    {hook.singleton.SharpFieldName}.SendCustomEvent(\"{hook.GraphMethodName}\");");
                                    generatedLines.Add($"}}");
                                    generatedLines.Add($"public void {hook.SharpPostHookMethodName}() {{");
                                }
                                else
                                {
                                    generatedLines.Add($"    if (!{hook.singleton.SharpFieldName}.{hook.SharpMethodName}({hook.methodParameters.AsSharpCallParameters(rpc)})) return;");
                                }
                            }

                            if (rpc.singleton != null)
                            {
                                if (rpc.type == typeof(UdonBehaviour))
                                {
                                    foreach (var param in rpc.methodParameters)
                                    {
                                        generatedLines.Add($"    {rpc.singleton.SharpFieldName}.SetProgramVariable(\"{param.GraphParameterName(rpc)}\", {param.SharpFieldName(rpc)});");
                                    }

                                    generatedLines.Add($"    {rpc.singleton.SharpFieldName}.SendCustomEvent(\"{rpc.GraphMethodName}\");");
                                }
                                else
                                {
                                    generatedLines.Add($"    {rpc.singleton.SharpFieldName}.{rpc.SharpMethodName}({rpc.methodParameters.AsSharpCallParameters(rpc)});");
                                }
                            }

                            if (rpc.router != null)
                            {
                                if (rpc.router.routerType == typeof(UdonBehaviour))
                                {
                                    generatedLines.Add($"    {rpc.router.SharpFieldName}.SetProgramVariable(\"_RPC_PostRoute\", nameof({rpc.router.SharpPostRouteMethodName(rpc)}));");
                                    generatedLines.Add($"    {rpc.router.SharpFieldName}.SetProgramVariable(\"{rpc.routerParameter.GraphParameterName(rpc.router)}\", {rpc.routerParameter.SharpFieldName(rpc)});");
                                    generatedLines.Add($"    {rpc.router.SharpFieldName}.SendCustomEvent(\"{rpc.router.GraphMethodName}\");");
                                    generatedLines.Add($"}}");
                                    if (rpc.router != null && rpc.router.routerType == typeof(UdonBehaviour))
                                    {
                                        var routedBaseType = rpc.router.routableType == typeof(UdonBehaviour) ? typeof(UdonBehaviour) : typeof(GameObject);
                                        generatedLines.Add($"public {routedBaseType.FullName} {rpc.router.SharpRoutedFieldName(rpc)};");
                                    }

                                    generatedLines.Add($"public void {rpc.router.SharpPostRouteMethodName(rpc)}() {{");
                                    if (rpc.router.routableType == typeof(UdonBehaviour))
                                    {
                                        generatedLines.Add($"    var routed = {rpc.router.SharpRoutedFieldName(rpc)};");
                                    }
                                    else
                                    {
                                        generatedLines.Add($"    var routed = {rpc.router.SharpRoutedFieldName(rpc)}.GetComponent<{rpc.router.routableType.FullName}>();");
                                    }
                                }
                                else
                                {
                                    generatedLines.Add($"    var routed = {rpc.router.SharpFieldName}._Route({rpc.routerParameter.SharpFieldName(rpc)});");
                                }

                                if (rpc.router.routableType == typeof(UdonBehaviour))
                                {
                                    foreach (var param in rpc.methodParameters)
                                    {
                                        generatedLines.Add($"    routed.SetProgramVariable(\"{param.GraphParameterName(rpc)}\", {param.SharpFieldName(rpc)});");
                                    }

                                    generatedLines.Add($"    routed.SendCustomEvent(\"{rpc.GraphMethodName}\");");
                                }
                                else
                                {
                                    generatedLines.Add($"    routed.{rpc.SharpMethodName}({rpc.methodParameters.AsSharpCallParameters(rpc)});");
                                }
                            }

                            generatedLines.Add($"}}");
                        }

                        break;
                    case GenerationTarget.MethodContainer:
                        generatedLines.Add($"[{typeof(SerializeField).FullName}] private {typeof(RPCManager).FullName} _rpc_manager;");
                        if (mode == GenerationMode.Build)
                        {
                            foreach (GeneratedRouter router in generatedRouters)
                            {
                                if (router.routableType == type) generatedLines.Add($"[{typeof(SerializeField).FullName}] private {router.routerType.FullName} _rpc_{router.SharpFieldName};");
                            }
                        }

                        generatedLines.Add("");

                        foreach (GeneratedRPC rpc in generatedRPCs)
                        {
                            var modeName = rpc.mode switch
                            {
                                RPCSyncMode.Event => "Event",
                                RPCSyncMode.Variable => "Variable",
                                _ => ""
                            };

                            if (rpc.router != null && rpc.router.routableType != type) continue;
                            if (rpc.singleton != null && rpc.singleton.type != type) continue;

                            generatedLines.Add($"public void _SendRPC{rpc.methodName}({typeof(VRCPlayerApi).FullName} target{rpc.methodParameters.AsSharpMethodParameters(", ")}) {{");
                            if (mode == GenerationMode.Build)
                            {
                                switch (rpc.mode)
                                {
                                    case RPCSyncMode.Event:
                                        generatedLines.Add($"    _rpc_manager._SendEvent(target, {typeof(RPCChannel).FullName}.{rpc.SharpIdConstName}{rpc.methodParameters.AsSharpCallParameters(null, ", ")});");
                                        break;
                                    case RPCSyncMode.Variable:
                                        generatedLines.Add($"    _rpc_manager._SendVariable(target, {typeof(RPCChannel).FullName}.{rpc.SharpIdConstName}, {$"{rpc.ignoreDuplicates}".ToLower()}{rpc.methodParameters.AsSharpCallParameters(null, ", ")});");
                                        generatedLines.Add($"    if(target==null||target=={typeof(Networking).FullName}.{nameof(Networking.LocalPlayer)}){rpc.methodName}({rpc.methodParameters.AsSharpCallParameters(null)});"); // Ensure local calls are not forgotten for variables
                                        break;
                                    default: throw new Exception($"Invalid RPC mode at compile time! ({rpc.mode} - {Enum.GetName(typeof(RPCSyncMode), rpc.mode)})");
                                }
                            }

                            generatedLines.Add($"}}");
                        }

                        break;
                    default:
                        throw new Exception("Unrecognized generation mode: " + Enum.GetName(typeof(GenerationTarget), target));
                }
            }

            var newText = RegenerateFile(path, generatedLines);
            if (newText == null)
            {
                throw new Exception($"Could not regenerate RPCs in file: {path}!");
            }

            var oldText = File.ReadAllText(path);
            if (oldText == newText)
            {
                Debug.Log($"[DizzyRPC] Skipping regeneration of file: {path} - File has not changed.");
                return false;
            }

            Debug.Log($"[DizzyRPC] Regenerating contents of file: {path}");

            File.WriteAllText(path, newText);
            return true;
        }

        private static void GenerateRPCs(string guid, UdonGraphProgramAsset program, GenerationMode mode)
        {
            var generator = new UdonGraphGen(program);
            if (mode == GenerationMode.Clean)
            {
                foreach (var v in generator.Variables.Where(v => v.Name.StartsWith($"_RPC"))) v.Delete();
            }
            else
            {
                foreach (var v in generator.Variables.Where(v => v.Name.StartsWith($"_RPC"))) v.Delete();

                int x = 10;
                int y = 0;
                foreach (GeneratedRPC rpc in generatedRPCs)
                {
                    if (rpc.type == typeof(UdonBehaviour) && rpc.graphName == program.name)
                    {
                        generator.AddCustomEvent(rpc.GraphMethodName, position: new(x, y++));

                        Dictionary<GeneratedRPCParameter, UdonGraphGenVariable> graphVariables = new();
                        foreach (var parameter in rpc.methodParameters)
                        {
                            graphVariables[parameter] = generator.AddVariable(parameter.type, $"{parameter.GraphParameterName(rpc)}");
                        }
                    }
                }

                // foreach (GeneratedRouter router in generatedRouters)
                // {
                //     if (router.routerType == typeof(UdonBehaviour) && router.routerGraphName == program.name)
                //     {
                //         // Generate router variables
                //         generator.AddVariable<string>("_RPC_PostRoute");
                //         foreach (GeneratedRPC rpc in generatedRPCs)
                //         {
                //             
                //         }
                //         for (int i = 1; i <= NetworkEventParameterLimit - 1; i++) // -1 because of the routing ID
                //         {
                //             generator.AddVariable<object>($"_RPC_ROUTER_Param{i}");
                //         }
                //     }
                // }
                //
                // bool hasAnyRPC = false;
                // bool hasAnyHook = false;
                // foreach (GeneratedRPC rpc in generatedRPCs)
                // {
                //     if (rpc.type == typeof(UdonBehaviour) && rpc.graphName == program.name)
                //     {
                //         hasAnyRPC = true;
                //         foreach (var parameter in rpc.methodParameters)
                //         {
                //             generator.AddVariable(parameter.type, $"{parameter.GraphParameterName(rpc)}");
                //         }
                //     }
                //     foreach (GeneratedRPCHook hook in rpc.hooks)
                //     {
                //         if (hook.singleton.type == typeof(UdonBehaviour) && hook.singleton.udonGraphGuid == guid)
                //         {
                //             hasAnyHook = true;
                //             foreach (var parameter in hook.methodParameters)
                //             {
                //                 generator.AddVariable(parameter.type, $"{parameter.GraphParameterName(hook)}");
                //             }
                //         }
                //     }
                // }
                //
                // if (hasAnyHook)
                // {
                //     generator.AddVariable(typeof(string), $"_RPC_PostHook");
                // }
                // if (hasAnyRPC||hasAnyHook) generator.AddVariable<UdonBehaviour>("_RPC_Manager", true);
            }
        }

        private const string regionTag = "Generated RPCs (DO NOT EDIT)";

        private static string RegenerateFile(string path, List<String> generatedLines)
        {
            string[] lines = File.ReadAllLines(path);

            if (generatedLines.Count != 0)
            {
                generatedLines.Insert(0, $"#region {regionTag}");
                generatedLines.Add($"#endregion");
            }

            List<string> filePrefix = new();
            List<string> fileSuffix = new();

            bool foundRegionStart = false;
            bool foundRegionEnd = false;
            int indent = 0;
            // First, try to find #region tag
            for (int i = 0; i < lines.Length; i++)
            {
                if (foundRegionEnd) fileSuffix.Add(lines[i]);
                if (lines[i].Trim().Equals($"#region {regionTag}", StringComparison.InvariantCultureIgnoreCase))
                {
                    foundRegionStart = true;
                    indent = lines[i].IndexOf('#');
                }

                if (foundRegionStart && lines[i].Trim().Equals($"#endregion", StringComparison.InvariantCultureIgnoreCase))
                {
                    foundRegionEnd = true;
                }

                if (!foundRegionStart) filePrefix.Add(lines[i]);
            }

            if (foundRegionStart && foundRegionEnd)
            {
                string indentStr = "";
                for (int j = 0; j < indent; j++) indentStr += " ";
                return $"{string.Join("\n", filePrefix)}\n{indentStr}{string.Join($"\n{indentStr}", generatedLines)}\n{string.Join($"\n", fileSuffix)}";
            }

            // Region tag not found, insert at end of class.

            string fullFile = string.Join("\n", lines);

            string pattern = @".*?\[.*?GenerateRPCs.*?\].*?class.*?({).*";

            Regex regex = new Regex(pattern, RegexOptions.Singleline);
            var match = regex.Match(fullFile);

            if (!match.Success || match.Groups.Count <= 1)
            {
                throw new Exception($"Could not regenerate RPCs in file {path}! Class declaration was not found.");
            }

            int classStartIdx = match.Groups[1].Index;
            int braceCount = 0;
            int lastNewLineOrNonWhitespace = -1;
            bool lockIndent = false;
            indent = 0;
            // This doesn't handle using {} in strings and comments, but that's *fine* since this is the fallback system anyway. *Surely* there won't be mismatched braces in comments before it can ever generate the region, right?
            for (int i = classStartIdx; i < fullFile.Length; i++)
            {
                char c = fullFile[i];
                if (c == '{') braceCount++;
                if (c == '}') braceCount--;
                if (c == ' ' && !lockIndent) indent++;
                if (braceCount == 0)
                {
                    string indentStr = "";
                    for (int j = 0; j < indent; j++) indentStr += " ";
                    return $"{fullFile.Substring(0, lastNewLineOrNonWhitespace).Trim()}\n{indentStr}    {string.Join($"\n{indentStr}    ", generatedLines)}\n{indentStr}{fullFile.Substring(lastNewLineOrNonWhitespace).Trim()}";
                }

                if (!char.IsWhiteSpace(c) || c == '\n')
                {
                    lastNewLineOrNonWhitespace = i;
                    lockIndent = true;
                }

                if (c == '\n')
                {
                    lockIndent = false;
                    indent = 0;
                }
            }

            throw new Exception($"Could not regenerate RPCs in file {path}! End of class was not found.");
        }

        private enum GenerationTarget
        {
            Channel,
            MethodContainer
        }

        private enum GenerationMode
        {
            Clean,
            Editor,
            Build
        }

        public class GeneratedSingleton
        {
            public Type type;
            public int id;
            public string udonGraphGuid;
            public string SharpFieldName => $"singleton_{id}";
        }

        public class GeneratedRouter
        {
            public int id;
            public Type routerType;
            public string routerGraphName;
            public Type routableType;
            public string routableGraphName;
            public Type idType;
            public string SharpFieldName => $"router_{id}";
            public string GraphMethodName => $"_RPC_RouteRPC";
            public string SharpRoutedFieldName(GeneratedRPC rpc) => $"rpc_{rpc.id}_routed";
            public string SharpPostRouteMethodName(GeneratedRPC rpc) => $"_RPC_{rpc.id}_PostRoute";
        }

        public class GeneratedRPC
        {
            public ushort id;
            public string methodName;
            public GeneratedRPCParameter routerParameter;
            public List<GeneratedRPCParameter> methodParameters;
            public GeneratedSingleton singleton;
            public GeneratedRouter router;
            public Type type;
            public bool isUniqueType;
            public string graphName;
            public RPCSyncMode mode;
            public bool ignoreDuplicates;
            public string TypeName => graphName ?? (isUniqueType ? type.Name : type.FullName).Replace('.', '_');
            public string FullTypeName => graphName ?? type.FullName;
            public string SharpIdConstName => $"RPC_{TypeName}_{methodName}";
            public string GraphMethodName => $"_RPC_{methodName}";
            public string SharpMethodName => methodName;

            public List<GeneratedRPCParameter> AllParameters
            {
                get
                {
                    List<GeneratedRPCParameter> parameters = new(methodParameters);
                    if (router != null) parameters.Insert(0, routerParameter);
                    return parameters;
                }
            }

            public List<GeneratedRPCHook> hooks = new();
        }

        public class GeneratedRPCParameter
        {
            public string name;
            public Type type;

            public GeneratedRPCParameter()
            {
            }

            public GeneratedRPCParameter(ParameterInfo info)
            {
                name = info.Name;
                type = info.ParameterType;
            }

            public string SharpParameterType => type.FullName;
            public string SharpParameterName => $"p_{name}";
            public string SharpMethodParameter => $"{SharpParameterType} {SharpParameterName}";
            public string GraphParameterName(GeneratedRPC rpc) => $"_RPC_R_{rpc.methodName}_p_{name}";
            public string GraphParameterName(GeneratedRouter router) => $"_RPC_Router_{name}";
            public string GraphParameterName(GeneratedRPCHook hook) => $"_RPC_H_{hook.GraphMethodName}_p_{name}";
            public string SharpFieldName(GeneratedRPC rpc) => $"rpc_{rpc.id}_{SharpParameterName}";
        }

        private static string AsSharpMethodParameters(this List<GeneratedRPCParameter> parameters, string prefix = "")
        {
            if (parameters.Count == 0) return "";
            List<string> strs = new();
            foreach (var parameter in parameters) strs.Add($"{parameter.SharpMethodParameter}");
            return prefix + string.Join(", ", strs);
        }

        private static string AsSharpCallParameters(this List<GeneratedRPCParameter> parameters, GeneratedRPC rpc, string prefix = "")
        {
            if (parameters.Count == 0) return "";
            List<string> strs = new();
            foreach (var parameter in parameters) strs.Add($"{(rpc == null ? parameter.SharpParameterName : parameter.SharpFieldName(rpc))}");
            return prefix + string.Join(", ", strs);
        }

        public class GeneratedRPCHook
        {
            public GeneratedRPC rpc;
            public GeneratedSingleton singleton;
            public string methodName;
            public List<GeneratedRPCParameter> methodParameters;
            public string GraphMethodName => methodName;
            public string SharpPostHookMethodName => $"_RPC_{rpc.id}_PostHook_{singleton.id}";
            public string SharpMethodName => methodName;
        }
    }
}