/*
 * Copyright (C) 2025 ThizThizzyDizzy (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System;

namespace DizzyRPC.Attribute
{
    [AttributeUsage(AttributeTargets.Method)]
    public class RPCMethodAttribute : System.Attribute, RPCMethodDefinition
    {
        public int RateLimitPerSecond { get; }
        public bool EnforceSecure { get; }
        public bool AllowDropping { get; }
        public bool RequireLowLatency { get; }
        public bool IgnoreDuplicates { get; }
        public RPCSyncMode Mode { get; }

        public RPCMethodAttribute(int rateLimitPerSecond = -1, bool enforceSecure = false, bool allowDropping = true, bool requireLowLatency = false, bool ignoreDuplicates = false, RPCSyncMode mode = RPCSyncMode.Automatic)
        {
            RateLimitPerSecond = rateLimitPerSecond;
            EnforceSecure = enforceSecure;
            AllowDropping = allowDropping;
            RequireLowLatency = requireLowLatency;
            IgnoreDuplicates = ignoreDuplicates;
            Mode = mode;
        }
    }
}