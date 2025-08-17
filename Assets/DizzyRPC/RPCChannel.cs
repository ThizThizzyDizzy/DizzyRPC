using System;
using DizzyRPC.Attribute;
using DizzyRPC.Examples;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace DizzyRPC
{
    [GenerateRPCs]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class RPCChannel : UdonSharpBehaviour
    {
        private VRCPlayerApi localPlayer;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
        }

        public void Send(int id, params object[] parameters)
        {
            switch (parameters.Length)
            {
                case 0:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}");
                    return;
                case 1:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0]);
                    return;
                case 2:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1]);
                    return;
                case 3:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2]);
                    return;
                case 4:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3]);
                    return;
                case 5:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3], parameters[4]);
                    return;
                case 6:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5]);
                    return;
                case 7:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5], parameters[6]);
                    return;
                case 8:
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, $"RPC_{id}", parameters[0], parameters[1], parameters[2], parameters[3], parameters[4], parameters[5], parameters[6], parameters[7]);
                    return;
            }

            Debug.LogError($"[DizzyRPC] Skipping RPC {id} because it had too many parameters!");
        }

        #region Generated RPCs (DO NOT EDIT)
        [UnityEngine.SerializeField] private DizzyRPC.Examples.SingletonRPCExample singleton_0;
        [UnityEngine.SerializeField] private DizzyRPC.Examples.RPCHookExample singleton_1;
        
        [UnityEngine.SerializeField] private DizzyRPC.Examples.RPCRouterExample router_0;
        
        public const int RPC_RoutedRPCExample_SomeRPC = 0;
        public const int RPC_SingletonRPCExample_Example = 1;
        public const int RPC_SingletonRPCExample_ExampleWithParameters = 2;
        
        [VRC.SDK3.UdonNetworkCalling.NetworkCallable]
        public void RPC_0(System.Int32 _id, System.String message) {
            router_0._Route(_id).SomeRPC(message);
        }
        [VRC.SDK3.UdonNetworkCalling.NetworkCallable]
        public void RPC_1() {
            singleton_0.Example();
        }
        [VRC.SDK3.UdonNetworkCalling.NetworkCallable]
        public void RPC_2(System.Int32 parameter) {
            if (!singleton_1.CheckMethod(parameter)) return;
            singleton_0.ExampleWithParameters(parameter);
        }
        #endregion
    }
}