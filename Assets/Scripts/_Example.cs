using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class _Example : MonoBehaviour
{
	#region Variables

	// Public
	public float areaSize = 5000f;
	public Vector2 scaleRange = new Vector2();
	public NumberOfInstances numberOfInstances;
	public IndirectRenderer indirectRenderer;
	public List<IndirectInstanceData> instances = new List<IndirectInstanceData>();

	// Enums
	public enum NumberOfInstances
	{
		_2048 = 2048,
		_4096 = 4096,
		_8192 = 8192,
		_16384 = 16384,
		_32768 = 32768,
		_65536 = 65536,
		_131072 = 131072,
		_262144 = 262144
	}

	// Constants
	public const string PARENT_OBJ_NAME = "Parent";

	#endregion

	#region MonoBehaviour


	// Taken from:
	// http://extremelearning.com.au/unreasonable-effectiveness-of-quasirandom-sequences/
	// https://www.shadertoy.com/view/4dtBWH
	private Vector2 Nth_weyl(Vector2 p0, float n) {

		Vector2 res = p0 + n * new Vector2(0.754877669f, 0.569840296f);
		res.x %= 1;
		res.y %= 1;
		return res;
	}

	private void Start()
	{
		InitializeData();
		indirectRenderer.AddInstances(instances);
		indirectRenderer.Initialize();
	}

	private void InitializeData()
	{
		if (instances.Count == 0)
		{
			Debug.LogError("Instances list is empty!", this);
			return;
		}

		int numOfInstancesPerType = ((int) numberOfInstances) / instances.Count;
		int instanceCounter = 0;
		for (int i = 0; i < instances.Count; i++)
		{
			instances[i].positions = new Vector3[numOfInstancesPerType];
			instances[i].rotations = new Vector3[numOfInstancesPerType];
			instances[i].uniformScales = new float[numOfInstancesPerType];

			for (int k = 0; k < numOfInstancesPerType; k++)
			{
				Vector2 pos = Nth_weyl(Vector2.zero, instanceCounter++) * areaSize;
				instances[i].positions[k]  = new Vector3(pos.x - areaSize * 0.5f, 35f, pos.y - areaSize * 0.5f);
				instances[i].rotations[k] = new Vector3(0f, 0f, 0f);
				instances[i].uniformScales[k] = Random.Range(scaleRange.x, scaleRange.y);
			}
		}
	}

	#endregion

	#region Public Functions

	// public void InstantiatePrefabs()
	// {
	// 	DestroyInstances(PARENT_OBJ_NAME);
	// 	InitializeData();
	// 	Transform parent = new GameObject(PARENT_OBJ_NAME).transform;
	// 	parent.position = Vector3.zero;
	// 	parent.localScale = Vector3.one;
	// 	parent.rotation = Quaternion.identity;

	// 	for (int i = 0; i < instances.Count; i++)
	// 	{
	// 		Vector3[] positions = instances[i].positions;
	// 		for (int k = 0; k < positions.Length; k++)
	// 		{
	// 			Transform t = Instantiate(prefab);
	// 			t.parent = parent;
	// 			t.localPosition = positions[k];
	// 			t.rotation = Quaternion.Euler(instances[i].rotations[k]);
	// 			t.localScale = Vector3.one * instances[i].uniformScales[k];
	// 		}
	// 	}
	// }

	// public void DestroyInstances(string parentName)
	// {
	// 	GameObject obj = GameObject.Find(parentName);
	// 	if (obj != null)
	// 	{
	// 		DestroyImmediate(obj);
	// 	}
	// }

	#endregion
}
