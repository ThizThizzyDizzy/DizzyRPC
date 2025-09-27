using System;
using VRC.Udon;

namespace DizzyRPC.Attribute
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RPCHookAttribute : System.Attribute
    {
        public readonly Type type;
        public readonly string typeNameOverride;
        public readonly string methodName;

        public RPCHookAttribute(Type type, string methodName)
        {
            this.type = type;
            this.methodName = methodName;
        }
        public RPCHookAttribute(string udonBehaviorTypeName, string methodName)
        {
            type = typeof(UdonBehaviour);
            typeNameOverride = udonBehaviorTypeName;
            this.methodName = methodName;
        }

        public string FullTypeName => typeNameOverride ?? type.FullName;
    }
}