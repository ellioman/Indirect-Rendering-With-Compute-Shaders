using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class IndirectInstanceData
{
    public GameObject prefab;
    public Vector2 scaleRange = new Vector2();
    public Vector3 positionOffset = new Vector3();
    public Mesh lod00Mesh;
    public Mesh lod01Mesh;
    public Mesh lod02Mesh;
    public Mesh shadowMesh;
    public Material material;
    public Vector3[] rotations;
    public Vector3[] positions;
    public float[] uniformScales;
}
