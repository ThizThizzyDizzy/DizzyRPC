/*
 * Copyright (C) 2025 ThizThizzyDizzy (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using UdonSharp;
using VRC.Udon;

namespace DizzyRPC
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class RPCRouter<T, I> : UdonSharpBehaviour where T : UdonSharpBehaviour
    {
        public abstract T _Route(I id);
        public abstract I _GetId(T routedObject);
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public abstract class RPCRouter<I> : UdonSharpBehaviour
    {
        public abstract UdonBehaviour _Route(I id);
        public abstract I _GetId(UdonBehaviour routedObject);
    }
}