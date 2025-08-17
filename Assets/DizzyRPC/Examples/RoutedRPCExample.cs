using System;
using DizzyRPC.Attribute;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;

namespace DizzyRPC.Examples
{
    [GenerateRPCs]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RoutedRPCExample : UdonSharpBehaviour
    {
        [RPCMethod]
        [NetworkCallable]
        public void SomeRPC(string message)
        {
            Debug.Log($"[DizzyRPC] {gameObject.name}: Received {message}");
        }

        private void Update()
        {
            _Send_SomeRPC(null, $"Testing from {gameObject.name}");
        }
        #region Generated RPCs (DO NOT EDIT)
        [UnityEngine.SerializeField] private DizzyRPC.RPCManager _rpc_manager;
        [UnityEngine.SerializeField] private DizzyRPC.Examples.RPCRouterExample _rpc_router;
        
        public void _Send_SomeRPC(VRC.SDKBase.VRCPlayerApi target, System.String message) => _rpc_manager.Send(target, DizzyRPC.RPCChannel.RPC_RoutedRPCExample_SomeRPC, _rpc_router._GetId(this), message);
        #endregion
    }
}