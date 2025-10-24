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
                    routerGraphName = program.name,
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

            if (target == GenerationTarget.Channel && mode != GenerationMode.Build)
            {
                generatedLines.Add("private void _DecodeRPC(int id, byte[] data) {}");
            }

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
                        if (mode != GenerationMode.Build) break;

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
                                    generatedLines.Add($"    {rpc.router.SharpFieldName}.SetProgramVariable(\"_RPC_RouteChannel\", this);");
                                    generatedLines.Add($"    {rpc.router.SharpFieldName}.SetProgramVariable(\"_RPC_RouteTarget\", nameof({rpc.router.SharpRoutedFieldName(rpc)}));");
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

        private const int NODE_BASE = 82 - NODE_ROW;
        private const int NODE_ROW = 25;
        private const int NODE_1 = NODE_BASE + NODE_ROW;
        private const int NODE_2 = NODE_BASE + NODE_ROW * 2;
        private const int NODE_3 = NODE_BASE + NODE_ROW * 3;
        private const int NODE_4 = NODE_BASE + NODE_ROW * 4;
        private const int NODE_5 = NODE_BASE + NODE_ROW * 5;
        private const int NODE_6 = NODE_BASE + NODE_ROW * 6;
        private const int NODE_7 = NODE_BASE + NODE_ROW * 7;
        private const int NODE_8 = NODE_BASE + NODE_ROW * 8;
        private const int NODE_9 = NODE_BASE + NODE_ROW * 9;
        private const int NODE_SPACING = NODE_3;
        private const int GRAPH_ROUTER_WIDTH = 1470;

        public class CachedNodeConnection
        {
            public readonly int index;
            public readonly string sourceGroupName;
            public readonly string sourceGuid;
            public readonly string sourceName;
            public readonly string[] sourceValues;

            public readonly string targetGroupName;
            public readonly string targetGuid;
            public readonly string targetName;
            public readonly string[] targetValues;

            public CachedNodeConnection(UdonGraphGen generator, UdonGraphGenNode source, UdonGraphGenNode target, int index)
            {
                this.index = index;
                sourceGroupName = generator.Groups.FirstOrDefault(g => g.Contains(source))?.title;
                sourceGuid = source?.Guid;
                sourceName = source?.Name;
                sourceValues = source?.Values;
                TweakValues(generator, sourceName, sourceValues);

                targetGroupName = generator.Groups.FirstOrDefault(g => g.Contains(target))?.title;
                targetGuid = target?.Guid;
                targetName = target?.Name;
                targetValues = target?.Values;
                TweakValues(generator, targetName, targetValues);
            }

            private void TweakValues(UdonGraphGen generator, string nodeName, string[] values)
            {
                if (values == null) return;
                if (nodeName == "Get_Variable" || nodeName == "Set_Variable")
                {
                    // Use variable name instead of Guid, since the Guids change during codegen
                    Debug.Log($"Changing variable reference Guid {values[0]} to variable name");
                    values[0] = generator.GetVariable(values[0].Split('|')[1]).Name; // use variable name instead of Guid
                    Debug.Log($"Variable name ended up as: {values[0]}");
                }
                if(nodeName=="Set_Variable")
                {
                    // Clear the `sendChange` field, since we don't care about it (and it doesn't like to match up after codegen)
                    values[2] = "";
                }
            }

            public bool SourceMatches(UdonGraphGen generator, UdonGraphGenNode node)
            {
                var nodeGroupName = generator.Groups.FirstOrDefault(g => g.Contains(node))?.title;
                var nodeValues = node.Values;
                TweakValues(generator, node.Name, nodeValues);
                Debug.Log($"Checking source match:\n{sourceGroupName}=={nodeGroupName}\n{sourceName}=={node.Name}\n{string.Join(",", sourceValues)}=={string.Join(",", nodeValues)}");
                return sourceGroupName == nodeGroupName
                       && sourceName == node.Name
                       && sourceValues.SequenceEqual(nodeValues);
            }

            public bool TargetMatches(UdonGraphGen generator, UdonGraphGenNode node)
            {
                var nodeGroupName = generator.Groups.FirstOrDefault(g => g.Contains(node))?.title;
                var nodeValues = node.Values;
                TweakValues(generator, node.Name, nodeValues);
                Debug.Log($"Checking target match:\n{targetGroupName}=={nodeGroupName}\n{targetName}=={node.Name}\n{string.Join(",", targetValues)}=={string.Join(",", nodeValues)}");
                return targetGroupName == nodeGroupName
                       && targetName == node.Name
                       && targetValues.SequenceEqual(nodeValues);
            }
        }

        private static void GenerateRPCs(string guid, UdonGraphProgramAsset program, GenerationMode mode)
        {
            var generator = new UdonGraphGen(program);

            List<CachedNodeConnection> nodeConnections = new();
            List<CachedNodeConnection> flowConnections = new();

            // Cleanup, but remember external connections
            foreach (var g in generator.Groups.Where(g => g.title.StartsWith($"_RPC")))
            {
                List<UdonGraphGenNode> groupNodes = new(g.GetContainedNodes(generator));
                foreach (var node in groupNodes)
                {
                    var nodeInputs = node.GetNodeInputs(generator);
                    for (var i = 0; i < nodeInputs.Length; i++)
                    {
                        var input = nodeInputs[i];
                        if (input == null) continue;
                        if (g.Contains(input)) continue; // Don't care about in-group connections
                        nodeConnections.Add(new(generator, input, node, i));
                    }

                    var flowTargets = node.GetFlowTargets(generator);
                    for (var i = 0; i < flowTargets.Length; i++)
                    {
                        var target = flowTargets[i];
                        if (target == null) continue;
                        if (g.Contains(target)) continue; // Don't care about in-group connections
                        flowConnections.Add(new(generator, node, target, i));
                    }
                }

                foreach (var node in generator.Nodes)
                {
                    if (g.Contains(node)) continue;
                    var nodeInputs = node.GetNodeInputs(generator);
                    for (var i = 0; i < nodeInputs.Length; i++)
                    {
                        var input = nodeInputs[i];
                        if (input == null) continue;
                        if (!g.Contains(input)) continue; // Don't care about out-of-group connections
                        Debug.Log($"[DizzyRPC] and the {i} one is a connection!");
                        nodeConnections.Add(new(generator, input, node, i));
                    }

                    var flowTargets = node.GetFlowTargets(generator);
                    for (var i = 0; i < flowTargets.Length; i++)
                    {
                        var target = flowTargets[i];
                        if (target == null) continue;
                        if (!g.Contains(target)) continue; // Don't care about out-of-group connections
                        flowConnections.Add(new(generator, node, target, i));
                    }
                }

                generator.DeleteGroup(g);
            }

            foreach (var v in generator.Variables.Where(g => g.Name.StartsWith($"_RPC"))) v.Delete();

            List<(UdonGraphGenNode, int)> restoreFlowOutputs = new();
            List<UdonGraphGenNode> restoreFlowInputs = new();
            List<UdonGraphGenNode> restoreNodeOutputs = new();
            List<(UdonGraphGenNode, int)> restoreNodeInputs = new(1);

            if (mode == GenerationMode.Clean)
            {
                // Just do nothing, I guess, since cleanup already happened?
            }
            else
            {
                var allNodes = generator.Nodes;
                float leftX = (allNodes.Length == 0 ? 0 : allNodes.Min(n => n.Position.x)) - 640;
                float leftY = 0;
                float rightX = (allNodes.Length == 0 ? 0 : allNodes.Max(n => n.Position.x)) + 400;
                float rightY = 0;
                float bottomX = 0;
                float bottomY = (allNodes.Length == 0 ? 0 : allNodes.Max(n => n.Position.y)) + 400;

                var rpcManagerVar = generator.AddVariable<UdonBehaviour>($"_RPC_Manager");

                int routerCount = generatedRouters.Count(router => router.routerType == typeof(UdonBehaviour) && router.routerGraphName == program.name); // always 1 or 0, but you never know
                int hookCount = generatedRPCs.Sum(rpc => rpc.hooks.Count(hook => hook.singleton.type == typeof(UdonBehaviour) && hook.singleton.udonGraphGuid == guid));

                float totalBottomWidth = (routerCount + hookCount) * GRAPH_ROUTER_WIDTH;

                float missingWidth = totalBottomWidth - (rightX - (leftX + 240));
                if (missingWidth > 0)
                {
                    leftX -= missingWidth / 2;
                    rightX += missingWidth / 2;
                }

                bottomX = ((leftX + 240) + rightX) / 2 - totalBottomWidth / 2;

                foreach (GeneratedRPC rpc in generatedRPCs)
                {
                    if (rpc.type == typeof(UdonBehaviour) && rpc.graphName == program.name)
                    {
                        generator.currentGroup = generator.AddGroup($"_RPC_{rpc.methodName}");

                        generator.AddComment("!! GENERATED CODE !!\nDO NOT ADD OR EDIT ANY NODE IN THIS GROUP!\n\nThis is an RPC method. It will run when this RPC is sent by another user.\n1. Connect flow from the event node below.\n2. Use the variables below if needed.\n3. Add your RPC logic.", new(leftX - 500, leftY, 500, NODE_6));
                        var leftCommentY = leftY + NODE_7;

                        restoreFlowOutputs.Add((generator.AddCustomEvent(rpc.GraphMethodName, position: new(leftX, leftY)), 0));
                        leftY += NODE_3;

                        Dictionary<GeneratedRPCParameter, UdonGraphGenVariable> graphVariables = new();
                        foreach (var parameter in rpc.methodParameters)
                        {
                            graphVariables[parameter] = generator.AddVariable(parameter.type, $"{parameter.GraphParameterName(rpc)}");
                            restoreNodeOutputs.Add(generator.AddGetVariable(graphVariables[parameter], new(leftX, leftY)));
                            leftY += NODE_1;
                        }

                        if (leftCommentY > leftY) leftY = leftCommentY;

                        leftY += NODE_SPACING;

                        generator.currentGroup = generator.AddGroup($"_RPC_SEND_{rpc.methodName}");

                        generator.AddComment("!! GENERATED CODE !!\nDO NOT ADD OR EDIT ANY NODE IN THIS GROUP!\n\nThis is a SEND RPC method. This will send an RPC to another player.\nTo send an RPC:\n1. Connect a VRCPlayerAPI value to the RPC_Target variable.\nThis is the player that will receive the RPC.\n(Use const null to send to all players)\n2. Connect values to ALL RPC variables.\n3. Connect flow to the left side of the Block.", new(rightX + 180, rightY + NODE_5, 700, NODE_8));
                        var rightCommentY = rightY + NODE_9;

                        var originalRightY = rightY;
                        var block = generator.AddBlock(position: new(rightX, rightY));
                        restoreFlowInputs.Add(block);
                        rightY += NODE_BASE + NODE_ROW * (rpc.methodParameters.Count * 2 + 7);

                        List<UdonGraphGenNode> flowNodes = new();
                        var targetVariable = generator.AddVariable<VRCPlayerApi>($"_RPC_Target");
                        if (rpc.methodParameters.Count > 0)
                        {
                            var sTarget = generator.AddSetVariable(targetVariable, new(rightX, rightY));
                            restoreNodeInputs.Add((sTarget, 1));
                            flowNodes.Add(sTarget);
                            rightY += NODE_4;
                            for (int i = 0; i < rpc.methodParameters.Count; i++)
                            {
                                var setValue = generator.AddSetVariable(graphVariables[rpc.methodParameters[i]], new(rightX, rightY));
                                restoreNodeInputs.Add((setValue, 1));
                                flowNodes.Add(setValue);
                                rightY += NODE_4;
                            }
                        }

                        rightX += 100;

                        var rightYWas = rightY;
                        rightY = originalRightY;
                        List<UdonGraphGenNode> nodesThatNeedTheRPCManager = new();
                        var type = generator.AddType<object[]>(new(rightX, rightY));
                        // rightY += NODE_1;
                        var createArray = generator.AddMethod<Array>(nameof(Array.CreateInstance), new(rightX, rightY), typeof(Type), typeof(int));
                        createArray.SetValue(0, type);
                        createArray.SetValue(1, rpc.methodParameters.Count);
                        flowNodes.Add(createArray);
                        // rightY += NODE_4;

                        for (int i = 0; i < rpc.methodParameters.Count; i++)
                        {
                            var setValue = generator.AddMethod<Array>(nameof(Array.SetValue), new(rightX, rightY), typeof(object), typeof(int));
                            setValue.SetValue(0, createArray);
                            setValue.SetValue(2, i);
                            flowNodes.Add(setValue);
                            // rightY += NODE_5;
                            var getValue = generator.AddGetVariable(graphVariables[rpc.methodParameters[i]], new(rightX, rightY));
                            setValue.SetValue(1, getValue);
                            // rightY += NODE_1;
                        }

                        var setTarget = generator.AddMethod<UdonBehaviour>(nameof(UdonBehaviour.SetProgramVariable), new(rightX, rightY), typeof(string), typeof(object));
                        setTarget.SetValue(1, nameof(RPCManager._graph_target));
                        flowNodes.Add(setTarget);
                        nodesThatNeedTheRPCManager.Add(setTarget);
                        // rightY += NODE_4;
                        var getTarget = generator.AddGetVariable(targetVariable, new(rightX, rightY));
                        setTarget.SetValue(2, getTarget);
                        // rightY += NODE_1;

                        var setParameters = generator.AddMethod<UdonBehaviour>(nameof(UdonBehaviour.SetProgramVariable), new(rightX, rightY), typeof(string), typeof(object));
                        setParameters.SetValue(1, nameof(RPCManager._graph_parameters));
                        setParameters.SetValue(2, createArray);
                        flowNodes.Add(setParameters);
                        nodesThatNeedTheRPCManager.Add(setParameters);
                        // rightY += NODE_4;

                        var setId = generator.AddMethod<UdonBehaviour>(nameof(UdonBehaviour.SetProgramVariable), new(rightX, rightY), typeof(string), typeof(object));
                        setId.SetValue(1, nameof(RPCManager._graph_id));
                        flowNodes.Add(setId);
                        nodesThatNeedTheRPCManager.Add(setId);
                        // rightY += NODE_4;

                        var constId = generator.AddConst<int>(rpc.id, new(rightX, rightY));
                        setId.SetValue(2, constId);
                        // rightY += NODE_1;


                        var sendCustomEvent = generator.AddMethod<UdonBehaviour>(nameof(UdonBehaviour.SendCustomEvent), new(rightX, rightY), typeof(string));
                        switch (rpc.mode)
                        {
                            case RPCSyncMode.Event:
                                sendCustomEvent.SetValue(1, nameof(RPCManager._Graph_SendEvent));
                                break;
                            case RPCSyncMode.Variable:
                                sendCustomEvent.SetValue(1, nameof(RPCManager._Graph_SendVariable));
                                break;
                        }

                        flowNodes.Add(sendCustomEvent);
                        block.AddFlow(flowNodes.ToArray());

                        // rightY += NODE_3;

                        var rpcManagerGet = generator.AddGetVariable(rpcManagerVar, new(rightX, rightY));
                        sendCustomEvent.SetValue(0, rpcManagerGet);
                        foreach (var node in nodesThatNeedTheRPCManager) node.SetValue(0, rpcManagerGet);
                        // rightY += NODE_1;

                        rightY = rightYWas;

                        if (rightCommentY > rightY) rightY = rightCommentY;

                        rightY += NODE_SPACING;
                    }
                }

                bottomY = Mathf.Max(bottomY, leftY, rightY);

                foreach (GeneratedRouter router in generatedRouters)
                {
                    if (router.routerType == typeof(UdonBehaviour) && router.routerGraphName == program.name)
                    {
                        bottomX += NODE_SPACING;

                        var baseBottomY = bottomY;
                        var baseBottomX = bottomX;

                        generator.currentGroup = generator.AddGroup($"_RPC_ROUTER");

                        var routeResultVar = generator.AddVariable<UdonBehaviour>("_RPC_RouteResult");

                        restoreFlowOutputs.Add((generator.AddCustomEvent("_RPC_RouteRPC", position: new(bottomX, bottomY)), 0));
                        bottomY += NODE_3;

                        restoreNodeOutputs.Add(generator.AddGetVariable(generator.AddVariable<string>("_RPC_Router__id"), new(bottomX, bottomY)));
                        bottomY += NODE_1;

                        bottomX += GRAPH_ROUTER_WIDTH - 500;
                        bottomY = baseBottomY;

                        var block = generator.AddBlock(new(bottomX, bottomY));
                        restoreFlowInputs.Add(block);
                        bottomY += NODE_4;

                        var setResult = generator.AddSetVariable(routeResultVar, new(bottomX, bottomY));
                        restoreNodeInputs.Add((setResult, 1));
                        bottomY += NODE_4;

                        bottomY = baseBottomY;
                        bottomX += 100;

                        var postRouteTarget = generator.AddMethod<UdonBehaviour>(nameof(UdonBehaviour.SetProgramVariable), new(bottomX, bottomY), typeof(string), typeof(object));
                        // bottomY += NODE_4;
                        var routeTarget = generator.AddGetVariable(generator.AddVariable<string>("_RPC_RouteTarget"), new(bottomX, bottomY));
                        // bottomY += NODE_1;
                        postRouteTarget.SetValue(1, routeTarget);

                        var getResult = generator.AddGetVariable(routeResultVar, new(bottomX, bottomY));
                        postRouteTarget.SetValue(2, getResult);
                        // bottomY += NODE_1;

                        var postRouteEvent = generator.AddMethod<UdonBehaviour>(nameof(UdonBehaviour.SendCustomEvent), new(bottomX, bottomY), typeof(string));
                        // bottomY += NODE_3;
                        var postRoute = generator.AddGetVariable(generator.AddVariable<string>("_RPC_PostRoute"), new(bottomX, bottomY));
                        // bottomY += NODE_1;
                        postRouteEvent.SetValue(1, postRoute);

                        block.AddFlow(setResult, postRouteTarget, postRouteEvent);

                        var routeChannel = generator.AddGetVariable(generator.AddVariable<UdonBehaviour>("_RPC_RouteChannel"), new(bottomX, bottomY));
                        // bottomY += NODE_1;
                        postRouteTarget.SetValue(0, routeChannel);
                        postRouteEvent.SetValue(0, routeChannel);

                        bottomY = baseBottomY;

                        float commentWidth = GRAPH_ROUTER_WIDTH - NODE_SPACING - 700;
                        generator.AddComment("!! GENERATED CODE !!\nDO NOT ADD OR EDIT ANY NODE IN THIS GROUP!\n\nThis is a routing function. It must match a routing ID to the corresponding UdonBehavior.", new(baseBottomX + 300, bottomY, commentWidth, NODE_4));
                        bottomY += NODE_4;
                        generator.AddComment("1. Connect flow from the event on the left to begin routing.\n2. Use the variable on the left for the routing ID.\n3. Add routing logic to match the ID to an UdonBehavior.", new(baseBottomX + 300, bottomY, commentWidth / 2, NODE_5));
                        generator.AddComment("4. Connect the UdonBehavior value to the RouteResult variable on the right.\n5. Connect flow to the block on the right to complete routing.\nDO NOT CONNECT FLOW FROM ANY OTHER SOURCE.", new(baseBottomX + 300 + commentWidth / 2, bottomY, commentWidth / 2, NODE_5));

                        bottomX += 250;
                        bottomY = baseBottomY;
                    }
                }

                if (hookCount > 0)
                {
                    var postHookVar = generator.AddVariable(typeof(string), $"_RPC_PostHook");
                    var hookChannelVar = generator.AddVariable<UdonBehaviour>("_RPC_HookChannel");

                    foreach (GeneratedRPC rpc in generatedRPCs)
                    {
                        foreach (GeneratedRPCHook hook in rpc.hooks)
                        {
                            if (hook.singleton.type == typeof(UdonBehaviour) && hook.singleton.udonGraphGuid == guid)
                            {
                                bottomX += NODE_SPACING;

                                var baseBottomY = bottomY;
                                var baseBottomX = bottomX;

                                generator.currentGroup = generator.AddGroup($"_RPC_HOOK_{hook.methodName}");


                                restoreFlowOutputs.Add((generator.AddCustomEvent(hook.GraphMethodName, position: new(bottomX, bottomY)), 0));
                                bottomY += NODE_3;

                                foreach (var parameter in hook.methodParameters)
                                {
                                    restoreNodeOutputs.Add(generator.AddGetVariable(generator.AddVariable(parameter.type, $"{parameter.GraphParameterName(hook)}"), new(bottomX, bottomY)));
                                    bottomY += NODE_1;
                                }

                                bottomX += GRAPH_ROUTER_WIDTH - 500;
                                bottomY = baseBottomY;

                                var block = generator.AddBlock(new(bottomX, bottomY));
                                restoreFlowInputs.Add(block);
                                bottomY += NODE_4;

                                bottomY = baseBottomY;
                                bottomX += 100;

                                var postHookEvent = generator.AddMethod<UdonBehaviour>(nameof(UdonBehaviour.SendCustomEvent), new(bottomX, bottomY), typeof(string));
                                // bottomY += NODE_3;

                                var hookChannel = generator.AddGetVariable(hookChannelVar, new(bottomX, bottomY));
                                // bottomY += NODE_1;
                                postHookEvent.SetValue(0, hookChannel);

                                var postRoute = generator.AddGetVariable(postHookVar, new(bottomX, bottomY));
                                // bottomY += NODE_1;
                                postHookEvent.SetValue(1, postRoute);

                                block.AddFlow(postHookEvent);

                                bottomY = baseBottomY;

                                float commentWidth = GRAPH_ROUTER_WIDTH - NODE_SPACING - 700;
                                generator.AddComment("!! GENERATED CODE !!\nDO NOT ADD OR EDIT ANY NODE IN THIS GROUP!\n\nThis is an RPC hook. This will run after an RPC has been received, but before it has run.", new(baseBottomX + 300, bottomY, commentWidth, NODE_4));
                                bottomY += NODE_4;
                                generator.AddComment("1. Connect flow from the event on the left to begin the hook.\n2. Use the variables on the left if needed.\n3. Add your custom hook logic.", new(baseBottomX + 300, bottomY, commentWidth / 2, NODE_5));
                                generator.AddComment("4. If you wish to allow the RPC to run, you must connect flow to the block on the right. If flow does not reach this block, the RPC will be cancelled.\nDO NOT CONNECT FLOW FROM ANY OTHER SOURCE.", new(baseBottomX + 300 + commentWidth / 2, bottomY, commentWidth / 2, NODE_5));

                                bottomX += 250;
                                bottomY = baseBottomY;
                            }
                        }
                    }
                }
            }

            Debug.Log($"[DizzyRPC] Restoring {restoreFlowOutputs.Count} Flow outputs from {flowConnections.Count} connections");
            foreach (var (node, index) in restoreFlowOutputs)
            {
                foreach (var conn in flowConnections)
                {
                    if (conn.SourceMatches(generator, node) && conn.index == index)
                    {
                        node.InsertFlowTarget(index, conn.targetGuid);
                    }
                }
            }

            Debug.Log($"[DizzyRPC] Restoring {restoreFlowInputs.Count} Flow inputs from {flowConnections.Count} connections");
            foreach (var node in restoreFlowInputs)
            {
                foreach (var conn in flowConnections)
                {
                    if (conn.TargetMatches(generator, node))
                    {
                        generator.GetNode(conn.sourceGuid).InsertFlowTarget(conn.index, node);
                    }
                }
            }

            Debug.Log($"[DizzyRPC] Restoring {restoreNodeInputs.Count} Node inputs from {nodeConnections.Count} connections");
            foreach (var (node, index) in restoreNodeInputs)
            {
                foreach (var conn in nodeConnections)
                {
                    if (conn.TargetMatches(generator, node) && conn.index == index)
                    {
                        node.SetValue(index, generator.GetNode(conn.sourceGuid));
                    }
                }
            }

            Debug.Log($"[DizzyRPC] Restoring {restoreNodeOutputs.Count} Node outputs from {nodeConnections.Count} connections");
            foreach (var node in restoreNodeOutputs)
            {
                foreach (var conn in nodeConnections)
                {
                    if (conn.SourceMatches(generator, node))
                    {
                        generator.GetNode(conn.targetGuid).SetValue(conn.index, node);
                    }
                }
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

            Regex regex = new Regex(@"\bclass\b[^{]*({)", RegexOptions.Singleline);
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
            public string GraphParameterName(GeneratedRPC rpc) => $"_RPC_p_{rpc.methodName}__{name}";
            public string GraphParameterName(GeneratedRouter router) => $"_RPC_Router_{name}";
            public string GraphParameterName(GeneratedRPCHook hook) => $"_RPCH_p_{hook.GraphMethodName}__{name}";
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