using DizzyRPC.Attribute;
using VRC.Udon;
using VRRefAssist;

namespace DizzyRPC.Examples
{
    [Singleton]
    [RPCGraphRouter("GraphRPCExample")]
    public class RPCGraphRouterExample : RPCRouter<int>
    {
        public UdonBehaviour[] routedObjects;

        public override UdonBehaviour _Route(int id)
        {
            return routedObjects[id];
        }

        public override int _GetId(UdonBehaviour routedObject)
        {
            for (int i = 0; i < routedObjects.Length; i++)
            {
                if (routedObjects[i] == routedObject) return i;
            }

            return -1;
        }
    }
}