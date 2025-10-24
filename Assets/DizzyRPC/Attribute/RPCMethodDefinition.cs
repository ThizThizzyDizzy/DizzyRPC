/*
 * Copyright (C) 2025 ThizThizzyDizzu (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using System;

namespace DizzyRPC.Attribute
{
    public interface RPCMethodDefinition
    {
        int RateLimitPerSecond { get; }
        bool EnforceSecure { get; }
        bool AllowDropping { get; }
        bool RequireLowLatency { get; }
        bool IgnoreDuplicates { get; }
        RPCSyncMode Mode { get; }
    }

    public static class RPCMethodDefinitionExtensions
    {
        public static RPCSyncMode CalculateMode(this RPCMethodDefinition method)
        {
            var mode = method.Mode;
            if (mode == RPCSyncMode.Automatic)
            {
                mode = RPCSyncMode.Variable; // Safe default, much higher bandwidth limit
                // Figure out what kind of RPC this is supposed to be
                if (!method.AllowDropping) mode = RPCSyncMode.Event; // Events are never skipped, variables might be
                if (method.RequireLowLatency) mode = RPCSyncMode.Variable; // Variables sync instantly, events are roughly every second
                if (method.IgnoreDuplicates) mode = RPCSyncMode.Variable; // Events are controlled by VRC, this can only be ensured on variable syncs //TODO actually this may be doable
                if (method.EnforceSecure) mode = RPCSyncMode.Event; // Events are only sent to the target player, variables are broadcast to all
            }

            // Validate settings
            if (method.EnforceSecure && mode != RPCSyncMode.Event) throw new ArgumentException("RPC Method with enforceSecure enabled must have a RPCSyncMode of Event!");
            if (method.RequireLowLatency && mode != RPCSyncMode.Variable) throw new ArgumentException("RPC Method with requireLowLatency enabled must have a RPCSyncMode of Variable!");
            if (method.IgnoreDuplicates && mode != RPCSyncMode.Variable) throw new ArgumentException("RPC Method with ignoreDuplicates enabled must have a RPCSyncMode of Variable!");
            if (method.AllowDropping == false && mode != RPCSyncMode.Event) throw new ArgumentException("RPC Method with allowDropping disabled must have a RPCSyncMode of Event!");

            return mode;
        }
    }

    public enum RPCSyncMode
    {
        Automatic,
        Event,
        Variable
    }
}