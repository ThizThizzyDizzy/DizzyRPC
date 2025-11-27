/*
 * Copyright (C) 2025 ThizThizzyDizzy (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using UnityEditor;

namespace DizzyRPC.Editor
{
    [CustomEditor(typeof(RPCGraphDataStorage))]
    public class RPCGraphDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("This object stores DizzyRPC metadata for Udon Graph programs. Select an Udon Graph Program Asset to configure DizzyRPC for that program.", MessageType.Info, true);
        }
    }
}