using UdonSharp;

namespace DizzyRPC
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class RPCRouter<T, I> : UdonSharpBehaviour where T : UdonSharpBehaviour
    {
        public abstract T _Route(I id);
        public abstract I _GetId(T routedObject);
    }
}