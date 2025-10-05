using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor.ProgramSources.UdonGraphProgram;
using VRC.Udon.Editor.ProgramSources.UdonGraphProgram.UI.GraphView;
using VRC.Udon.Graph;
using VRC.Udon.Serialization;

namespace DizzyRPC.Editor
{
    /// <summary>
    /// This class borrows a lot of functionality from VRC.Udon.Editor.ProgramSources.UdonGraphProgram.UI.GraphView.UdonGraph
    /// However, it's much more tailored to code generation rather than UI-based modification.
    /// </summary>
    public class UdonGraphGen
    {
        private readonly UdonGraphProgramAsset _program;

        public UdonGraphGenVariable[] Variables
        {
            get
            {
                List<UdonGraphGenVariable> variables = new();
                foreach (var node in _program.graphData.nodes)
                {
                    if (node.IsVariable())
                    {
                        variables.Add(new UdonGraphGenVariable(node));
                    }
                }

                return variables.ToArray();
            }
        }

        public UdonGraphGen(UdonGraphProgramAsset program)
        {
            _program = program;
        }

        public UdonGraphGenVariable AddVariable<T>(string name = null, bool isPublic = false) => AddVariable(typeof(T), name, isPublic);

        public UdonGraphGenVariable AddVariable(Type type, string name = null, bool isPublic = false)
        {
            if (type == typeof(UdonBehaviour)) type = typeof(IUdonEventReceiver); // This is what VRChat uses for UdonBehavior variables
            var newNode = _program.graphData.AddNode($"Variable_{type.FullName.Replace(".", "")}");

            if (name == null) name = $"_generated_{newNode.uid}";

            newNode.nodeUIDs = new string[5];
            newNode.nodeValues = new[]
            {
                SerializableObjectContainer.Serialize(default),
                SerializableObjectContainer.Serialize(name, typeof(string)),
                SerializableObjectContainer.Serialize(isPublic, typeof(bool)),
                SerializableObjectContainer.Serialize(false, typeof(bool)), // synced
                SerializableObjectContainer.Serialize("none", typeof(string)) // sync type (smoothing), "none", "linear", or "smooth" - see VRC.Udon.UdonNetworkTypes
            };
            newNode.position = Vector2.zero;
            return new UdonGraphGenVariable(newNode);
        }

        public UdonGraphGenCustomEvent AddCustomEvent(string name = null, int rateLimit = 5, int numParameters = 0, Vector2? position = null)
        {
            var newNode = _program.graphData.AddNode("Event_Custom");

            if (name == null) name = $"_generated_{newNode.uid}";

            newNode.nodeUIDs = new string[2];
            newNode.nodeValues = new[]
            {
                SerializableObjectContainer.Serialize(name, typeof(string)),
                SerializableObjectContainer.Serialize(rateLimit, typeof(int)),
            };
            newNode.position = position ?? Vector2.zero;
            return new UdonGraphGenCustomEvent(newNode);
        }
    }

    public class UdonGraphGenVariable : UdonGraphGenNode
    {
        public Type Type
        {
            get
            {
                foreach (var type in Assembly.GetAssembly(typeof(UdonNodeData)).GetTypes())
                {
                    if (_node.fullName == $"Variable_{type.FullName.Replace(".", "")}") return type;
                }

                throw new Exception($"Could not find type for variable: {_node.fullName}!");
            }
        }

        public string Name { get => _node.nodeValues[(int)UdonParameterProperty.ValueIndices.Name].GetAsString(); set => _node.nodeValues[(int)UdonParameterProperty.ValueIndices.Name].Set(value); }
        public bool IsPublic { get => _node.nodeValues[(int)UdonParameterProperty.ValueIndices.IsPublic].GetAsBool(); set => _node.nodeValues[(int)UdonParameterProperty.ValueIndices.IsPublic].Set(value); }
        public bool IsSynced { get => _node.nodeValues[(int)UdonParameterProperty.ValueIndices.IsSynced].GetAsBool(); set => _node.nodeValues[(int)UdonParameterProperty.ValueIndices.IsSynced].Set(value); }

        public UdonGraphGenVariable(UdonNodeData node) : base(node)
        {
            if (!node.IsVariable()) throw new ArgumentException($"Node is not a variable: {node.fullName}!");
        }
    }

    public class UdonGraphGenNode
    {
        protected readonly UdonNodeData _node;
        protected UdonGraphGenNode(UdonNodeData node) => _node = node;

        public void Delete()
        {
            foreach (var node in _node.GetGraph().nodes)
            {
                node.ClearReferencesToNode(_node);
            }

            _node.GetGraph().RemoveNode(_node);
        }
    }

    public class UdonGraphGenCustomEvent : UdonGraphGenNode
    {
        public string Name { get => _node.nodeValues[0].GetAsString(); set => _node.nodeValues[0].Set(value); }
        public int RateLimit { get => _node.nodeValues[1].GetAsInt(); set => _node.nodeValues[1].Set(value); }

        public UdonGraphGenCustomEvent(UdonNodeData node) : base(node)
        {
            if (!node.IsCustomEvent()) throw new ArgumentException($"Node is not a custom event: {node.fullName}!");
        }
    }

    public static class UdonGraphGenExtensions
    {
        public static bool IsVariable(this UdonNodeData node) => node.fullName.StartsWith("Variable");
        public static bool IsEvent(this UdonNodeData node) => node.fullName.StartsWith("Event");

        public static bool IsCustomEvent(this UdonNodeData node, int? numParameters = null)
        {
            if (numParameters == null) return node.fullName.StartsWith("Event_Custom");
            if (numParameters == 0) return node.fullName.Equals("Event_Custom");
            return node.fullName.Equals($"Event_Custom_{numParameters}_Parameters");
        }
    }

    public static class UdonNodeValueExtensions
    {
        private static object Get(SerializableObjectContainer value, out Type type)
        {
            var strs = value.stringValue.Split('|', 2);
            type = Type.GetType(strs[0]);
            return Parse(strs[1], type);
        }

        private static object Get(SerializableObjectContainer value) => Get(value, out _);
        public static bool GetAsBool(this SerializableObjectContainer value) => (bool)Get(value);
        public static int GetAsInt(this SerializableObjectContainer value) => (int)Get(value);
        public static string GetAsString(this SerializableObjectContainer value) => (string)Get(value);
        public static Type GetAsType(this SerializableObjectContainer value) => (Type)Get(value);

        public static void Set<T>(this SerializableObjectContainer value, T v)
        {
            value.stringValue = $"{typeof(T).AssemblyQualifiedName}|{ToString(v)}";
        }

        private static object Parse(string str, Type type)
        {
            if (type == typeof(Type)) return Type.GetType(str);
            return Convert.ChangeType(str, type);
        }

        private static string ToString<T>(T t)
        {
            if (t is Type type) return type.AssemblyQualifiedName;
            return $"{t}";
        }
    }
}