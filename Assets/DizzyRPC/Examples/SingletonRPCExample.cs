using System;
using DizzyRPC.Attribute;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRRefAssist;

namespace DizzyRPC.Examples
{
    [Singleton]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SingletonRPCExample : UdonSharpBehaviour
    {
        [RPCMethod(mode:RPCSyncMode.Event)]
        public void _Example()
        {
            Debug.Log("[DizzyRPC] Singleton RPC Example - Hello, world!");
        }

        [RPCMethod(mode:RPCSyncMode.Event)]
        public void _ExampleWithParameters(int parameter)
        {
            Debug.Log($"[DizzyRPC] Singleton RPC Example - Hello, world! - {parameter}");
        }

        [RPCMethod(mode: RPCSyncMode.Variable)]
        public void _ExampleVariableRPC(int parameter)
        {
            Debug.Log($"[DizzyRPC] Variable RPC Example - Hello, world! - {parameter}");
        }

        [RPCHook("GraphRPCExample", "_asdf")]
        public bool _HookTest(string param1)
        {
            Debug.Log($"[DizzyRPC] Hook Test: {param1}");
            return true;
        }

        private int seconds = 0;
        private int i = 0;

        private void Update()
        {
            if ((int)Time.time > seconds || Input.GetKey(KeyCode.T))
            {
                seconds = (int)Time.time;
                _Send_ExampleWithParameters(null, i++);
                _Send_Example(null);
                if (i > 4)
                {
                    i = 0;
                }
            }

            if (Input.GetKey(KeyCode.V)) _Send_ExampleVariableRPC(null, seconds);
        }
        #region Generated RPCs (DO NOT EDIT)
        [UnityEngine.SerializeField] private DizzyRPC.RPCManager _rpc_manager;
        
        public void _Send_Example(VRC.SDKBase.VRCPlayerApi target) {
        }
        public void _Send_ExampleWithParameters(VRC.SDKBase.VRCPlayerApi target, System.Int32 parameter) {
        }
        public void _Send_ExampleVariableRPC(VRC.SDKBase.VRCPlayerApi target, System.Int32 parameter) {
        }
        #endregion
    }
}