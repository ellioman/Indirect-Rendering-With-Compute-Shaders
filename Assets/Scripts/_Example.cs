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
			instances[i].positions = new Vector4[numberOfInstancesPerType];
			for (int k = 0; k < numberOfInstancesPerType; k++)
			{
				instances[i].positions[k] = new Vector4(Random.Range(-2000f, 2000f), 15f, Random.Range(-1000f, 1000f), Random.Range(5f, 10f));
			}
		}
	}
}
