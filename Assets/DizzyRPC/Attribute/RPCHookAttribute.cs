/*
 * Copyright (C) 2025 ThizThizzyDizzy (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

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