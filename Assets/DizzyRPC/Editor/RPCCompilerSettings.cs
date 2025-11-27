/*
 * Copyright (C) 2025 ThizThizzyDizzu (https://www.thizthizzydizzy.com)
 *
 * This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
 * If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

using UnityEditor;
using UnityEngine;

namespace DizzyRPC.Editor
{
    public class RPCCompilerSettings : EditorWindow
    {
        public static bool AutoRecompileInEditor { get => EditorPrefs.GetBool("DizzyRPC_AutoRebuildEditor", false); set => EditorPrefs.SetBool("DizzyRPC_AutoRebuildEditor", value); }
        public static bool AutoRecompileForPlayMode { get => EditorPrefs.GetBool("DizzyRPC_AutoRecompileForPlayMode", true); set => EditorPrefs.SetBool("DizzyRPC_AutoRecompileForPlayMode", value); }
        public static bool AutoRecompileOnBuild { get => EditorPrefs.GetBool("DizzyRPC_AutoRecompileOnBuild", true); set => EditorPrefs.SetBool("DizzyRPC_AutoRecompileOnBuild", value); }

        [MenuItem("Tools/DizzyRPC/Settings")]
        public static void ShowWindow()
        {
            var window = GetWindow<RPCCompilerSettings>("DizzyRPC Settings");
            window.minSize = window.maxSize = new Vector2(375, 300);
        }

        private void OnGUI()
        {
            GUILayout.Label("DizzyRPC Compiler Settings", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, alignment = TextAnchor.MiddleLeft });
            EditorGUILayout.HelpBox("When RPCs are compiled in EDITOR MODE:\n- They have no functionality\n- You can make code changes without causing errors.", MessageType.Info, true);
            EditorGUILayout.HelpBox("When RPCs are compiled in BUILD MODE:\n- They are fully functional\n- Modifications may cause errors in generated code.", MessageType.Info, true);
            AutoRecompileInEditor = DrawSettingToggle(
                "Auto Recompile RPCs in Editor",
                "Automatically recompile RPCs in EDITOR MODE when changes are made or when the editor loads.\nNOTE: You may need to manually reload the file in your code editor to see the changes.",
                AutoRecompileInEditor
            );
            AutoRecompileForPlayMode = DrawSettingToggle(
                "Auto Recompile RPCs for Play Mode",
                "Automatically recompile RPCs in BUILD MODE before entering Play Mode.\nAutomatically recompile RPCs in EDITOR MODE after exiting Play Mode.",
                AutoRecompileForPlayMode
            );
            AutoRecompileOnBuild = DrawSettingToggle(
                "Auto Recompile RPCs on Build",
                "Automatically recompile RPCs in BUILD MODE when building.",
                AutoRecompileOnBuild
            );
        }

        private bool DrawSettingToggle(string title, string description, bool currentValue)
        {
            var originalColor = GUI.backgroundColor;

            GUI.backgroundColor = currentValue
                ? new Color(0.5f, 1.0f, 0.5f, 1.0f)
                : new Color(1.0f, 0.5f, 0.5f, 1.0f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(title, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            var newValue = EditorGUILayout.Toggle(currentValue, GUILayout.Width(20));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();

            GUI.backgroundColor = originalColor;
            return newValue;
        }
    }
}