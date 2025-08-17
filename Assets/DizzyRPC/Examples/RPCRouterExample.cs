using VRRefAssist;

namespace DizzyRPC.Examples
{
    [Singleton]
    public class RPCRouterExample : RPCRouter<RoutedRPCExample, int>
    {
        public RoutedRPCExample[] routedObjects;

        public override RoutedRPCExample _Route(int id)
        {
            return routedObjects[id];
        }

        public override int _GetId(RoutedRPCExample routedObject)
        {
            for (int i = 0; i < routedObjects.Length; i++)
            {
                if (routedObjects[i] == routedObject) return i;
            }

            return -1;
        }
    }
}