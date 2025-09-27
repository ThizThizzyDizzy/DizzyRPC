using UnityEditor;

namespace DizzyRPC.Editor
{
    [CustomEditor(typeof(RPCGraphDataObject))]
    public class RPCGraphDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("This object stores DizzyRPC metadata for Udon Graph programs. Select an Udon Graph Program Asset to configure DizzyRPC for that program.", MessageType.Info, true);
        }
    }
}