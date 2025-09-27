namespace DizzyRPC.Attribute
{
    public class RPCGraphRouterAttribute : System.Attribute
    {
        public readonly string name;

        public RPCGraphRouterAttribute(string name)
        {
            this.name = name;
        }
    }
}