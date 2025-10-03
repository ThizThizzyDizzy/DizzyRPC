using DizzyRPC.Attribute;
using UdonSharp;
using VRRefAssist;

namespace DizzyRPC.Examples
{
    [Singleton]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RPCHookExample : UdonSharpBehaviour
    {
        public bool Enabled = true;

        [RPCHook(typeof(SingletonRPCExample), nameof(SingletonRPCExample._ExampleWithParameters))]
        public bool CheckMethod(int parameter)
        {
            return !Enabled || parameter == 2;
        }
        [RPCHook(typeof(RoutedRPCExample), nameof(RoutedRPCExample._SomeRPC))]
        public bool Check2Method(string message)
        {
            return true;
        }
    }
}