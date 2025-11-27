/*
 * Copyright (C) 2025 ThizThizzyDizzy (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace DizzyRPC.Debugger
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RPCDebuggerGraph : UdonSharpBehaviour
    {
        [SerializeField] private Transform marker;
        [SerializeField] private TrailRenderer trail;
        [SerializeField] private bool clampData;
        [SerializeField] public TextMeshPro title;

        public Vector3[] data = new Vector3[1024];
        public VRCPlayerApi player;

        private void Start()
        {
            for (int i = 0; i < data.Length; i++) data[i] = MakeVec3(i, 0);
        }

        private void Update()
        {
            trail.Clear();
            trail.AddPositions(data);
            trail.AddPosition(data[data.Length - 1] + transform.TransformVector(new Vector3(0, 0, trail.widthMultiplier * 2)));
        }

        private Vector3 MakeVec3(int i, float f)
        {
            if (clampData) f = Mathf.Clamp01(f);
            return transform.TransformPoint(new Vector3(i / (float)data.Length - .5f + Random.value / 1000f, f - .5f + Random.value / 1000f, -trail.widthMultiplier + Random.value / 1000f));
        }

        public void Add(float f)
        {
            Vector3 offset = transform.TransformVector(-1f, 0, 0) / data.Length;
            for (var i = 1; i < data.Length; i++)
            {
                data[i] += offset;
                data[i - 1] = data[i];
            }

            data[data.Length - 1] = MakeVec3(data.Length - 1, f);
        }
    }
}