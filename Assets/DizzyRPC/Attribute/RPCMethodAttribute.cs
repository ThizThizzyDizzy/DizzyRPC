using System;

namespace DizzyRPC.Attribute
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RPCMethodAttribute : System.Attribute
    {
        public readonly int rateLimitPerSecond;
        public readonly bool enforceSecure;
        public readonly bool allowDropping;
        public readonly bool requireLowLatency;
        public readonly RPCSyncMode mode;

        public RPCMethodAttribute(int rateLimitPerSecond = -1, bool enforceSecure = false, bool allowDropping = true, bool requireLowLatency = false, RPCSyncMode mode = RPCSyncMode.Automatic)
        {
            this.rateLimitPerSecond = rateLimitPerSecond;
            this.enforceSecure = enforceSecure;
            this.allowDropping = allowDropping;
            this.requireLowLatency = requireLowLatency;
            this.mode = mode;
        }
    }
        
    public enum RPCSyncMode
    {
        Automatic,
        Event,
        Variable
    }
}