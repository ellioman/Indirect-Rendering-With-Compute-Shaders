using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class _Example : MonoBehaviour
{
	public int numberOfInstancesPerType;
	public IndirectRenderer indirectRenderer;
	public List<IndirectInstanceData> instances = new List<IndirectInstanceData>();

	// Use this for initialization
	private void Awake()
	{
		InitializeData();
		indirectRenderer.AddInstances(instances);
	}

	private void Start()
	{
		indirectRenderer.StartDrawing();
	}

	private void InitializeData()
	{
		for (int i = 0; i < instances.Count; i++)
		{
			instances[i].positions = new Vector3[numberOfInstancesPerType];
			instances[i].rotations = new Vector3[numberOfInstancesPerType];
			instances[i].uniformScales = new float[numberOfInstancesPerType];

			for (int k = 0; k < numberOfInstancesPerType; k++)
			{
				instances[i].positions[k] = new Vector3(Random.Range(-2000f, 2000f), 15f, Random.Range(-1000f, 1000f));
				instances[i].rotations[k] = new Vector3(0f, 0f, 0f);
				instances[i].uniformScales[k] = 0.25f;// Random.Range(0.20f, 0.3f);
			}
		}
	}

	private void OnGUI()
	{
		GUI.Label(new Rect(5f, 5f, 1000f, 200f), "Hold mouse button down and WASD to move camera");
	}
}
