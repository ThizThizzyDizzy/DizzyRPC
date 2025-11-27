/*
 * Copyright (C) 2025 ThizThizzyDizzy (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

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