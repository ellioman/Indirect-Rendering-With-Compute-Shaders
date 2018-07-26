using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(_Example))]
public class _ExampleEditor : Editor
{
    public override void OnInspectorGUI()
    {        
        _Example myScript = (_Example) target;

        if (GUILayout.Button("Instantiate"))
        {
            myScript.InstantiatePrefabs();
        }

        if (GUILayout.Button("Destroy Instances"))
        {
            myScript.DestroyInstances(_Example.PARENT_OBJ_NAME);
        }

		DrawDefaultInspector();
    }
}
