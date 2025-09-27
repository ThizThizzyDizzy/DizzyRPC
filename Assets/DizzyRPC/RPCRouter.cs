using UdonSharp;
using VRC.Udon;

namespace DizzyRPC
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class RPCRouter<T, I> : UdonSharpBehaviour where T : UdonSharpBehaviour
    {
        public abstract T _Route(I id);
        public abstract I _GetId(T routedObject);
    }
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class RPCRouter<I> : UdonSharpBehaviour
    {
        public abstract UdonBehaviour _Route(I id);
        public abstract I _GetId(UdonBehaviour routedObject);
    }
}