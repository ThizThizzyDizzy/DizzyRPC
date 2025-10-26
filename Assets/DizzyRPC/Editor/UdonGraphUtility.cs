/*
 * Copyright (C) 2025 ThizThizzyDizzu (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor.ProgramSources.UdonGraphProgram;
using VRC.Udon.Editor.ProgramSources.UdonGraphProgram.UI.GraphView;
using VRC.Udon.Graph;
using VRC.Udon.Serialization;
using VRC.Udon.Serialization.OdinSerializer.Utilities;

namespace DizzyRPC.Editor
{
    /// <summary>
    /// This class borrows a lot of functionality from VRC.Udon.Editor.ProgramSources.UdonGraphProgram.UI.GraphView.UdonGraph
    /// However, it's much more tailored to code generation rather than UI-based modification.
    /// </summary>
    public class UdonGraphGen
    {
        private readonly UdonGraphProgramAsset _program;
        public UdonGraphGenGroup currentGroup;

        public UdonGraphGenNode[] Nodes
        {
            get
            {
                List<UdonGraphGenNode> nodes = new();
                foreach (var node in _program.graphData.nodes)
                {
                    if (node.IsVariable()) continue;
                    nodes.Add(new UdonGraphGenNode(node));
                }

                return nodes.ToArray();
            }
        }

        public UdonGraphGenVariable[] Variables
        {
            get
            {
                List<UdonGraphGenVariable> variables = new();
                foreach (var node in _program.graphData.nodes)
                {
                    if (!node.IsVariable()) continue;
                    variables.Add(new UdonGraphGenVariable(node));
                }

                return variables.ToArray();
            }
        }

        public UdonGraphGenGroup[] Groups
        {
            get
            {
                List<UdonGraphGenGroup> groups = new();
                foreach (var data in _program.graphElementData)
                {
                    if (data.type == UdonGraphElementType.UdonGroup)
                    {
                        groups.Add(UdonGraphGenGroup.FromData(data));
                    }
                }

                return groups.ToArray();
            }
        }

        public UdonGraphGen(UdonGraphProgramAsset program)
        {
            _program = program;
        }

        private UdonGraphGenNode AddNode(string name, int numValues, Vector2? position = null)
        {
            var newNode = _program.graphData.AddNode(name);

            newNode.nodeUIDs = new string[numValues];
            newNode.nodeValues = new SerializableObjectContainer[numValues];
            for (int i = 0; i < numValues; i++) newNode.nodeValues[i] = SerializableObjectContainer.Serialize(default);
            newNode.position = position ?? Vector2.zero;
            var node = new UdonGraphGenNode(newNode);
            currentGroup?.Add(node);
            return node;
        }

        public UdonGraphGenVariable AddVariable<T>(string name = null, bool isPublic = false) => AddVariable(typeof(T), name, isPublic);

        public UdonGraphGenVariable AddVariable(Type type, string name = null, bool isPublic = false)
        {
            var newNode = AddNode($"Variable_{type.ToUdonGraphName()}", 5);

            if (name == null) name = $"_generated_{newNode.Guid}";

            newNode.SetValue(1, name);
            newNode.SetValue(2, isPublic);
            newNode.SetValue(3, false); // Synced
            newNode.SetValue(4, "none"); // sync type (smoothing), "none", "linear", or "smooth" - see VRC.Udon.UdonNetworkTypes
            return new UdonGraphGenVariable(newNode);
        }

        public UdonGraphGenCustomEvent AddCustomEvent(string name = null, int rateLimit = 5, int numParameters = 0, Vector2? position = null)
        {
            var newNode = AddNode("Event_Custom", 2, position);

            if (name == null) name = $"_generated_{newNode.Guid}";

            newNode.SetValue(0, name);
            newNode.SetValue(1, rateLimit);
            return new UdonGraphGenCustomEvent(newNode);
        }

        public UdonGraphGenNode AddGetVariable(UdonGraphGenVariable variable, Vector2? position = null)
        {
            var newNode = AddNode("Get_Variable", 1, position);

            newNode.SetValue(0, variable.Guid);
            return newNode;
        }

        public UdonGraphGenNode AddSetVariable(UdonGraphGenVariable variable, Vector2? position = null)
        {
            var newNode = AddNode("Set_Variable", 3, position);

            newNode.SetValue(0, variable.Guid);
            return newNode;
        }

        public UdonGraphGenBlock AddBlock(Vector2? position = null)
        {
            return new UdonGraphGenBlock(AddNode("Block", 0, position));
        }

        public UdonGraphGenNode AddMethod<T>(string methodName, Vector2? position = null, params Type[] types) => AddMethod(typeof(T), methodName, position, types);

        public UdonGraphGenNode AddMethod(Type type, string methodName, Vector2? position = null, params Type[] types)
        {
            MethodInfo method;
            try
            {
                method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, types, null);
                if (method == null) Debug.LogError($"Method not found: {methodName} of type {type.FullName}");
            }
            catch (AmbiguousMatchException ame)
            {
                Debug.LogError($"Ambiguous match for method {methodName} of type {type.FullName}");
                throw;
            }

            var node = AddNode($"{type.ToUdonGraphName()}.{method.ToUdonGraphName()}", method.GetParameters().Length + (method.IsStatic ? 0 : 1), position);
            return node;
        }

        public UdonGraphGenNode AddType<T>(Vector2? position = null) => AddType(typeof(T), position);

        public UdonGraphGenNode AddType(Type type, Vector2? position = null)
        {
            return AddNode($"Type_{type.ToUdonGraphName()}", 0, position);
        }

        public UdonGraphGenNode AddConst<T>(T value, Vector2? position = null) => AddConst(typeof(T), value, position);

        public UdonGraphGenNode AddConst(Type type, object value, Vector2? position = null)
        {
            var node = AddNode($"Const_{type.ToUdonGraphName()}", 1, position);
            node.SetValue(0, value, type);
            return node;
        }
        public UdonGraphGenNode AddConstThis(Vector2? position = null)
        {
            var node = AddNode($"Const_This", 1, position);
            return node;
        }

        public UdonGraphGenGroup AddGroup(string name)
        {
            UdonGraphElementData[] data = new UdonGraphElementData[_program.graphElementData.Length + 1];
            for (int i = 0; i < _program.graphElementData.Length; i++) data[i] = _program.graphElementData[i];

            var group = new UdonGraphGenGroup { title = name };

            group.PrepareSave(data[data.Length - 1] = new(UdonGraphElementType.UdonGroup, group.uid, group.ToJson()));
            _program.graphElementData = data;
            return group;
        }

        public UdonGraphGenComment AddComment(string name, Rect rect)
        {
            UdonGraphElementData[] data = new UdonGraphElementData[_program.graphElementData.Length + 1];
            for (int i = 0; i < _program.graphElementData.Length; i++) data[i] = _program.graphElementData[i];

            var comment = new UdonGraphGenComment { title = name, layout = rect };

            comment.PrepareSave(data[data.Length - 1] = new(UdonGraphElementType.UdonComment, comment.uid, comment.ToJson()));
            _program.graphElementData = data;
            currentGroup?.Add(comment);
            return comment;
        }

        public void DeleteGroup(UdonGraphGenGroup group)
        {
            Nodes.Where(n => group.containedElements.Contains(n.Guid)).ForEach(n => n.Delete());

            List<UdonGraphElementData> datas = new();
            foreach (var data in _program.graphElementData)
            {
                if (data.type == UdonGraphElementType.UdonGroup && UdonGraphGenGroup.FromData(data).uid == group.uid)
                {
                    continue;
                }

                if (group.containedElements.Contains(data.uid)) continue;
                datas.Add(data);
            }

            _program.graphElementData = datas.ToArray();
        }

        public UdonGraphGenNode GetNode(string guid)
        {
            return Nodes.FirstOrDefault(n => n.Guid == guid);
        }

        public UdonGraphGenVariable GetVariable(string guid)
        {
            return Variables.FirstOrDefault(n => n.Guid == guid);
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
        }

        public UdonGraphGenVariable(UdonGraphGenNode node) : base(node)
        {
        }

        public override void Validate()
        {
            if (!_node.IsVariable()) throw new ArgumentException($"Node is not a variable: {_node.fullName}!");
        }
    }

    public class UdonGraphGenNode
    {
        protected readonly UdonNodeData _node;
        public string Guid => _node.uid;
        public string Name => _node.fullName;

        public string[] Values
        {
            get
            {
                string[] values = new string[_node.nodeValues.Length];
                for (int i = 0; i < values.Length; i++) values[i] = _node.nodeValues[i].stringValue;
                return values;
            }
        }

        public Vector2 Position => _node.position;

        public UdonGraphGenNode(UdonNodeData node)
        {
            _node = node;
            Validate();
        }

        public UdonGraphGenNode(UdonGraphGenNode node)
        {
            _node = node._node;
            Validate();
        }

        public virtual void Validate()
        {
        }

        public void Delete()
        {
            foreach (var node in _node.GetGraph().nodes)
            {
                node.ClearReferencesToNode(_node);
            }

            _node.GetGraph().RemoveNode(_node);
        }

        public void SetValue(int i, object value, Type type)
        {
            _node.nodeUIDs[i] = default;
            _node.nodeValues[i] = SerializableObjectContainer.Serialize(value, type);
        }

        public void SetValue(int i, string value) => SetValue(i, value, typeof(string));
        public void SetValue(int i, bool value) => SetValue(i, value, typeof(bool));
        public void SetValue(int i, int value) => SetValue(i, value, typeof(int));

        public void SetValue(int i, UdonGraphGenNode node)
        {
            _node.nodeUIDs[i] = node.Guid;
            _node.nodeValues[i] = SerializableObjectContainer.Serialize(default);
        }

        public void ClearValue(int i)
        {
            _node.nodeUIDs[i] = default;
            _node.nodeValues[i] = SerializableObjectContainer.Serialize(default);
        }

        public UdonGraphGenNode[] GetFlowTargets(UdonGraphGen generator)
        {
            UdonGraphGenNode[] targets = new UdonGraphGenNode[_node.flowUIDs.Length];
            for (int i = 0; i < targets.Length; i++) targets[i] = generator.GetNode(_node.flowUIDs[i]);
            return targets;
        }

        public UdonGraphGenNode[] GetNodeInputs(UdonGraphGen generator)
        {
            UdonGraphGenNode[] inputs = new UdonGraphGenNode[_node.nodeUIDs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                var guid = _node.nodeUIDs[i];
                if (guid == null) continue;
                if (guid.Contains('|')) guid = guid.Split('|')[0];
                inputs[i] = generator.GetNode(guid);
                if (!string.IsNullOrEmpty(guid) && inputs[i] == null) throw new Exception($"Could not find node {guid}!");
            }

            return inputs;
        }

        public void AddFlow(params UdonGraphGenNode[] nodes)
        {
            List<string> flow = new List<string>();
            foreach (var uid in _node.flowUIDs) flow.Add(uid);

            foreach (var node in nodes) flow.Add(node.Guid);

            _node.flowUIDs = flow.ToArray();
        }

        public void InsertFlowTarget(int index, UdonGraphGenNode node) => InsertFlowTarget(index, node.Guid);

        public void InsertFlowTarget(int index, string guid)
        {
            List<string> flow = new List<string>();
            foreach (var uid in _node.flowUIDs) flow.Add(uid);

            while (flow.Count < index) flow.Add(null);
            flow.Insert(index, guid);

            _node.flowUIDs = flow.ToArray();
        }
    }

    public class UdonGraphGenCustomEvent : UdonGraphGenNode
    {
        public string Name { get => _node.nodeValues[0].GetAsString(); set => _node.nodeValues[0].Set(value); }
        public int RateLimit { get => _node.nodeValues[1].GetAsInt(); set => _node.nodeValues[1].Set(value); }

        public UdonGraphGenCustomEvent(UdonNodeData node) : base(node)
        {
        }

        public UdonGraphGenCustomEvent(UdonGraphGenNode node) : base(node)
        {
        }

        public override void Validate()
        {
            if (!_node.IsCustomEvent()) throw new ArgumentException($"Node is not a custom event: {_node.fullName}!");
        }
    }

    public class UdonGraphGenBlock : UdonGraphGenNode
    {
        public UdonGraphGenBlock(UdonNodeData node) : base(node)
        {
        }

        public UdonGraphGenBlock(UdonGraphGenNode node) : base(node)
        {
        }

        public override void Validate()
        {
            if (_node.fullName != "Block") throw new ArgumentException($"Node is not a block event: {_node.fullName}!");
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

    public static class TypeExtensions
    {
        public static string ToUdonGraphName(this Type type)
        {
            if (type == typeof(UdonBehaviour)) type = typeof(IUdonEventReceiver); // This is what VRChat uses for UdonBehavior variables
            return type.FullName.Replace(".", "").Replace("[]", "Array");
        }

        public static string ToUdonGraphName(this MethodInfo method)
        {
            List<string> parameters = new();
            foreach (var param in method.GetParameters()) parameters.Add(param.ParameterType.ToUdonGraphName());
            return $"__{method.Name}__{string.Join("_", parameters)}__{method.ReturnType.ToUdonGraphName()}";
        }
    }

    [Serializable]
    public class UdonGraphGenElement
    {
        public string uid = Guid.NewGuid().ToString();
        public Rect layout;
        public string title;
        public int layer;
        public Color elementTypeColor;

        private UdonGraphElementData _data;

        public string ToJson() => JsonUtility.ToJson(this);
        public void PrepareSave(UdonGraphElementData data) => _data = data;
        public void Save() => _data.jsonData = ToJson();
    }

    [Serializable]
    public class UdonGraphGenGroup : UdonGraphGenElement
    {
        public List<string> containedElements = new();
        public UdonGraphGenGroup() => layer = -1;

        public static UdonGraphGenGroup FromData(UdonGraphElementData data)
        {
            if (data.type != UdonGraphElementType.UdonGroup) throw new ArgumentException("Cannot create UdonGraphGenGroup from data that is not a group!");
            var group = JsonUtility.FromJson<UdonGraphGenGroup>(data.jsonData);
            group.PrepareSave(data);
            return group;
        }

        public void Add(UdonGraphGenNode node)
        {
            containedElements.Add(node.Guid);
            Save();
        }

        public void Add(UdonGraphGenElement element)
        {
            containedElements.Add(element.uid);
            Save();
        }

        public IEnumerable<UdonGraphGenNode> GetContainedNodes(UdonGraphGen generator) => generator.Nodes.Where(Contains);

        public bool Contains(UdonGraphGenNode node) => containedElements.Contains(node.Guid);
    }

    [Serializable]
    public class UdonGraphGenComment : UdonGraphGenElement
    {
        public UdonGraphGenComment() => layer = 0;

        public static UdonGraphGenComment FromData(UdonGraphElementData data)
        {
            if (data.type != UdonGraphElementType.UdonComment) throw new ArgumentException("Cannot create UdonGraphGenComment from data that is not a comment!");
            var comment = JsonUtility.FromJson<UdonGraphGenComment>(data.jsonData);
            comment.PrepareSave(data);
            return comment;
        }
    }
}