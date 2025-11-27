/*
 * Copyright (C) 2025 ThizThizzyDizzu (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using UdonSharp;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRRefAssist;

namespace DizzyRPC
{
    [Singleton]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RPCManager : UdonSharpBehaviour
    {
        public int[] playerIds = new int[1024];
        public RPCChannel[] channels = new RPCChannel[1024];
        public bool[] cached = new bool[1024];

        public void _SendEvent(VRCPlayerApi target, ushort id, params object[] parameters)
        {
            // Debug.Log("[RPCManager] Sending Event of id " + id + " with " + parameters.Length + " parameters!");
            if (target == null)
            {
                foreach (var player in VRCPlayerApi.GetPlayers(new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]))
                {
                    foreach (var playerObject in Networking.GetPlayerObjects(player))
                    {
                        if (!Utilities.IsValid(playerObject)) continue;
                        var chan = playerObject.GetComponentInChildren<RPCChannel>();
                        if (!Utilities.IsValid(chan)) continue;
                        chan.SendEvent(id, parameters);
                    }
                }

                return;
            }

            RPCChannel channel = null;
            bool found = false;
            for (int i = 0; i < cached.Length; i++)
            {
                if (!cached[i]) continue;
                if (playerIds[i] == target.playerId)
                {
                    channel = channels[i];
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                foreach (var playerObject in Networking.GetPlayerObjects(target))
                {
                    if (!Utilities.IsValid(playerObject)) continue;
                    channel = playerObject.GetComponentInChildren<RPCChannel>();
                    if (!Utilities.IsValid(channel)) continue;
                    break;
                }

                for (int i = 0; i < cached.Length; i++)
                {
                    if (cached[i]) continue;
                    playerIds[i] = target.playerId;
                    channels[i] = channel;
                    cached[i] = true;
                }
            }

            channel.SendEvent(id, parameters);
        }

        public void _SendVariable(VRCPlayerApi target, ushort id, bool ignoreDuplicates, params object[] parameters)
        {
            foreach (var playerObject in Networking.LocalPlayer.GetPlayerObjects())
            {
                if (!Utilities.IsValid(playerObject)) continue;
                var chan = playerObject.GetComponentInChildren<RPCChannel>();
                if (!Utilities.IsValid(chan)) continue;
                chan.SendVariable(target, id, ignoreDuplicates, parameters);
            }
        }

        public VRCPlayerApi _graph_target;
        public ushort _graph_id;
        public DataList _graph_parameters;

        public void _Graph_SendEvent()
        {
            _SendEvent(_graph_target, _graph_id, ToObjectArray(_graph_parameters));
        }
        public void _Graph_SendVariable()
        {
            _SendVariable(_graph_target, _graph_id, false, ToObjectArray(_graph_parameters));//TODO ignoreDuplicates
        }

        private object[] ToObjectArray(DataList list)
        {
            if (list == null) return new object[0];
            object[] arr = new object[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = ExtractValue(list[i]);
            return arr;
        }
        private object ExtractValue(DataToken token)
        {
            switch (token.TokenType)
            {
                case TokenType.Null:
                    return null;
                case TokenType.Boolean:
                    return token.Boolean;
                case TokenType.SByte:
                    return token.SByte;
                case TokenType.Byte:
                    return token.Byte;
                case TokenType.Short:
                    return token.Short;
                case TokenType.UShort:
                    return token.UShort;
                case TokenType.Int:
                    return token.Int;
                case TokenType.UInt:
                    return token.UInt;
                case TokenType.Long:
                    return token.Long;
                case TokenType.ULong:
                    return token.ULong;
                case TokenType.Float:
                    return token.Float;
                case TokenType.Double:
                    return token.Double;
                case TokenType.String:
                    return token.String;
                case TokenType.DataList:
                    return token.DataList;
                case TokenType.DataDictionary:
                    return token.DataDictionary;
                case TokenType.Reference:
                    return token.Reference;
                default:
                    return null;
            }
        }
    }
}