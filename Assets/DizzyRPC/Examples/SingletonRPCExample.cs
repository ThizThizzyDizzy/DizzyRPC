using System;
using DizzyRPC.Attribute;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRRefAssist;

namespace DizzyRPC.Examples
{
    [Singleton]
    [GenerateRPCs]
    public class SingletonRPCExample : UdonSharpBehaviour
    {
        [RPCMethod]
        [NetworkCallable]
        public void Example()
        {
            Debug.Log("[DizzyRPC] Singleton RPC Example - Hello, world!");
        }

        [RPCMethod]
        [NetworkCallable]
        public void ExampleWithParameters(int parameter)
        {
            Debug.Log($"[DizzyRPC] Singleton RPC Example - Hello, world! - {parameter}");
        }

        private int i = 0;

        private void Update()
        {
            _Send_ExampleWithParameters(null, i++);
            if (i > 100)
            {
                i = 0;
                _Send_Example(null);
            }
        }

        #region Generated RPCs (DO NOT EDIT)
        [UnityEngine.SerializeField] private DizzyRPC.RPCManager _rpc_manager;
        
        public void _Send_Example(VRC.SDKBase.VRCPlayerApi target) => _rpc_manager.Send(target, DizzyRPC.RPCChannel.RPC_SingletonRPCExample_Example);
        public void _Send_ExampleWithParameters(VRC.SDKBase.VRCPlayerApi target, System.Int32 parameter) => _rpc_manager.Send(target, DizzyRPC.RPCChannel.RPC_SingletonRPCExample_ExampleWithParameters, parameter);
        #endregion
    }
}