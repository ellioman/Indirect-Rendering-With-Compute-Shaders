using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class IndirectInstanceData
{
	public Mesh lod00Mesh;
	public Mesh lod01Mesh;
	public Mesh lod02Mesh;
	public Material material;
	public Vector3[] rotations;
	public Vector3[] positions;
	public float[] uniformScales;
}
