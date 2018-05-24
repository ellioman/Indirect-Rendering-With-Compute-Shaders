using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

[System.Serializable]
public struct ComputeShaderInputData
{
    public Vector3 position;        // 3
    public Vector3 rotation;        // 6
    public float uniformScale;      // 7
    public Vector3 boundsCenter;    // 10
    public Vector3 boundsExtents;   // 13
    public float lod00Range;       // 14
    public float lod01Range;       // 15
    public float lod02Range;       // 16
}

public struct ComputeShaderOutputData
{
    Vector3 position;// 3
    Vector3 rotation;// 6
    float uniformScale;    // 7
    float unused;	// 8
};

[Serializable]
public class IndirectRenderObject
{
	private int m_numOfInstances;
	private Mesh m_lod00Mesh;
	private Mesh m_lod01Mesh;
	private Mesh m_lod02Mesh;
	private Material m_lod00Material;
	private Material m_lod01Material;
	private Material m_lod02Material;
	private MaterialPropertyBlock Lod00MatPropBlock;
	private MaterialPropertyBlock Lod01MatPropBlock;
	private MaterialPropertyBlock Lod02MatPropBlock;
	private static int materialPropertyCounter = 0;
	private ComputeShader m_computeShader = null;
	private ComputeBuffer m_positionsBuffer = null;
	private ComputeBuffer m_lod00PositionsBuffer = null;
	private ComputeBuffer m_lod01PositionsBuffer = null;
	private ComputeBuffer m_lod02PositionsBuffer = null;
	private ComputeBuffer m_lod00ArgsBuffer = null;
	private ComputeBuffer m_lod01ArgsBuffer = null;
	private ComputeBuffer m_lod02ArgsBuffer = null;
    private uint[] m_lod00Args;
	private uint[] m_lod01Args;
	private uint[] m_lod02Args;
	private int m_kernelId;
	private Vector3Int threadGroupSize = new Vector3Int();
	private int m_computeShaderNumOfInstances = 0;

	// Constants
	private const int NUM_OF_ARGS_PER_INSTANCE = 5;
	private const int COMPUTE_SHADER_INPUT_BYTE_SIZE = 16 * sizeof(float);
    private const int COMPUTE_SHADER_OUTPUT_BYTE_SIZE = 8 * sizeof(float);


	public void Initialize(ComputeShader _computeShader, IndirectInstanceData _data)// ComputeBuffer _argsBuffer, ComputeBuffer _positionBuffer, ComputeBuffer indexOffsetBuffer, int id)
    {
        Clear();

        m_computeShader = _computeShader;
		m_lod00Mesh = _data.lod00Mesh;
		m_lod01Mesh = _data.lod01Mesh;
		m_lod02Mesh = _data.lod02Mesh;

        // Arguments
        InitalizeArguments(_data);

        // Buffers
		InitializeBuffers(_data);

		// Materials
        InitializeMaterials(_data);

		// Compute Shader
		InitializeComputeShader();
    }

    private void InitializeComputeShader()
    {
		m_kernelId = m_computeShader.FindKernel("CSMain");

		uint xGroupSize;
		uint yGroupSize;
		uint zGroupSize;
		m_computeShader.GetKernelThreadGroupSizes(m_kernelId, out xGroupSize, out yGroupSize, out zGroupSize);
		threadGroupSize.x = (int) xGroupSize;
		threadGroupSize.y = (int) yGroupSize;
		threadGroupSize.z = (int) zGroupSize;

        //Debug.Log("m_computeShader.Dispatch(" + m_kernelId + ", " + (Mathf.NextPowerOfTwo(m_totalnumOfInstances) / threadGroupSize.x) + ", 1, 1);");
    }

    private void RunCompute(HiZOcclusionBufferGenerate _hiZBuffer, Matrix4x4 _MVP, Vector3 _cameraPosition, Vector3 _lightDirection, float _shadowCullingLength)
	{
		// Global Variables
		m_computeShader.SetFloat("_ShadowCullingLength", _shadowCullingLength);
		m_computeShader.SetInt("_NumberOfInstances", m_numOfInstances);
        m_computeShader.SetMatrix("_UNITY_MATRIX_MVP", _MVP);
        m_computeShader.SetVector("_HiZTextureSize", _hiZBuffer.textureSize);
        m_computeShader.SetVector("_CamPos", _cameraPosition);
        m_computeShader.SetVector("_LightDirection", _lightDirection);
		m_computeShader.SetBuffer(m_kernelId, "positionBuffer", m_positionsBuffer);
		m_computeShader.SetBuffer(m_kernelId, "lod00PositionsBuffer", m_lod00PositionsBuffer);
		m_computeShader.SetBuffer(m_kernelId, "lod01PositionsBuffer", m_lod01PositionsBuffer);
		m_computeShader.SetBuffer(m_kernelId, "lod02PositionsBuffer", m_lod02PositionsBuffer);
        m_computeShader.SetTexture(m_kernelId, "_HiZMap", _hiZBuffer.HiZDepthTexture);

		m_lod00PositionsBuffer.SetCounterValue(0);
		m_lod01PositionsBuffer.SetCounterValue(0);
		m_lod02PositionsBuffer.SetCounterValue(0);

		// Dispatch
		m_computeShader.Dispatch(m_kernelId, m_computeShaderNumOfInstances / threadGroupSize.x, 1, 1);

		//
		ComputeBuffer.CopyCount(m_lod00PositionsBuffer, m_lod00ArgsBuffer, 4);
		ComputeBuffer.CopyCount(m_lod01PositionsBuffer, m_lod01ArgsBuffer, 4);
		ComputeBuffer.CopyCount(m_lod02PositionsBuffer, m_lod02ArgsBuffer, 4);

		// Debug
		// m_lod00ArgsBuffer.GetData(m_lod00Args);
		// m_lod01ArgsBuffer.GetData(m_lod01Args);
		// m_lod02ArgsBuffer.GetData(m_lod02Args);
		// Debug.Log(m_lod00Args[1] + " vs. " + m_lod01Args[1] + " vs. " + m_lod02Args[1]);
	}

    public void Draw(HiZOcclusionBufferGenerate _hiZBuffer, Matrix4x4 _MVP, Vector3 _cameraPosition, Vector3 _lightDirection, float _shadowCullingLength, Bounds drawBounds, ShadowCastingMode shadowCastingMode, bool receiveShadows = true, float _test = 0)
	{
		m_computeShader.SetFloat("_Test", _test);
		// Compute visible objects
		RunCompute(_hiZBuffer, _MVP, _cameraPosition, _lightDirection, _shadowCullingLength);

		// Draw visible objects
		Graphics.DrawMeshInstancedIndirect(m_lod00Mesh, 0, m_lod00Material, drawBounds, m_lod00ArgsBuffer, 0, Lod00MatPropBlock, shadowCastingMode, receiveShadows);
		Graphics.DrawMeshInstancedIndirect(m_lod01Mesh, 0, m_lod01Material, drawBounds, m_lod01ArgsBuffer, 0, Lod01MatPropBlock, shadowCastingMode, receiveShadows);
		Graphics.DrawMeshInstancedIndirect(m_lod02Mesh, 0, m_lod02Material, drawBounds, m_lod02ArgsBuffer, 0, Lod02MatPropBlock, shadowCastingMode, receiveShadows);
	}

	public void Clear()
	{
		if (m_positionsBuffer != null) { m_positionsBuffer.Release(); }
		if (m_lod00PositionsBuffer != null) { m_lod00PositionsBuffer.Release(); }
		if (m_lod01PositionsBuffer != null) { m_lod01PositionsBuffer.Release(); }
		if (m_lod02PositionsBuffer != null) { m_lod02PositionsBuffer.Release(); }
		if (m_lod00ArgsBuffer != null) { m_lod00ArgsBuffer.Release(); }
		if (m_lod01ArgsBuffer != null) { m_lod01ArgsBuffer.Release(); }
		if (m_lod02ArgsBuffer != null) { m_lod02ArgsBuffer.Release(); }

		m_positionsBuffer = null;
		m_lod00PositionsBuffer = null;
		m_lod01PositionsBuffer = null;
		m_lod02PositionsBuffer = null;
		m_lod00ArgsBuffer = null;
		m_lod01ArgsBuffer = null;
		m_lod02ArgsBuffer = null;
	}

	private void InitializeBuffers(IndirectInstanceData _data)
	{
		m_positionsBuffer = new ComputeBuffer(_data.positions.Length, COMPUTE_SHADER_INPUT_BYTE_SIZE);
		m_numOfInstances = _data.positions.Length;
        ComputeShaderInputData[] ComputeShaderInputData = new ComputeShaderInputData[m_numOfInstances];
        
        for (int i = 0; i < m_numOfInstances; i++)
        {
			ComputeShaderInputData newData = new ComputeShaderInputData();
            m_computeShaderNumOfInstances = Mathf.ClosestPowerOfTwo(m_numOfInstances);
            
			Bounds b = new Bounds();
			b.Encapsulate(_data.lod00Mesh.bounds);
			b.Encapsulate(_data.lod01Mesh.bounds);
			b.Encapsulate(_data.lod02Mesh.bounds);
			b.extents *= _data.uniformScales[i];
			// Debug.Log(b);

			newData.position = _data.positions[i];
			newData.rotation = _data.rotations[i];
			newData.uniformScale = _data.uniformScales[i];
			newData.boundsCenter = _data.positions[i];//_data.lod00Mesh.bounds.center;
			newData.boundsExtents = b.extents;//_data.lod00Mesh.bounds.extents;
			newData.lod00Range = _data.lod00Range;
			newData.lod01Range = _data.lod01Range;
			newData.lod02Range = _data.lod02Range;
			ComputeShaderInputData[i] = newData;
        }
        m_positionsBuffer.SetData(ComputeShaderInputData);

		m_lod00PositionsBuffer = new ComputeBuffer(m_numOfInstances, COMPUTE_SHADER_OUTPUT_BYTE_SIZE, ComputeBufferType.Append);
		m_lod01PositionsBuffer = new ComputeBuffer(m_numOfInstances, COMPUTE_SHADER_OUTPUT_BYTE_SIZE, ComputeBufferType.Append);
		m_lod02PositionsBuffer = new ComputeBuffer(m_numOfInstances, COMPUTE_SHADER_OUTPUT_BYTE_SIZE, ComputeBufferType.Append);

		m_lod00PositionsBuffer.SetCounterValue(0);
		m_lod01PositionsBuffer.SetCounterValue(0);
		m_lod02PositionsBuffer.SetCounterValue(0);
	}

    private void InitializeMaterials(IndirectInstanceData _data)
    {
        // ----------------------------------------------------------
        // Silly workaround for a shadow bug.
        // If we don't create a material property block and set some
        // unique value to it, we only get shadows in one of our draw calls. 
        Lod00MatPropBlock = new MaterialPropertyBlock();
        Lod01MatPropBlock = new MaterialPropertyBlock();
        Lod02MatPropBlock = new MaterialPropertyBlock();

        Lod00MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter, 1); materialPropertyCounter++;
        Lod01MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter, 2); materialPropertyCounter++;
        Lod02MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter, 3); materialPropertyCounter++;

        // End of silly workaround!
        // ----------------------------------------------------------

        m_lod00Material = new Material(_data.lod00Material);
        m_lod01Material = new Material(_data.lod01Material);
        m_lod02Material = new Material(_data.lod02Material);

        m_lod00Material.SetColor("_Color", Color.red);
        m_lod01Material.SetColor("_Color", Color.green);
        m_lod02Material.SetColor("_Color", Color.blue);

        m_lod00Material.SetBuffer("positionBuffer", m_lod00PositionsBuffer);
        m_lod01Material.SetBuffer("positionBuffer", m_lod01PositionsBuffer);
        m_lod02Material.SetBuffer("positionBuffer", m_lod02PositionsBuffer);
    }

    private void InitalizeArguments(IndirectInstanceData _data)
    {
        m_lod00Args = new uint[NUM_OF_ARGS_PER_INSTANCE];
        m_lod01Args = new uint[NUM_OF_ARGS_PER_INSTANCE];
        m_lod02Args = new uint[NUM_OF_ARGS_PER_INSTANCE];

        // Buffer with arguments, bufferWithArgs, has to have five integer numbers
        // 0 - index count per instance, 
        m_lod00Args[0] = _data.lod00Mesh.GetIndexCount(0);
        m_lod01Args[0] = _data.lod01Mesh.GetIndexCount(0);
        m_lod02Args[0] = _data.lod02Mesh.GetIndexCount(0);

        // 1 - instance count
        m_lod00Args[1] = (uint)_data.positions.Length;
        m_lod01Args[1] = (uint)_data.positions.Length;
        m_lod02Args[1] = (uint)_data.positions.Length;

        // 2 - start index location
        m_lod00Args[2] = 0;
        m_lod01Args[2] = 0;
        m_lod02Args[2] = 0;

        // 3 - base vertex location
        m_lod00Args[3] = 0;
        m_lod01Args[3] = 0;
        m_lod02Args[3] = 0;

        // 4 - start instance location
        m_lod00Args[4] = 0;
        m_lod01Args[4] = 0;
        m_lod02Args[4] = 0;

        m_lod00ArgsBuffer = new ComputeBuffer(m_lod00Args.Length, NUM_OF_ARGS_PER_INSTANCE * sizeof(uint), ComputeBufferType.IndirectArguments);
        m_lod01ArgsBuffer = new ComputeBuffer(m_lod01Args.Length, NUM_OF_ARGS_PER_INSTANCE * sizeof(uint), ComputeBufferType.IndirectArguments);
        m_lod02ArgsBuffer = new ComputeBuffer(m_lod02Args.Length, NUM_OF_ARGS_PER_INSTANCE * sizeof(uint), ComputeBufferType.IndirectArguments);

        m_lod00ArgsBuffer.SetData(m_lod00Args);
        m_lod01ArgsBuffer.SetData(m_lod01Args);
        m_lod02ArgsBuffer.SetData(m_lod02Args);
    }
}
