using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class IndirectInstanceData
{
	public Mesh lod00Mesh;
	public Mesh lod01Mesh;
	public Mesh lod02Mesh;
	public Material lod00Material;
	public Material lod01Material;
	public Material lod02Material;
	public Vector4[] positions;
	public float lod00Range;
	public float lod01Range;
	public float lod02Range;
}
