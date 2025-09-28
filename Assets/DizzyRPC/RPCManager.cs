using UdonSharp;
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
        public object[] _graph_parameters;
        public void _Graph_SendEvent()
        {
            _SendEvent(_graph_target, _graph_id, _graph_parameters);
        }
        public void _Graph_SendVariable(VRCPlayerApi target, ushort id, bool ignoreDuplicates, params object[] parameters)
        {
            foreach (var playerObject in Networking.LocalPlayer.GetPlayerObjects())
            {
                if (!Utilities.IsValid(playerObject)) continue;
                var chan = playerObject.GetComponentInChildren<RPCChannel>();
                if (!Utilities.IsValid(chan)) continue;
                chan.SendVariable(target, id, ignoreDuplicates, parameters);
            }
        }
    }
}