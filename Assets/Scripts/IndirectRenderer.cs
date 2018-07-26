using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public struct FrustumPlane
{
	public Vector3 normal;
	public Vector3 pointOnPlane;
	public Vector3 v0;
	public Vector3 v1;
	public Vector3 v2;
	public void DebugDraw(Color c)
	{
		Gizmos.color = c;
		Gizmos.DrawLine(v0, v1);
		Gizmos.DrawLine(v1, v2);
		Gizmos.DrawLine(v2, v0);
	}
}

[System.Serializable]
public struct ComputeShaderInputData
{
	public uint drawCallID;		// 1
    public Vector3 position;        // 3
    public Vector3 rotation;        // 6
    public float uniformScale;      // 7
    public Vector3 boundsCenter;    // 10
    public Vector3 boundsExtents;   // 13
    public float distanceToCamera; // 14
}

public struct ComputeShaderOutputData
{
    public Vector3 position;// 3
    public Vector3 rotation;// 6
    public float uniformScale;// 7
};

[System.Serializable]
public class IndirectRenderingMesh
{
	public List<ComputeShaderInputData> computeInstances = new List<ComputeShaderInputData>();
	public Mesh mesh;
    public Material material;
    public MaterialPropertyBlock Lod00MatPropBlock;
	public MaterialPropertyBlock Lod01MatPropBlock;
	public MaterialPropertyBlock Lod02MatPropBlock;
}

public partial class IndirectRenderer : MonoBehaviour
{
    #region Variables
    
    [Header("Settings")]
    [Space(10f)]
	public bool useCPUSorting = false;
	[Range(0f, 500f)] public float m_cameraDistanceSorting = 250f;
	public bool m_receiveShadows = true;
    [Range(0f, 500f)] public float m_shadowCullingLength = 250f;
    public ShadowCastingMode m_shadowCastingMode;

    [Header("References")]
    public ComputeShader m_00_lodSortingCS = null;
    public ComputeShader m_01_occlusionCS = null;
    public ComputeShader m_02_scanInstancesCS = null;
	public ComputeShader m_03_scanGroupSumsCS = null;
	public ComputeShader m_04_copyInstanceDataCS = null;
	public ComputeShader m_05_calcInstanceOffsetsCS = null;
    public HiZBuffer m_hiZBuffer;
    public Camera m_camera;
    public Light m_light;

    [Header("Debug")]
    [Space(10f)]
	public DebugStop m_debugStop = DebugStop.DontStop;
	public DebugLog m_debugLog = DebugLog.DontLog;
    public bool m_showHiZTexture = false;
    [SerializeField] [Range(0, 16)] private int m_hiZTextureLodLevel = 0;

    // Private Variables

	// Buffers
    private ComputeBuffer m_lodDistancesTempBuffer = null;
	private ComputeBuffer m_isVisibleBuffer = null;
	private ComputeBuffer m_groupSumArray = null;
	private ComputeBuffer m_scannedGroupSumBuffer = null;
	private ComputeBuffer m_scannedInstancePredicates = null;
	private ComputeBuffer m_positionsBuffer = null;
	private ComputeBuffer m_culledInstanceBuffer = null;
	// private ComputeBuffer m_culledDrawcallIDBuffer = null;
	private ComputeBuffer m_argsBuffer = null;

    // Kernel ID's
	private int m_00_lodSortingCSKernelID;
	private int m_00_lodSortingTransposeCSKernelID;
	private int m_01_occlusionKernelID;
	private int m_02_scanInstancesKernelID;
	private int m_03_scanGroupSumsKernelID;
	private int m_04_copyInstanceDataKernelID;
	private int m_05_calcInstanceOffsetsKernelID;

	// Other
	private int m_numberOfInstances = 0;
	private uint[] m_args = null;
	private Vector3 lastCamPos = Vector3.zero;
    private Bounds m_drawBounds = new Bounds();
	private Vector3 m_camPosition = Vector3.zero;
	private Vector3 m_camNearCenter = Vector3.zero;
    private Vector3 m_camFarCenter = Vector3.zero;
	private Vector3 m_lightDirection = Vector3.zero;
    private Vector4[] m_cameraFrustumPlanes = new Vector4[6];
	private Vector3[] m_cameraNearPlaneVertices = new Vector3[4];
    private Vector3[] m_cameraFarPlaneVertices = new Vector3[4];
	private Matrix4x4 m_MVP;
	private FrustumPlane[] m_frustumPlanes = null;
	private ComputeShaderInputData[] instancesPositionsArray = null;
    private List<IndirectInstanceData> m_instances = new List<IndirectInstanceData>();
    private List<IndirectRenderingMesh> m_renderers = new List<IndirectRenderingMesh>();

    // Constants
	private const int NUMBER_OF_ARGS_PER_INSTANCE = 15;
	private const int HACK_POT_PADDING_DRAW_ID = 666;
	// Enums
	public enum DebugStop
	{
		DontStop,
		AfterInitialize,
		AfterArgsBufferReset,
		AfterSorting,
		AfterCulling,
		AfterScanInstances,
		AfterScanGroupSums,
		AfterCopyInstanceData,
		AfterCalcInstanceOffsets,
	}

	public enum DebugLog
	{
		DontLog,
		AfterSorting,
		AfterCulling,
		AfterScanInstances,
		AfterScanGroupSums,
		AfterCopyInstanceData,
		AfterCalcInstanceOffsets,
	}

    #endregion

    #region MonoBehaviour

	private void Update()
	{
		m_hiZBuffer.DebugLodLevel = m_hiZTextureLodLevel;
		m_camera.enabled = !m_showHiZTexture;
	}

	private void OnPreCull()
	{
		if (m_renderers == null
            || m_renderers.Count == 0
            || m_hiZBuffer.HiZDepthTexture == null
			|| m_showHiZTexture == true)
        {
            return;
        }

		RunCompute();
		
		// Draw visible objects
		if (m_debugStop == DebugStop.DontStop)
		{
			for (int i = 0; i < m_renderers.Count; i++)
			{
				IndirectRenderingMesh irm = m_renderers[i];

				Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_drawBounds, m_argsBuffer, (i * 60) + 00, irm.Lod00MatPropBlock, m_shadowCastingMode, m_receiveShadows);
				Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_drawBounds, m_argsBuffer, (i * 60) + 20, irm.Lod01MatPropBlock, m_shadowCastingMode, m_receiveShadows);
				Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_drawBounds, m_argsBuffer, (i * 60) + 40, irm.Lod02MatPropBlock, ShadowCastingMode.Off, false);	
			}
		}
    }

    private void OnDestroy()
    {
        if (m_lodDistancesTempBuffer != null) { m_lodDistancesTempBuffer.Release(); }
		if (m_positionsBuffer != null) { m_positionsBuffer.Release(); }
		if (m_culledInstanceBuffer != null) { m_culledInstanceBuffer.Release(); }
		if (m_argsBuffer != null) { m_argsBuffer.Release(); }
		if (m_isVisibleBuffer != null) { m_isVisibleBuffer.Release(); }
		if (m_groupSumArray != null) { m_groupSumArray.Release(); }
		if (m_scannedGroupSumBuffer != null) { m_scannedGroupSumBuffer.Release(); }
		if (m_scannedInstancePredicates != null) { m_scannedInstancePredicates.Release(); }
		// if (m_culledDrawcallIDBuffer != null) { m_culledDrawcallIDBuffer.Release(); }
    }

    #endregion

    #region Private Functions
    private void RunCompute()
	{
        // Update Compute();
        CalculateCameraFrustum(ref m_camera);

        // Global data
        m_drawBounds.center = m_camPosition;
        m_drawBounds.extents = Vector3.one * 10000;
		m_lightDirection = m_light.transform.forward;

		if (m_debugStop == DebugStop.AfterInitialize)
		{
			return;
		}

		const int scanThreadGroupSize = 64;

		//////////////////////////////////////////////////////
		// Reset the arguments buffer
		//////////////////////////////////////////////////////
		Profiler.BeginSample("Resetting args buffer");
		{
			m_argsBuffer.SetData(m_args);
		}
		Profiler.EndSample();

		if (m_debugStop == DebugStop.AfterArgsBufferReset)
		{
			return;
		}

		//////////////////////////////////////////////////////
		// Sort the position buffer based on distance from camera
		//////////////////////////////////////////////////////
		Profiler.BeginSample("00 LOD Sorting");
		{
			if (Vector3.Distance(m_camPosition, lastCamPos) > m_cameraDistanceSorting)
			{	
				lastCamPos = m_camPosition;
				if (useCPUSorting)
				{
					RunCPUSorting(ref m_camNearCenter);
				}
				else 
				{
					RunGPUSorting(ref m_camNearCenter);
				}
				Log00Sorting();
			}
		}
		Profiler.EndSample();
		
		if (m_debugStop == DebugStop.AfterSorting)
		{
			return;
		}

		//////////////////////////////////////////////////////
		// Set up compute shader to perform the occlusion culling
		//////////////////////////////////////////////////////
		Profiler.BeginSample("01 Occlusion");
		{
			// Input
			m_01_occlusionCS.SetFloat("_ShadowCullingLength", m_shadowCullingLength);
			m_01_occlusionCS.SetMatrix("_UNITY_MATRIX_MVP", m_MVP);
			m_01_occlusionCS.SetVector("_HiZTextureSize", m_hiZBuffer.textureSize);
			m_01_occlusionCS.SetVector("_CamNearCenter", m_camNearCenter);
			m_01_occlusionCS.SetVector("_CamFarCenter", m_camFarCenter);
			m_01_occlusionCS.SetVector("_LightDirection", m_lightDirection);
			m_01_occlusionCS.SetTexture(m_01_occlusionKernelID, "_HiZMap", m_hiZBuffer.HiZDepthTexture);
			m_01_occlusionCS.SetBuffer(m_01_occlusionKernelID, "positionBuffer", m_positionsBuffer);

			// Output
			m_01_occlusionCS.SetBuffer(m_01_occlusionKernelID, "argsBuffer", m_argsBuffer);
			m_01_occlusionCS.SetBuffer(m_01_occlusionKernelID, "_IsVisibleBuffer", m_isVisibleBuffer);

			// Dispatch
			int groupX = Mathf.Max(m_numberOfInstances / 64, 1);
			m_01_occlusionCS.Dispatch(m_01_occlusionKernelID, groupX, 1, 1);

			// Debug
			Log01Culling();
		}
		Profiler.EndSample();

		if (m_debugStop == DebugStop.AfterCulling)
		{
			return;
		}

		//////////////////////////////////////////////////////
		// Perform scan of instance predicates
		//////////////////////////////////////////////////////
		Profiler.BeginSample("02 Scan Instances");
		{
			// Input
			m_02_scanInstancesCS.SetBuffer(m_02_scanInstancesKernelID, "instancePredicatesIn", m_isVisibleBuffer);

			// Output
			m_02_scanInstancesCS.SetBuffer(m_02_scanInstancesKernelID, "groupSumArray", m_groupSumArray);
			m_02_scanInstancesCS.SetBuffer(m_02_scanInstancesKernelID, "scannedInstancePredicates", m_scannedInstancePredicates);

			// Dispatch
			int groupX = m_numberOfInstances / (2 * scanThreadGroupSize);
			m_02_scanInstancesCS.Dispatch(m_02_scanInstancesKernelID, groupX, 1, 1);

			// Debug
			Log02ScanInstances();
		}
		Profiler.EndSample();

		if (m_debugStop == DebugStop.AfterScanInstances)
		{
			return;
		}

		//////////////////////////////////////////////////////
		// Perform scan of group sums
		//////////////////////////////////////////////////////
		Profiler.BeginSample("03 Scan Thread Groups");
		{
			// Input
			int numOfGroups = m_numberOfInstances / (2 * scanThreadGroupSize);
			m_03_scanGroupSumsCS.SetInt("_NumOfGroups", numOfGroups);
			m_03_scanGroupSumsCS.SetBuffer(m_03_scanGroupSumsKernelID, "groupSumArrayIn", m_groupSumArray);

			// Output
			m_03_scanGroupSumsCS.SetBuffer(m_03_scanGroupSumsKernelID, "groupSumArrayOut", m_scannedGroupSumBuffer);

			// Dispatch
			int groupX = 1;
			m_03_scanGroupSumsCS.Dispatch(m_03_scanGroupSumsKernelID, groupX, 1, 1);

			// Debug
			Log03ScanGroupSums();
		}
		Profiler.EndSample();

		if (m_debugStop == DebugStop.AfterScanGroupSums)
		{
			return;
		}
		
		//////////////////////////////////////////////////////
		// Perform stream compaction 
		//////////////////////////////////////////////////////
		Profiler.BeginSample("04 Copy Instance Data");
		{
			// Input
			m_04_copyInstanceDataCS.SetInt("_NumberOfInstances", m_numberOfInstances);
			m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "instanceDataIn", m_positionsBuffer);
			m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "instancePredicatesIn", m_isVisibleBuffer);
			m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "groupSumArray", m_scannedGroupSumBuffer);
			m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "scannedInstancePredicates", m_scannedInstancePredicates);

			// Output
			m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "instanceDataOut", m_culledInstanceBuffer);
			// m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "drawcallIDDataOut", m_culledDrawcallIDBuffer);

			// Dispatch
			int groupX = m_numberOfInstances / (2 * scanThreadGroupSize);
			m_04_copyInstanceDataCS.Dispatch(m_04_copyInstanceDataKernelID, groupX, 1, 1);

			// Debug
			Log04CopyInstanceData();
		}
		Profiler.EndSample();

		if (m_debugStop == DebugStop.AfterCopyInstanceData)
		{
			return;
		}

		//////////////////////////////////////////////////////
		// Calculate instance offsets and store in drawcall arguments buffer
		//////////////////////////////////////////////////////
		Profiler.BeginSample("05 Calculate Instance Offsets");
		{
			// Input/ Output
			m_05_calcInstanceOffsetsCS.SetInt("NoofDrawcalls", m_instances.Count * 3);
			m_05_calcInstanceOffsetsCS.SetBuffer(m_05_calcInstanceOffsetsKernelID, "drawcallDataOut", m_argsBuffer);

			// Dispatch
			m_05_calcInstanceOffsetsCS.Dispatch(m_05_calcInstanceOffsetsKernelID, 1, 1, 1);
			
			// Debug
			Log05CalculateInstanceOffsets();
		}
		Profiler.EndSample();
	}

	private void RunCPUSorting(ref Vector3 _cameraPosition)
	{
		int index = 0;
		for (int i = 0; i < m_renderers.Count; i++)
		{
			List<ComputeShaderInputData> data = m_renderers[i].computeInstances;
			int dataSize = data.Count;

			// Update the cam to obj distances
			for (int p = 0; p < dataSize; p++)
			{
				ComputeShaderInputData d = data[p];
				d.distanceToCamera = Vector3.Distance(d.position, _cameraPosition);
				data[p] = d;
			}

			// Sort
			data.Sort(
				(a,b) => 
				{
					return a.distanceToCamera <= b.distanceToCamera ? -1 : 1;
				}
			);
			
			for (int j = 0; j < dataSize; j++)
			{
				instancesPositionsArray[index] = data[j];
				index++;
			}
		}
		m_positionsBuffer.SetData(instancesPositionsArray);
	}
	
    private void RunGPUSorting(ref Vector3 _cameraPosition)
	{
		uint BITONIC_BLOCK_SIZE   = 256;
		uint TRANSPOSE_BLOCK_SIZE = 8;

        // Determine parameters.
        uint NUM_ELEMENTS  = (uint) m_numberOfInstances;
        uint MATRIX_WIDTH  = BITONIC_BLOCK_SIZE;
        uint MATRIX_HEIGHT = (uint)NUM_ELEMENTS / BITONIC_BLOCK_SIZE;

        // Sort the data
        // First sort the rows for the levels <= to the block size
        for (uint level = 2; level <= BITONIC_BLOCK_SIZE; level <<= 1)
        {
            SetGPUSortConstants(ref m_00_lodSortingCS, ref level, ref level, ref MATRIX_HEIGHT, ref MATRIX_WIDTH);

            // Sort the row data
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingCSKernelID, "Data", m_positionsBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
        }

        // Then sort the rows and columns for the levels > than the block size
        // Transpose. Sort the Columns. Transpose. Sort the Rows.
        for (uint level = (BITONIC_BLOCK_SIZE << 1); level <= NUM_ELEMENTS; level <<= 1)
        {
            // Transpose the data from buffer 1 into buffer 2
			uint l = (level / BITONIC_BLOCK_SIZE);
			uint lm = (level & ~NUM_ELEMENTS) / BITONIC_BLOCK_SIZE;
            SetGPUSortConstants(ref m_00_lodSortingCS, ref l, ref lm, ref MATRIX_WIDTH, ref MATRIX_HEIGHT);
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingTransposeCSKernelID, "Input", m_positionsBuffer);
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingTransposeCSKernelID, "Data", m_lodDistancesTempBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingTransposeCSKernelID, (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), 1);

            // Sort the transposed column data
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingCSKernelID, "Data", m_lodDistancesTempBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);

            // Transpose the data from buffer 2 back into buffer 1
            SetGPUSortConstants(ref m_00_lodSortingCS, ref BITONIC_BLOCK_SIZE, ref level, ref MATRIX_HEIGHT, ref MATRIX_WIDTH);
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingTransposeCSKernelID, "Input", m_lodDistancesTempBuffer);
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingTransposeCSKernelID, "Data", m_positionsBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingTransposeCSKernelID, (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), 1);

            // Sort the row data
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingCSKernelID, "Data", m_positionsBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
		}
	}

    void SetGPUSortConstants(ref ComputeShader cs, ref uint level, ref uint levelMask, ref uint width, ref uint height)
    {
        cs.SetInt("_Level",     (int)level    );
        cs.SetInt("_LevelMask", (int)levelMask);
        cs.SetInt("_Width",     (int)width    );
        cs.SetInt("_Height",    (int)height   );
	}

	private void CalculateCameraFrustum(ref Camera _camera)
    {
		m_camPosition = _camera.transform.position;
		m_camNearCenter = m_camPosition + m_camera.transform.forward * m_camera.nearClipPlane;
		m_camFarCenter = m_camPosition + m_camera.transform.forward * m_camera.farClipPlane;

        CalculateCameraFrustumPlanes();

        // Swap [1] and [2] so the order is better for the loop
        Vector4 temp;
        temp = m_cameraFrustumPlanes[1]; 
        m_cameraFrustumPlanes[1] = m_cameraFrustumPlanes[2]; 
        m_cameraFrustumPlanes[2] = temp; 
        
        
        for (int i = 0; i < 4; i++)
        {
            m_cameraNearPlaneVertices[i] = Plane3Intersect( m_cameraFrustumPlanes[4], m_cameraFrustumPlanes[i], m_cameraFrustumPlanes[( i + 1 ) % 4] ); //near corners on the created projection matrix
            m_cameraFarPlaneVertices[i] = Plane3Intersect( m_cameraFrustumPlanes[5], m_cameraFrustumPlanes[i], m_cameraFrustumPlanes[( i + 1 ) % 4] ); //far corners on the created projection matrix
        }

        // Revert the swap
        temp = m_cameraFrustumPlanes[1]; m_cameraFrustumPlanes[1] = m_cameraFrustumPlanes[2]; m_cameraFrustumPlanes[2] = temp;

        m_frustumPlanes = new FrustumPlane[] {
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Left],   pointOnPlane = m_camNearCenter, v0 = m_camNearCenter, v1 = m_cameraFarPlaneVertices[0], v2 = m_cameraFarPlaneVertices[3] },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Right],  pointOnPlane = m_camNearCenter, v0 = m_camNearCenter, v1 = m_cameraFarPlaneVertices[2], v2 = m_cameraFarPlaneVertices[1] },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Bottom], pointOnPlane = m_camNearCenter, v0 = m_camNearCenter, v1 = m_cameraFarPlaneVertices[1], v2 = m_cameraFarPlaneVertices[0] },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Top],    pointOnPlane = m_camNearCenter, v0 = m_camNearCenter, v1 = m_cameraFarPlaneVertices[3], v2 = m_cameraFarPlaneVertices[2] },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Near],   pointOnPlane = m_camNearCenter, v0 = m_camNearCenter, v1 = m_camNearCenter, v2 = m_camNearCenter },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Far],    pointOnPlane = m_camFarCenter,  v0 = m_camNearCenter, v1 = m_camNearCenter, v2 = m_camNearCenter },
        };
    }

    // Get intersection point of 3 planes
    private Vector3 Plane3Intersect ( Vector4 p1, Vector4 p2, Vector4 p3 )
    { 
        Vector3 p1Norm = new Vector3(p1.x, p1.y, p1.z);
        Vector3 p2Norm = new Vector3(p2.x, p2.y, p2.z);
        Vector3 p3Norm = new Vector3(p3.x, p3.y, p3.z);
        
        return (( -p1.w * Vector3.Cross( p2Norm, p3Norm ) ) +
                ( -p2.w * Vector3.Cross( p3Norm, p1Norm ) ) +
                ( -p3.w * Vector3.Cross( p1Norm, p2Norm ) ) ) /
            ( Vector3.Dot( p1Norm, Vector3.Cross( p2Norm, p3Norm ) ) );
    }

    private void CalculateCameraFrustumPlanes()
	{
        Matrix4x4 M = m_camera.transform.localToWorldMatrix;
        Matrix4x4 V = m_camera.worldToCameraMatrix;
        Matrix4x4 P = m_camera.projectionMatrix;
        m_MVP = P*V;//*M;
		
		// Left clipping plane.
		m_cameraFrustumPlanes[0] = new Vector4( m_MVP[3]+m_MVP[0], m_MVP[7]+m_MVP[4], m_MVP[11]+m_MVP[8], m_MVP[15]+m_MVP[12] );
        m_cameraFrustumPlanes[0] /= Mathf.Sqrt(m_cameraFrustumPlanes[0].x * m_cameraFrustumPlanes[0].x + m_cameraFrustumPlanes[0].y * m_cameraFrustumPlanes[0].y + m_cameraFrustumPlanes[0].z * m_cameraFrustumPlanes[0].z);
		
		// Right clipping plane.
		m_cameraFrustumPlanes[1] = new Vector4( m_MVP[3]-m_MVP[0], m_MVP[7]-m_MVP[4], m_MVP[11]-m_MVP[8], m_MVP[15]-m_MVP[12] );
        m_cameraFrustumPlanes[1] /= Mathf.Sqrt(m_cameraFrustumPlanes[1].x * m_cameraFrustumPlanes[1].x + m_cameraFrustumPlanes[1].y * m_cameraFrustumPlanes[1].y + m_cameraFrustumPlanes[1].z * m_cameraFrustumPlanes[1].z);
		
		// Bottom clipping plane.
		m_cameraFrustumPlanes[2] = new Vector4( m_MVP[3]+m_MVP[1], m_MVP[7]+m_MVP[5], m_MVP[11]+m_MVP[9], m_MVP[15]+m_MVP[13] );
        m_cameraFrustumPlanes[2] /= Mathf.Sqrt(m_cameraFrustumPlanes[2].x * m_cameraFrustumPlanes[2].x + m_cameraFrustumPlanes[2].y * m_cameraFrustumPlanes[2].y + m_cameraFrustumPlanes[2].z * m_cameraFrustumPlanes[2].z);
		
		// Top clipping plane.
		m_cameraFrustumPlanes[3] = new Vector4( m_MVP[3]-m_MVP[1], m_MVP[7]-m_MVP[5], m_MVP[11]-m_MVP[9], m_MVP[15]-m_MVP[13] );
        m_cameraFrustumPlanes[3] /= Mathf.Sqrt(m_cameraFrustumPlanes[3].x * m_cameraFrustumPlanes[3].x + m_cameraFrustumPlanes[3].y * m_cameraFrustumPlanes[3].y + m_cameraFrustumPlanes[3].z * m_cameraFrustumPlanes[3].z);
		
		// Near clipping plane.
		m_cameraFrustumPlanes[4] = new Vector4( m_MVP[3]+m_MVP[2], m_MVP[7]+m_MVP[6], m_MVP[11]+m_MVP[10], m_MVP[15]+m_MVP[14] );
        m_cameraFrustumPlanes[4] /= Mathf.Sqrt(m_cameraFrustumPlanes[4].x * m_cameraFrustumPlanes[4].x + m_cameraFrustumPlanes[4].y * m_cameraFrustumPlanes[4].y + m_cameraFrustumPlanes[4].z * m_cameraFrustumPlanes[4].z);
		
		// Far clipping plane.
		m_cameraFrustumPlanes[5] = new Vector4( m_MVP[3]-m_MVP[2], m_MVP[7]-m_MVP[6], m_MVP[11]-m_MVP[10], m_MVP[15]-m_MVP[14] );
        m_cameraFrustumPlanes[5] /= Mathf.Sqrt(m_cameraFrustumPlanes[5].x * m_cameraFrustumPlanes[5].x + m_cameraFrustumPlanes[5].y * m_cameraFrustumPlanes[5].y + m_cameraFrustumPlanes[5].z * m_cameraFrustumPlanes[5].z);
	}

    #endregion

    #region Public Functions

    public void AddInstances(List<IndirectInstanceData> _instances)
    {
        m_instances.AddRange(_instances);
    }

    public void Initialize()
    {
		m_00_lodSortingCSKernelID 			= m_00_lodSortingCS.FindKernel("BitonicSort");
		m_00_lodSortingTransposeCSKernelID 	= m_00_lodSortingCS.FindKernel("MatrixTranspose");
		m_01_occlusionKernelID 				= m_01_occlusionCS.FindKernel("CSMain");
		m_02_scanInstancesKernelID 			= m_02_scanInstancesCS.FindKernel("CSMain");
		m_03_scanGroupSumsKernelID 			= m_03_scanGroupSumsCS.FindKernel("CSMain");
		m_04_copyInstanceDataKernelID 		= m_04_copyInstanceDataCS.FindKernel("CSMain");
		m_05_calcInstanceOffsetsKernelID 	= m_05_calcInstanceOffsetsCS.FindKernel("CSMain");

		int materialPropertyCounter = 0;
        int instanceCounter = 0;
        m_args = new uint[m_instances.Count * NUMBER_OF_ARGS_PER_INSTANCE];
        for (int i = 0; i < m_instances.Count; i++)
        {
            IndirectRenderingMesh irm = new IndirectRenderingMesh();
            IndirectInstanceData data = m_instances[i];

            // Initialize Mesh
            irm.mesh = new Mesh();
            irm.mesh.CombineMeshes(
				new CombineInstance[] {
					new CombineInstance() { mesh = data.lod00Mesh},
					new CombineInstance() { mesh = data.lod01Mesh},
					new CombineInstance() { mesh = data.lod02Mesh}
				}, 
				true,		// Merge Submeshes 
				false, 		// Use Matrices
				false		// Has lightmap data
			);

            // Arguments
			int argsIndex = i * NUMBER_OF_ARGS_PER_INSTANCE;
            
			// Buffer with arguments has to have five integer numbers
            // LOD00
            m_args[argsIndex + 0] = data.lod00Mesh.GetIndexCount(0); 				// 0 - index count per instance, 
            m_args[argsIndex + 1] = 0;												// 1 - instance count
            m_args[argsIndex + 2] = 0; 												// 2 - start index location
            m_args[argsIndex + 3] = 0; 												// 3 - base vertex location
            m_args[argsIndex + 4] = 0; 												// 4 - start instance location

            // LOD01
            m_args[argsIndex + 5] = data.lod01Mesh.GetIndexCount(0);				// 0 - index count per instance, 
            m_args[argsIndex + 6] = 0;												// 1 - instance count
            m_args[argsIndex + 7] = m_args[argsIndex + 0] + m_args[argsIndex + 2];	// 2 - start index location
            m_args[argsIndex + 8] = 0;												// 3 - base vertex location
            m_args[argsIndex + 9] = 0;												// 4 - start instance location

            // LOD02
            m_args[argsIndex + 10] = data.lod02Mesh.GetIndexCount(0);				// 0 - index count per instance, 
            m_args[argsIndex + 11] = 0;												// 1 - instance count
            m_args[argsIndex + 12] = m_args[argsIndex + 5] + m_args[argsIndex + 7];	// 2 - start index location
            m_args[argsIndex + 13] = 0;												// 3 - base vertex location
            m_args[argsIndex + 14] = 0;												// 4 - start instance location


            // Materials
            irm.Lod00MatPropBlock = new MaterialPropertyBlock();
            irm.Lod01MatPropBlock = new MaterialPropertyBlock();
            irm.Lod02MatPropBlock = new MaterialPropertyBlock();

            // ----------------------------------------------------------
            // Silly workaround for a shadow bug.
            // If we don't set a unique value to the property block we 
            // only get shadows in one of our draw calls. 
            irm.Lod00MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, 1);
            irm.Lod01MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, 2);
            irm.Lod02MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, 3);
            // End of silly workaround!
            // ----------------------------------------------------------

            irm.Lod00MatPropBlock.SetBuffer("positionBuffer", m_culledInstanceBuffer);
            irm.Lod01MatPropBlock.SetBuffer("positionBuffer", m_culledInstanceBuffer);
            irm.Lod02MatPropBlock.SetBuffer("positionBuffer", m_culledInstanceBuffer);

            irm.Lod00MatPropBlock.SetColor("_Color", Color.red);
            irm.Lod01MatPropBlock.SetColor("_Color", Color.green);
            irm.Lod02MatPropBlock.SetColor("_Color", Color.blue);

            irm.material = new Material(data.material);

			// Add the instance data (positions, rotations, scaling, bounds...)
			for (int j = 0; j < m_instances[i].positions.Length; j++)
			{
				instanceCounter++;
				IndirectInstanceData _data = m_instances[i];
				ComputeShaderInputData newData = new ComputeShaderInputData();
				
				Bounds b = new Bounds();
				b.Encapsulate(_data.lod00Mesh.bounds);
				b.Encapsulate(_data.lod01Mesh.bounds);
				b.Encapsulate(_data.lod02Mesh.bounds);
				b.extents *= _data.uniformScales[j];

				newData.drawCallID = (uint) i * NUMBER_OF_ARGS_PER_INSTANCE;
				newData.position = _data.positions[j];
				newData.rotation = _data.rotations[j];
				newData.uniformScale = _data.uniformScales[j];
				newData.boundsCenter = _data.positions[j];
				newData.boundsExtents = b.extents;
				newData.distanceToCamera = Vector3.Distance(newData.position, m_camera.transform.position);

				irm.computeInstances.Add(newData);
			}

            // Add the data to the renderer list
            m_renderers.Add(irm);
        }
        
		// HACK! Padding the data so it becomes the power of two.
		if (!Mathf.IsPowerOfTwo(instanceCounter))
		{
			int iterations = Mathf.NextPowerOfTwo(instanceCounter) - instanceCounter;
			for (int j = 0; j < iterations; j++)
			{
				m_renderers[0].computeInstances.Add(new ComputeShaderInputData() {
					drawCallID = HACK_POT_PADDING_DRAW_ID
				});
			}
		}

		List<ComputeShaderInputData> tempInstancesPositionsList = new List<ComputeShaderInputData>();
		for (int i = 0; i < m_renderers.Count; i++)
		{
			tempInstancesPositionsList.AddRange(m_renderers[i].computeInstances);
		}

		instancesPositionsArray = tempInstancesPositionsList.ToArray();
		m_numberOfInstances = tempInstancesPositionsList.Count;

        InitializeBuffers();
    }

	#endregion

    #region Private Functions

	private void InitializeBuffers()
	{   
		int computeShaderInputSize = Marshal.SizeOf(typeof(ComputeShaderInputData));
		int computeShaderOutputSize = Marshal.SizeOf(typeof(ComputeShaderOutputData));

		m_argsBuffer 				= new ComputeBuffer(m_instances.Count * NUMBER_OF_ARGS_PER_INSTANCE, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
		m_positionsBuffer 			= new ComputeBuffer(m_numberOfInstances, computeShaderInputSize, ComputeBufferType.Default);
		m_lodDistancesTempBuffer 	= new ComputeBuffer(m_numberOfInstances, computeShaderInputSize, ComputeBufferType.Default);
		m_culledInstanceBuffer 		= new ComputeBuffer(m_numberOfInstances, computeShaderOutputSize, ComputeBufferType.Default);
		m_isVisibleBuffer 			= new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
		m_scannedInstancePredicates = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
		m_groupSumArray 			= new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
		m_scannedGroupSumBuffer 	= new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
		// m_culledDrawcallIDBuffer 	= new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);

		m_argsBuffer.SetData(m_args);
		m_positionsBuffer.SetData(instancesPositionsArray);
		m_lodDistancesTempBuffer.SetData(instancesPositionsArray);

		for (int i = 0; i < m_renderers.Count; i++)
        {
            IndirectRenderingMesh irm = m_renderers[i];
			irm.Lod00MatPropBlock.SetBuffer("positionBuffer", m_culledInstanceBuffer);
            irm.Lod01MatPropBlock.SetBuffer("positionBuffer", m_culledInstanceBuffer);
            irm.Lod02MatPropBlock.SetBuffer("positionBuffer", m_culledInstanceBuffer);
		}
	}

	private void Log00Sorting()
	{
		if (m_debugLog != DebugLog.AfterSorting)
		{
			return;
		}
		m_debugLog = DebugLog.DontLog;

		StringBuilder sb = new StringBuilder();
		ComputeShaderInputData[] distanceData = new ComputeShaderInputData[m_numberOfInstances];
		m_positionsBuffer.GetData(distanceData);
		sb.AppendLine("00 distances:");
		for (int i = 0; i < distanceData.Length; i++)
		{
			if (i % 350 == 0)
			{
				Debug.Log(sb.ToString());
				sb = new StringBuilder();
			}
			sb.AppendLine(i + ": " + distanceData[i].drawCallID + " => " + distanceData[i].distanceToCamera + " => " + distanceData[i].position);
		}
		Debug.Log(sb.ToString());
	}

	private void Log01Culling()
	{
		if (m_debugLog != DebugLog.AfterCulling)
		{
			return;
		}
		m_debugLog = DebugLog.DontLog;

		StringBuilder sb = new StringBuilder();
		uint[] isVisibleData = new uint[m_numberOfInstances];
		m_isVisibleBuffer.GetData(isVisibleData);
		sb.AppendLine("01 IsVisible:");
		for (int i = 0; i < isVisibleData.Length; i++)
		{
			sb.AppendLine(i + ": " + isVisibleData[i]);
		}
		Debug.Log(sb.ToString());

		sb = new StringBuilder();
		uint[] argsData = new uint[m_instances.Count * NUMBER_OF_ARGS_PER_INSTANCE];
		m_argsBuffer.GetData(argsData);
		sb.AppendLine("01 argsData:");
		for (int i = 0; i < argsData.Length; i++)
		{
			sb.Append(argsData[i] + " ");
			
			if ((i+1) % 5 == 0) 
			{
				sb.AppendLine("");
			}
		}
		Debug.Log(sb.ToString());
	}

	private void Log02ScanInstances()
	{
		if (m_debugLog != DebugLog.AfterScanInstances)
		{
			return;
		}
		m_debugLog = DebugLog.DontLog;

		StringBuilder sb = new StringBuilder();
		uint[] scannedData = new uint[m_numberOfInstances];
		m_scannedInstancePredicates.GetData(scannedData);
		sb.AppendLine("02 Scanned:");
		for (int i = 0; i < scannedData.Length; i++)
		{
			sb.AppendLine(i + ": " + scannedData[i]);
		}
		Debug.Log(sb.ToString());


		sb = new StringBuilder();
		uint[] groupSumData = new uint[m_numberOfInstances];
		m_groupSumArray.GetData(groupSumData);
		sb.AppendLine("02 GroupSum");
		for (int i = 0; i < groupSumData.Length; i++)
		{
			sb.AppendLine(i + ": " + groupSumData[i]);
		}
		Debug.Log(sb.ToString());
	}

	private void Log03ScanGroupSums()
	{
		if (m_debugLog != DebugLog.AfterScanGroupSums)
		{
			return;
		}
		m_debugLog = DebugLog.DontLog;

		StringBuilder sb = new StringBuilder();
		uint[] groupSumArrayOutData = new uint[m_numberOfInstances];
		m_scannedGroupSumBuffer.GetData(groupSumArrayOutData);
		sb.AppendLine("03 GroupSumArray:");
		for (int i = 0; i < groupSumArrayOutData.Length; i++)
		{
			sb.AppendLine(i + ": " + groupSumArrayOutData[i]);
		}
		Debug.Log(sb.ToString());
	}

	private void Log04CopyInstanceData()
	{
		if (m_debugLog != DebugLog.AfterCopyInstanceData)
		{
			return;
		}
		m_debugLog = DebugLog.DontLog;

		StringBuilder sb = new StringBuilder();
		ComputeShaderOutputData[] culledInstancesData = new ComputeShaderOutputData[m_numberOfInstances];
		m_culledInstanceBuffer.GetData(culledInstancesData);
		sb.AppendLine("04 culledInstances:");
		for (int i = 0; i < culledInstancesData.Length; i++)
		{
			sb.AppendLine(i + ": " + culledInstancesData[i].position + " " + culledInstancesData[i].rotation + ": " + culledInstancesData[i].uniformScale);
		}
		Debug.Log(sb.ToString());

		// uint[] drawcallIDData = new uint[m_totalNumOfInstances];
		// m_culledDrawcallIDBuffer.GetData(drawcallIDData);
		// string drawcallIDText = "04 drawcallIDData:\n";
		// for (int i = 0; i < drawcallIDData.Length; i++)
		// {
		// 	drawcallIDText += i + ": " + drawcallIDData[i] + "\n";
		// }
		// Debug.Log(drawcallIDText);
	}

	private void Log05CalculateInstanceOffsets()
	{
		if (m_debugLog != DebugLog.AfterCalcInstanceOffsets)
		{
			return;
		}
		m_debugLog = DebugLog.DontLog;

		StringBuilder sb = new StringBuilder();
		uint[] argsData = new uint[m_instances.Count * NUMBER_OF_ARGS_PER_INSTANCE];
		m_argsBuffer.GetData(argsData);
		sb.AppendLine("01 argsData:");
		for (int i = 0; i < argsData.Length; i++)
		{
			sb.Append(argsData[i] + " ");
			
			if ((i+1) % 5 == 0) 
			{
				sb.AppendLine("");
			}
		}
		Debug.Log(sb.ToString());
	}

	#endregion
}