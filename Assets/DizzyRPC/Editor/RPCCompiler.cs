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

namespace DizzyRPC.Editor
{
    public class RPCCompiler : MonoBehaviour
    {
        private static readonly List<GeneratedSingleton> generatedSingletons = new();
        private static readonly List<GeneratedRouter> generatedRouters = new();
        private static readonly List<GeneratedRPC> generatedRPCs = new();

        [MenuItem("Tools/DizzyRPC/Compile RPCs")]
        public static void Compile()
        {
            CompileRPCs();

            try
            {
                AssetDatabase.StartAssetEditing();

                var types = TypeCache.GetTypesWithAttribute<GenerateRPCsAttribute>();
                List<Type> unknownTypes = new(types);
                foreach (var type in types.Where((type) => type == typeof(RPCChannel))) // I could just always generate for RPCChannel, but this way it still uses the GenerateRPCs attribute
                {
                    unknownTypes.Remove(type);
                    GenerateRPCs(type, FindMonoAssetPath(type), GenerationMode.Channel);
                }

                foreach (var type in types.Where((type) => type.GetMethods().Any((method) => method.GetCustomAttribute<RPCMethodAttribute>() != null)))
                {
                    unknownTypes.Remove(type);
                    GenerateRPCs(type, FindMonoAssetPath(type), GenerationMode.MethodContainer);
                }

                if (unknownTypes.Count > 0) throw new Exception($"[DizzyRPC] Could not generate RPCs for type {unknownTypes[0].FullName}, as it does not follow a recognized pattern!");
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }
        
        [MenuItem("Tools/DizzyRPC/Clean Compiled RPCs")]
        public static void Clean()
        {
            generatedSingletons.Clear();
            generatedRouters.Clear();
            generatedRPCs.Clear();
            
            try
            {
                AssetDatabase.StartAssetEditing();

                var types = TypeCache.GetTypesWithAttribute<GenerateRPCsAttribute>();
                foreach (var type in types.Where((type) => type == typeof(RPCChannel)))
                {
                    GenerateRPCs(type, FindMonoAssetPath(type), GenerationMode.Channel);
                }

                foreach (var type in types.Where((type) => type.GetMethods().Any((method) => method.GetCustomAttribute<RPCMethodAttribute>() != null)))
                {
                    GenerateRPCs(type, FindMonoAssetPath(type), GenerationMode.MethodContainer);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
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
            generatedSingletons.Clear();
            generatedRouters.Clear();
            generatedRPCs.Clear();

            // Generate the RPC code
            var assembly = Assembly.GetAssembly(typeof(RPCMethodAttribute));
            List<Type> singletonTypes = new();
            List<Type> types = new(assembly.GetTypes().Where((t) => t.IsClass && !t.IsAbstract));

            // Routers
            foreach (var type in types)
            {
                if (IsDerivedFromGeneric(type, typeof(RPCRouter<,>), out var actualBase))
                {
                    var genericArgs = actualBase.GetGenericArguments();
                    generatedRouters.Add(new GeneratedRouter()
                    {
                        id = generatedRouters.Count,
                        type = type,
                        routableType = genericArgs[0],
                        idType = genericArgs[1]
                    });
                }
            }

            // RPCs
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods())
                {
                    var rpcMethod = method.GetCustomAttribute<RPCMethodAttribute>();
                    if (rpcMethod == null) continue;

                    if (method.GetCustomAttribute<NetworkCallableAttribute>() != null)
                    {
                        throw new Exception($"[DizzyRPC] RPCMethod {type.FullName}.{method.Name} must not have the NetworkCallable attribute!");
                    }

                    if (!method.Name.StartsWith("_"))
                    {
                        throw new Exception($"[DizzyRPC] The name of RPCMethod {type.FullName}.{method.Name} must begin with _!");
                    }

                    var mode = rpcMethod.mode;
                    if (mode == RPCSyncMode.Automatic)
                    {
                        mode = RPCSyncMode.Variable; // Safe default, much higher bandwidth limit
                        // Figure out what kind of RPC this is supposed to be
                        if (!rpcMethod.allowDropping) mode = RPCSyncMode.Event; // Events are never skipped, variables might be
                        if (rpcMethod.requireLowLatency) mode = RPCSyncMode.Variable; // Variables sync instantly, events are roughly every second
                        if (rpcMethod.enforceSecure) mode = RPCSyncMode.Event; // Events are only sent to the target player, variables are broadcast to all
                    }

                    // Validate settings
                    if (rpcMethod.enforceSecure && mode != RPCSyncMode.Event) throw new ArgumentException("RPC Method with enforceSecure enabled must have a RPCSyncMode of Event!");
                    if (rpcMethod.requireLowLatency && mode != RPCSyncMode.Variable) throw new ArgumentException("RPC Method with requireLowLatency enabled must have a RPCSyncMode of Variable!");
                    if (rpcMethod.allowDropping == false && mode != RPCSyncMode.Event) throw new ArgumentException("RPC Method with allowDropping disabled must have a RPCSyncMode of Event!");

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

                            var rpc = new GeneratedRPC()
                            {
                                id = generatedRPCs.Count,
                                isUniqueType = types.Count((t) => t.Name == type.Name) == 1,
                                method = method,
                                singleton = generatedSingletons[singletonTypes.IndexOf(type)],
                                type = type,
                                mode = mode
                            };
                            generatedRPCs.Add(rpc);
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
                            var rpc = new GeneratedRPC()
                            {
                                id = generatedRPCs.Count,
                                isUniqueType = types.Count((t) => t.Name == type.Name) == 1,
                                method = method,
                                router = router,
                                type = type,
                                mode = mode
                            };
                            generatedRPCs.Add(rpc);
                            generated = true;
                            break;
                        }
                    }

                    if (generated) continue;

                    throw new Exception($"Could not map RPCs to {method.Name} in {type.Name}! ");
                }
            }

            // RPC Hooks
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods())
                {
                    var hook = method.GetCustomAttribute<RPCHookAttribute>();
                    if (hook == null) continue;
                    bool found = false;
                    foreach (var rpc in generatedRPCs)
                    {
                        if (hook.type == rpc.type && hook.methodName == rpc.method.Name)
                        {
                            found = true;

                            foreach (var parameter in method.GetParameters())
                            {
                                if (!rpc.method.GetParameters().Any(param => param.Name == parameter.Name && param.ParameterType == parameter.ParameterType))
                                {
                                    throw new Exception($"Cannot compile RPC hook {hook.methodName}! Parameter {parameter.Name} of type {parameter.ParameterType.FullName} is not present on the target RPC method! RPC hooks must only use parameters that exist on the RPC method.");
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

                            rpc.hooks.Add(new GeneratedRPCHook() { singleton = hookSingleton, method = method });
                            break;
                        }
                    }

                    if (!found) throw new Exception($"[DizzyRPC] Could not generate RPC hook: {hook.type.FullName}.{hook.methodName}! (No matching RPC was found)");
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

        private static void GenerateRPCs(Type type, string path, GenerationMode mode)
        {
            List<string> generatedLines = new();
            switch (mode)
            {
                case GenerationMode.Channel:
                    foreach (GeneratedSingleton singleton in generatedSingletons)
                    {
                        generatedLines.Add($"[{typeof(SerializeField).FullName}] private {singleton.type.FullName} singleton_{singleton.id};");
                    }

                    generatedLines.Add("");
                    foreach (GeneratedRouter router in generatedRouters)
                    {
                        generatedLines.Add($"[{typeof(SerializeField).FullName}] private {router.type.FullName} router_{router.id};");
                    }

                    generatedLines.Add("");
                    foreach (GeneratedRPC rpc in generatedRPCs)
                    {
                        generatedLines.Add($"public const int RPC_{rpc.TypeName}_{rpc.method.Name} = {rpc.id};");
                    }

                    generatedLines.Add("");
                    foreach (GeneratedRPC rpc in generatedRPCs)
                    {
                        if (rpc.mode != RPCSyncMode.Event) continue;
                        List<string> methodParameters = new();
                        List<string> callParameters = new();

                        if (rpc.router != null) methodParameters.Add($"{rpc.router.idType.FullName} _id");

                        foreach (var parameter in rpc.method.GetParameters())
                        {
                            methodParameters.Add($"{parameter.ParameterType.FullName} {parameter.Name}");
                            callParameters.Add($"{parameter.Name}");
                        }

                        generatedLines.Add($"[{typeof(NetworkCallableAttribute).Namespace}.NetworkCallable]");
                        generatedLines.Add($"public void RPC_{rpc.id}({string.Join(", ", methodParameters)}) {{");
                        generatedLines.Add($"    debugger.{nameof(RPCDebugger._OnReceiveRPC)}({typeof(NetworkCalling).FullName}.{nameof(NetworkCalling.CallingPlayer)}, {rpc.id}{(rpc.router != null ? ", _id" : "")}{(callParameters.Count > 0 ? ", " : "")}{string.Join(", ", callParameters)});");
                        foreach (var hook in rpc.hooks)
                        {
                            List<string> hookCallParameters = new();

                            foreach (var parameter in hook.method.GetParameters())
                            {
                                hookCallParameters.Add($"{parameter.Name}");
                            }

                            generatedLines.Add($"    if (!singleton_{hook.singleton.id}.{hook.method.Name}({string.Join(", ", hookCallParameters)})) return;");
                        }

                        if (rpc.singleton != null) generatedLines.Add($"    singleton_{rpc.singleton.id}.{rpc.method.Name}({string.Join(", ", callParameters)});");
                        if (rpc.router != null)
                        {
                            generatedLines.Add($"    router_{rpc.router.id}._Route(_id).{rpc.method.Name}({string.Join(", ", callParameters)});");
                        }

                        generatedLines.Add($"}}");
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
                        if (rpc.mode != RPCSyncMode.Variable) continue;
                        List<(Type, string)> rpcParameters = new();
                        List<string> callParameters = new();

                        if (rpc.router != null) rpcParameters.Add((rpc.router.idType, "_id"));

                        foreach (var parameter in rpc.method.GetParameters())
                        {
                            rpcParameters.Add((parameter.ParameterType, parameter.Name));
                            callParameters.Add($"{parameter.Name}");
                        }

                        generatedLines.Add($"private void _DecodeRPC_{rpc.id}(byte[] data) {{");
                        generatedLines.Add($"    int _data_position = 0;");
                        
                        foreach (var (parameterType, parameterName) in rpcParameters)
                        {
                            generatedLines.Add($"    {parameterType.FullName} {parameterName} = Decode{parameterType.Name}(data, ref _data_position);");
                        }
                        
                        foreach (var hook in rpc.hooks)
                        {
                            List<string> hookCallParameters = new();

                            foreach (var parameter in hook.method.GetParameters())
                            {
                                hookCallParameters.Add($"{parameter.Name}");
                            }

                            generatedLines.Add($"    if (!singleton_{hook.singleton.id}.{hook.method.Name}({string.Join(", ", hookCallParameters)})) return;");
                        }

                        if (rpc.singleton != null) generatedLines.Add($"    singleton_{rpc.singleton.id}.{rpc.method.Name}({string.Join(", ", callParameters)});");
                        if (rpc.router != null)
                        {
                            generatedLines.Add($"    router_{rpc.router.id}._Route(_id).{rpc.method.Name}({string.Join(", ", callParameters)});");
                        }

                        generatedLines.Add($"}}");
                    }

                    break;
                case GenerationMode.MethodContainer:
                    generatedLines.Add($"[{typeof(SerializeField).FullName}] private {typeof(RPCManager).FullName} _rpc_manager;");
                    foreach (GeneratedRouter router in generatedRouters)
                    {
                        if (router.routableType == type) generatedLines.Add($"[{typeof(SerializeField).FullName}] private {router.type.FullName} _rpc_router;");
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

                        List<string> methodParameters = new();
                        List<string> callParameters = new();

                        methodParameters.Add($"{typeof(VRCPlayerApi).FullName} target");
                        callParameters.Add("target");
                        callParameters.Add($"{typeof(RPCChannel).FullName}.RPC_{rpc.TypeName}_{rpc.method.Name}");

                        if (rpc.router != null) callParameters.Add($"_rpc_router._GetId(this)");

                        foreach (var parameter in rpc.method.GetParameters())
                        {
                            methodParameters.Add($"{parameter.ParameterType.FullName} {parameter.Name}");
                            callParameters.Add($"{parameter.Name}");
                        }

                        generatedLines.Add($"public void _Send{rpc.method.Name}({string.Join(", ", methodParameters)}) => _rpc_manager.Send{modeName}({string.Join(", ", callParameters)});");
                    }

                    break;
                default:
                    throw new Exception("Unrecognized generation mode: " + Enum.GetName(typeof(GenerationMode), mode));
            }

            var newText = RegenerateFile(path, generatedLines);
            if (newText == null)
            {
                throw new Exception($"Could not regenerate RPCs in file: {path}!");
            }

            File.WriteAllText(path, newText);
        }

        private const string regionTag = "Generated RPCs (DO NOT EDIT)";

        private static string RegenerateFile(string path, List<String> generatedLines)
        {
            string[] lines = File.ReadAllLines(path);

            generatedLines.Insert(0, $"#region {regionTag}");
            generatedLines.Add($"#endregion");

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

        private enum GenerationMode
        {
            Channel,
            MethodContainer
        }

        private class GeneratedSingleton
        {
            public Type type;
            public int id;
        }

        private class GeneratedRouter
        {
            public int id;
            public Type type;
            public Type routableType;
            public Type idType;
        }

        private class GeneratedRPC
        {
            public int id;
            public MethodInfo method;
            public GeneratedSingleton singleton;
            public GeneratedRouter router;
            public Type type;
            public bool isUniqueType;
            public RPCSyncMode mode;
            public string TypeName => isUniqueType ? type.Name.Replace('.', '_') : type.FullName;
            public List<GeneratedRPCHook> hooks = new();
        }

        private class GeneratedRPCHook
        {
            public GeneratedSingleton singleton;
            public MethodInfo method;
        }
    }
}