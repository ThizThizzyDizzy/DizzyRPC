using System;

namespace DizzyRPC.Attribute
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RPCHookAttribute : System.Attribute
    {
        public readonly Type type;
        public readonly string methodName;

        public RPCHookAttribute(Type type, string methodName)
        {
            this.type = type;
            this.methodName = methodName;
        }
    }
}