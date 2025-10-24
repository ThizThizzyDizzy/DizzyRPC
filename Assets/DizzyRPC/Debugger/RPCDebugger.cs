/*
 * Copyright (C) 2025 ThizThizzyDizzu (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using UdonSharp;
using UnityEngine;
using VRC.SDK3.Network;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;
using VRRefAssist;

namespace DizzyRPC.Debugger
{
    [Singleton]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RPCDebugger : UdonSharpBehaviour
    {
        [SerializeField] private GameObject graphPrefab;

        // Global graphs
        private RPCDebuggerGraph sentVariables;
        private RPCDebuggerGraph sentVariableBytes;
        private RPCDebuggerGraph receivedVariables;
        private RPCDebuggerGraph totalQueuedEvents;
        private RPCDebuggerGraph rpcPerSerialization;

        private RPCDebuggerGraph networkClogged;
        private RPCDebuggerGraph networkSuffering;
        private RPCDebuggerGraph throughputPercentage;
        private RPCDebuggerGraph totalBytesPerSecond;

        // Per channel
        private RPCDebuggerGraph[] variableSyncLatency = new RPCDebuggerGraph[0];
        private RPCDebuggerGraph[] queuedEvents = new RPCDebuggerGraph[0];
        private RPCDebuggerGraph[] bytesPerSecond = new RPCDebuggerGraph[0];

        private int numSentVariables = 0;
        private int numSentVariableBytes = 0;
        private int numReceivedVariables = 0;

        private RPCDebuggerGraph CreateGraph(int x, int y, string title)
        {
            var graph = Instantiate(graphPrefab, transform).GetComponent<RPCDebuggerGraph>();
            graph.title.text = title;
            graph.transform.localPosition = new Vector3(x, y, 0);
            return graph;
        }

        private void Start()
        {
            rpcPerSerialization = CreateGraph(0, 2, "RPCs per serialization");
            totalBytesPerSecond = CreateGraph(0, 1, "Total Bytes Per Second");
            totalQueuedEvents = CreateGraph(0, 0, "Total Queued Events");

            receivedVariables = CreateGraph(-1, 2, "Received Serializations");
            sentVariables = CreateGraph(-1, 1, "Sent Serializations");
            sentVariableBytes = CreateGraph(-1, 0, "Sent Serialization Bytes");

            networkClogged = CreateGraph(-2, 2, "Clogged");
            networkSuffering = CreateGraph(-2, 1, "Suffering");
            throughputPercentage = CreateGraph(-2, 0, "Bandwidth Usage");
        }

        private void Update()
        {
            totalQueuedEvents.Add(NetworkCalling.GetAllQueuedEvents() / 100f);
            totalBytesPerSecond.Add(Stats.BytesOutAverage / Stats.BytesOutMax);
            int i = 0;
            foreach (var player in VRCPlayerApi.GetPlayers(new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]))
            {
                foreach (var playerObject in Networking.GetPlayerObjects(player))
                {
                    if (!Utilities.IsValid(playerObject)) continue;
                    var channel = playerObject.GetComponentInChildren<RPCChannel>();
                    if (!Utilities.IsValid(channel)) continue;
                    if (queuedEvents.Length <= i)
                    {
                        var newChannelGraphs = new RPCDebuggerGraph[queuedEvents.Length + 1];
                        for (int idx = 0; idx < queuedEvents.Length; idx++) newChannelGraphs[idx] = queuedEvents[idx];
                        queuedEvents = newChannelGraphs;
                        queuedEvents[i] = CreateGraph(i + 1, 0, $"QE Channel {i}");
                    }

                    queuedEvents[i].title.text = $"QE Channel {i}:\n{player.displayName} ({player.playerId})";
                    queuedEvents[i].player = player;
                    queuedEvents[i].Add(NetworkCalling.GetQueuedEvents((IUdonEventReceiver)channel, "RPC_0") / 100f);

                    if (bytesPerSecond.Length <= i)
                    {
                        var newChannelGraphs = new RPCDebuggerGraph[bytesPerSecond.Length + 1];
                        for (int idx = 0; idx < bytesPerSecond.Length; idx++) newChannelGraphs[idx] = bytesPerSecond[idx];
                        bytesPerSecond = newChannelGraphs;
                        bytesPerSecond[i] = CreateGraph(i + 1, 1, $"BPS Channel {i}");
                    }

                    bytesPerSecond[i].title.text = $"BPS Channel {i}:\n{player.displayName} ({player.playerId})";
                    bytesPerSecond[i].player = player;
                    bytesPerSecond[i].Add(Stats.BytesPerSecondAverage(channel.gameObject) / Stats.BytesOutMax);

                    if (variableSyncLatency.Length <= i)
                    {
                        var newChannelGraphs = new RPCDebuggerGraph[variableSyncLatency.Length + 1];
                        for (int idx = 0; idx < variableSyncLatency.Length; idx++) newChannelGraphs[idx] = variableSyncLatency[idx];
                        variableSyncLatency = newChannelGraphs;
                        variableSyncLatency[i] = CreateGraph(i + 1, 2, "Serialization Latency");
                    }

                    variableSyncLatency[i].title.text = $"Serialization Latency";
                    variableSyncLatency[i].player = player;
                    i++;
                }
            }

            for (int j = i; j < queuedEvents.Length; j++)
            {
                queuedEvents[j].title.text = $"(Channel {j}:\nDisconnected)";
                queuedEvents[j].Add(0);
            }

            for (int j = i; j < bytesPerSecond.Length; j++)
            {
                queuedEvents[j].title.text = $"(Channel {j}:\nDisconnected)";
                queuedEvents[j].Add(0);
            }

            for (int j = i; j < variableSyncLatency.Length; j++)
            {
                variableSyncLatency[j].title.text = $"(Channel {j}:\nDisconnected)";
                variableSyncLatency[j].Add(0);
            }

            networkClogged.Add(Networking.IsClogged ? 1 : 0);
            networkSuffering.Add(Stats.Suffering / 100f);

            sentVariables.Add(numSentVariables / 16f);
            sentVariableBytes.Add(numSentVariableBytes / 280496f);
            receivedVariables.Add(numReceivedVariables / 16f);

            numSentVariables = numSentVariableBytes = numReceivedVariables = 0;

            throughputPercentage.Add(Stats.ThroughputPercentage);
        }

        public void _OnSendRPC(VRCPlayerApi target, int id, params object[] parameters)
        {
        }

        public void _OnReceiveRPC(VRCPlayerApi sender, int id, params object[] parameters)
        {
        }

        public void OnVariableSyncSent(SerializationResult result)
        {
            numSentVariables++;
            numSentVariableBytes += result.byteCount;
        }

        public void OnVariableSyncReceived(VRCPlayerApi player, float sendTime, float receiveTime, int rpcCount)
        {
            numReceivedVariables++;
            rpcPerSerialization.Add(rpcCount / 32f);
            foreach (var graph in variableSyncLatency)
                if (graph.player == player)
                    graph.Add(receiveTime - sendTime);
        }
    }
}