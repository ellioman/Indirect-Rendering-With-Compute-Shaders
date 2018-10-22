using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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

public struct FrustumPlane
{
    public Vector3 normal;
    public Vector3 pointOnPlane;
    public Vector3 v0;
    public Vector3 v1;
    public Vector3 v2;
}

// 15 * 4 bytes = 60 bytes
[System.Serializable]
public struct InstanceData
{
    public uint drawCallID;		  // 1
    public Vector3 position;      // 4
    public Vector3 rotation;      // 7
    public float uniformScale;     // 8
    public Vector3 boundsCenter;  // 11
    public Vector3 boundsExtents; // 14
    public float distanceToCamera; // 15
}

// 7 * 4 bytes = 28 bytes
public struct InstanceDrawData
{
    public Vector3 position;      // 3
    public Vector3 rotation;      // 6
    public float uniformScale;     // 7
};

[System.Serializable]
public class IndirectRenderingMesh
{
    public List<InstanceData> computeInstances = new List<InstanceData>();
    public Mesh mesh;
    public Material material;
    public MaterialPropertyBlock Lod00MatPropBlock;
    public MaterialPropertyBlock Lod01MatPropBlock;
    public MaterialPropertyBlock Lod02MatPropBlock;
    public MaterialPropertyBlock ShadowMatPropBlock;
}

public partial class IndirectRenderer : MonoBehaviour
{
    #region Variables

    [Header("Settings")]
    public bool m_enableFrustumCulling = true;
    public bool m_enableOcclusionCulling = true;
    public bool m_enableLOD = true;
    public bool m_enableLOD02Shadow = false;
    public bool m_enableCPUSorting = false;
    [Range(0f, 500f)] public float m_shadowCullingLength = 250f;

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

    // Debugging Variables
    [Header("Debug")]

    public DebugLog m_debugLog = DebugLog.DontLog;
    [Space(10f)]
    public bool m_debugDisableMesh = false;
    public bool m_debugDrawBounds = false;
    public bool m_debugDrawLOD = false;
    public bool m_debugDrawHiZ = false;
    [Range(0, 10)] public int m_debugHiZLod = 0;
    [Space(10f)]
    public Material m_debugDrawBoundsMaterial;
    public ComputeShader m_debugDrawBoundsCS;
    private Mesh m_debugBoundsMesh;
    private ComputeBuffer m_debugDrawBoundsArgsBuffer;
    private ComputeBuffer m_debugDrawBoundsPositionBuffer;
    private MaterialPropertyBlock m_debugDrawBoundsProps;
    private bool m_debugLastShowLOD = false;

    // Buffers
    private ComputeBuffer m_lodDistancesTempBuffer = null;
    private ComputeBuffer m_isVisibleBuffer = null;
    private ComputeBuffer m_groupSumArray = null;
    private ComputeBuffer m_scannedGroupSumBuffer = null;
    private ComputeBuffer m_scannedInstancePredicates = null;
    private ComputeBuffer m_instanceDataBuffer = null;
    private ComputeBuffer m_culledInstanceBuffer = null;
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
    private int m_numberOfInstanceTypes = 0;
    private int m_numberOfInstances = 0;
    private uint[] m_args = null;
    private Bounds m_bounds = new Bounds();
    private Vector3 m_camPosition = Vector3.zero;
    private Vector3 m_camNearCenter = Vector3.zero;
    private Vector3 m_camFarCenter = Vector3.zero;
    private Vector3 m_lightDirection = Vector3.zero;
    private Vector4[] m_cameraFrustumPlanes = new Vector4[6];
    private Vector3[] m_cameraNearPlaneVertices = new Vector3[4];
    private Vector3[] m_cameraFarPlaneVertices = new Vector3[4];
    private Matrix4x4 m_MVP;
    private FrustumPlane[] m_frustumPlanes = null;
    private InstanceData[] instancesPositionsArray = null;
    private IndirectInstanceData[] m_instances = null;
    private IndirectRenderingMesh[] m_renderers = null;

    // Constants
    private const int NUMBER_OF_ARGS_PER_INSTANCE = 20;
    private const int HACK_POT_PADDING_DRAW_ID = 666;

    #endregion

    #region MonoBehaviour
    private void Update()
    {
        m_hiZBuffer.DebugLodLevel = m_debugHiZLod;

        if (m_debugLastShowLOD != m_debugDrawLOD)
        {
            m_debugLastShowLOD = m_debugDrawLOD;
            for (int i = 0; i < m_renderers.Length; i++)
            {
                m_renderers[i].Lod00MatPropBlock.SetColor("_Color", m_debugDrawLOD ? Color.red : Color.white);
                m_renderers[i].Lod01MatPropBlock.SetColor("_Color", m_debugDrawLOD ? Color.green : Color.white);
                m_renderers[i].Lod02MatPropBlock.SetColor("_Color", m_debugDrawLOD ? Color.blue : Color.white);
            }
        }

        if (m_debugDrawBounds)
        {
            DrawBounds();
        }
    }

    private void OnPreCull()
    {
        if (m_renderers == null
            || m_renderers.Length == 0
            || m_hiZBuffer.HiZDepthTexture == null
            )
        {
            return;
        }

        CalculateVisibleInstances();
        DrawInstances();
    }

    private void OnDestroy()
    {
        if (m_lodDistancesTempBuffer != null) { m_lodDistancesTempBuffer.Release(); }
        if (m_instanceDataBuffer != null) { m_instanceDataBuffer.Release(); }
        if (m_culledInstanceBuffer != null) { m_culledInstanceBuffer.Release(); }
        if (m_argsBuffer != null) { m_argsBuffer.Release(); }
        if (m_isVisibleBuffer != null) { m_isVisibleBuffer.Release(); }
        if (m_groupSumArray != null) { m_groupSumArray.Release(); }
        if (m_scannedGroupSumBuffer != null) { m_scannedGroupSumBuffer.Release(); }
        if (m_scannedInstancePredicates != null) { m_scannedInstancePredicates.Release(); }

#if UNITY_EDITOR
        OnDestroyEditor();
#endif
    }

    private void OnDrawGizmos()
    {
        if (m_camera == null)
        {
            return;
        }

        CalculateCameraFrustum(ref m_camera);

        for (int i = 0; i < m_frustumPlanes.Length; i++)
        {
            bool ifFacingAway = !AreDirectionsFacingEachother(m_frustumPlanes[i].normal, m_light.transform.forward);
            if (ifFacingAway)
            {
                Vector3 change = -m_light.transform.forward * m_shadowCullingLength;
                m_frustumPlanes[i].pointOnPlane += change;
                m_frustumPlanes[i].v0 += change;
                m_frustumPlanes[i].v1 += change;
                m_frustumPlanes[i].v2 += change;
            }

            Gizmos.color = ifFacingAway ? Color.green : Color.red; ;
            Gizmos.DrawLine(m_frustumPlanes[i].v0, m_frustumPlanes[i].v1);
            Gizmos.DrawLine(m_frustumPlanes[i].v1, m_frustumPlanes[i].v2);
            Gizmos.DrawLine(m_frustumPlanes[i].v2, m_frustumPlanes[i].v0);
        }
    }

    #endregion

    #region Private Functions
    private void DrawInstances()
    {
        ShadowCastingMode objShadowCastingMode = (m_enableLOD02Shadow) ? ShadowCastingMode.Off : ShadowCastingMode.On;
        for (int i = 0; i < m_renderers.Length; i++)
        {
            IndirectRenderingMesh irm = m_renderers[i];
            if (!m_debugDisableMesh)
            {
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, (i * 80) + 00, irm.Lod00MatPropBlock, objShadowCastingMode, true);
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, (i * 80) + 20, irm.Lod01MatPropBlock, objShadowCastingMode, true);
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, (i * 80) + 40, irm.Lod02MatPropBlock, objShadowCastingMode, true);
            }

            if (m_enableLOD02Shadow)
            {
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, (i * 80) + 60, irm.ShadowMatPropBlock, ShadowCastingMode.ShadowsOnly, false);
            }
        }
    }

    private void CalculateVisibleInstances()
    {
        // Update Compute();
        CalculateCameraFrustum(ref m_camera);

        // Global data
        m_bounds.center = m_camPosition;
        m_bounds.extents = Vector3.one * 10000;
        m_lightDirection = m_light.transform.forward;

        const int scanThreadGroupSize = 64;

        //////////////////////////////////////////////////////
        // Reset the arguments buffer
        //////////////////////////////////////////////////////
        Profiler.BeginSample("Resetting args buffer");
        {
            m_argsBuffer.SetData(m_args);
        }
        Profiler.EndSample();

        //////////////////////////////////////////////////////
        // Sort the position buffer based on distance from camera
        //////////////////////////////////////////////////////
        Profiler.BeginSample("00 LOD Sorting");
        {
            if (m_enableCPUSorting)
            {
                RunCPUSorting(ref m_camNearCenter);
            }
            else
            {
                RunGPUSorting(ref m_camNearCenter);
            }
            Log00Sorting();
        }
        Profiler.EndSample();

        //////////////////////////////////////////////////////
        // Set up compute shader to perform the occlusion culling
        //////////////////////////////////////////////////////
        Profiler.BeginSample("01 Occlusion");
        {
            // Input
            m_01_occlusionCS.SetBool("_ShouldFrustumCull", m_enableFrustumCulling);
            m_01_occlusionCS.SetBool("_ShouldOcclusionCull", m_enableOcclusionCulling);
            m_01_occlusionCS.SetBool("_ShouldLOD", m_enableLOD);
            m_01_occlusionCS.SetFloat("_ShadowCullingLength", m_shadowCullingLength);
            m_01_occlusionCS.SetMatrix("_UNITY_MATRIX_MVP", m_MVP);
            m_01_occlusionCS.SetVector("_HiZTextureSize", m_hiZBuffer.TextureSize);
            m_01_occlusionCS.SetVector("_CamNearCenter", m_camNearCenter);
            m_01_occlusionCS.SetVector("_CamFarCenter", m_camFarCenter);
            m_01_occlusionCS.SetVector("_LightDirection", m_lightDirection);
            m_01_occlusionCS.SetTexture(m_01_occlusionKernelID, "_HiZMap", m_hiZBuffer.HiZDepthTexture);
            m_01_occlusionCS.SetBuffer(m_01_occlusionKernelID, "_InstanceDataBuffer", m_instanceDataBuffer);

            // Output
            m_01_occlusionCS.SetBuffer(m_01_occlusionKernelID, "_ArgsBuffer", m_argsBuffer);
            m_01_occlusionCS.SetBuffer(m_01_occlusionKernelID, "_IsVisibleBuffer", m_isVisibleBuffer);

            // Dispatch
            int groupX = Mathf.Max(m_numberOfInstances / 64, 1);
            m_01_occlusionCS.Dispatch(m_01_occlusionKernelID, groupX, 1, 1);

            // Debug
            Log01Culling();
        }
        Profiler.EndSample();

        //////////////////////////////////////////////////////
        // Perform scan of instance predicates
        //////////////////////////////////////////////////////
        Profiler.BeginSample("02 Scan Instances");
        {
            // Input
            m_02_scanInstancesCS.SetBuffer(m_02_scanInstancesKernelID, "_InstancePredicatesIn", m_isVisibleBuffer);

            // Output
            m_02_scanInstancesCS.SetBuffer(m_02_scanInstancesKernelID, "_GroupSumArray", m_groupSumArray);
            m_02_scanInstancesCS.SetBuffer(m_02_scanInstancesKernelID, "_ScannedInstancePredicates", m_scannedInstancePredicates);

            // Dispatch
            int groupX = m_numberOfInstances / (2 * scanThreadGroupSize);
            m_02_scanInstancesCS.Dispatch(m_02_scanInstancesKernelID, groupX, 1, 1);

            // Debug
            Log02ScanInstances();
        }
        Profiler.EndSample();

        //////////////////////////////////////////////////////
        // Perform scan of group sums
        //////////////////////////////////////////////////////
        Profiler.BeginSample("03 Scan Thread Groups");
        {
            // Input
            int numOfGroups = m_numberOfInstances / (2 * scanThreadGroupSize);
            m_03_scanGroupSumsCS.SetInt("_NumOfGroups", numOfGroups);
            m_03_scanGroupSumsCS.SetBuffer(m_03_scanGroupSumsKernelID, "_GroupSumArrayIn", m_groupSumArray);

            // Output
            m_03_scanGroupSumsCS.SetBuffer(m_03_scanGroupSumsKernelID, "_GroupSumArrayOut", m_scannedGroupSumBuffer);

            // Dispatch
            int groupX = 1;
            m_03_scanGroupSumsCS.Dispatch(m_03_scanGroupSumsKernelID, groupX, 1, 1);

            // Debug
            Log03ScanGroupSums();
        }
        Profiler.EndSample();

        //////////////////////////////////////////////////////
        // Perform stream compaction 
        //////////////////////////////////////////////////////
        Profiler.BeginSample("04 Copy Instance Data");
        {
            // Input
            m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "_InstanceDataIn", m_instanceDataBuffer);
            m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "_InstancePredicatesIn", m_isVisibleBuffer);
            m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "_GroupSumArray", m_scannedGroupSumBuffer);
            m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "_ScannedInstancePredicates", m_scannedInstancePredicates);

            // Output
            m_04_copyInstanceDataCS.SetBuffer(m_04_copyInstanceDataKernelID, "_InstanceDataOut", m_culledInstanceBuffer);

            // Dispatch
            int groupX = m_numberOfInstances / (2 * scanThreadGroupSize);
            m_04_copyInstanceDataCS.Dispatch(m_04_copyInstanceDataKernelID, groupX, 1, 1);

            // Debug
            Log04CopyInstanceData();
        }
        Profiler.EndSample();

        //////////////////////////////////////////////////////
        // Calculate instance offsets and store in drawcall arguments buffer
        //////////////////////////////////////////////////////
        Profiler.BeginSample("05 Calculate Instance Offsets");
        {
            // Input/ Output
            m_05_calcInstanceOffsetsCS.SetInt("NoofDrawcalls", m_numberOfInstanceTypes * 4);
            m_05_calcInstanceOffsetsCS.SetBuffer(m_05_calcInstanceOffsetsKernelID, "_DrawcallDataOut", m_argsBuffer);

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
        for (int i = 0; i < m_renderers.Length; i++)
        {
            List<InstanceData> data = m_renderers[i].computeInstances;
            int dataSize = data.Count;

            // Update the cam to obj distances
            for (int p = 0; p < dataSize; p++)
            {
                InstanceData d = data[p];
                d.distanceToCamera = Vector3.Distance(d.position, _cameraPosition);
                data[p] = d;
            }

            // Sort
            data.Sort(
                (a, b) =>
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
        m_instanceDataBuffer.SetData(instancesPositionsArray);
    }

    private void RunGPUSorting(ref Vector3 _cameraPosition)
    {
        uint BITONIC_BLOCK_SIZE = 256;
        uint TRANSPOSE_BLOCK_SIZE = 8;

        // Determine parameters.
        uint NUM_ELEMENTS = (uint)m_numberOfInstances;
        uint MATRIX_WIDTH = BITONIC_BLOCK_SIZE;
        uint MATRIX_HEIGHT = (uint)NUM_ELEMENTS / BITONIC_BLOCK_SIZE;

        // Sort the data
        // First sort the rows for the levels <= to the block size
        for (uint level = 2; level <= BITONIC_BLOCK_SIZE; level <<= 1)
        {
            SetGPUSortConstants(ref m_00_lodSortingCS, ref level, ref level, ref MATRIX_HEIGHT, ref MATRIX_WIDTH);

            // Sort the row data
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingCSKernelID, "Data", m_instanceDataBuffer);
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
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingTransposeCSKernelID, "Input", m_instanceDataBuffer);
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingTransposeCSKernelID, "Data", m_lodDistancesTempBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingTransposeCSKernelID, (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), 1);

            // Sort the transposed column data
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingCSKernelID, "Data", m_lodDistancesTempBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);

            // Transpose the data from buffer 2 back into buffer 1
            SetGPUSortConstants(ref m_00_lodSortingCS, ref BITONIC_BLOCK_SIZE, ref level, ref MATRIX_HEIGHT, ref MATRIX_WIDTH);
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingTransposeCSKernelID, "Input", m_lodDistancesTempBuffer);
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingTransposeCSKernelID, "Data", m_instanceDataBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingTransposeCSKernelID, (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), 1);

            // Sort the row data
            m_00_lodSortingCS.SetBuffer(m_00_lodSortingCSKernelID, "Data", m_instanceDataBuffer);
            m_00_lodSortingCS.Dispatch(m_00_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
        }
    }

    private void SetGPUSortConstants(ref ComputeShader cs, ref uint level, ref uint levelMask, ref uint width, ref uint height)
    {
        cs.SetInt("_Level", (int)level);
        cs.SetInt("_LevelMask", (int)levelMask);
        cs.SetInt("_Width", (int)width);
        cs.SetInt("_Height", (int)height);
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
            m_cameraNearPlaneVertices[i] = Plane3Intersect(m_cameraFrustumPlanes[4], m_cameraFrustumPlanes[i], m_cameraFrustumPlanes[(i + 1) % 4]); //near corners on the created projection matrix
            m_cameraFarPlaneVertices[i] = Plane3Intersect(m_cameraFrustumPlanes[5], m_cameraFrustumPlanes[i], m_cameraFrustumPlanes[(i + 1) % 4]); //far corners on the created projection matrix
        }

        // Revert the swap
        temp = m_cameraFrustumPlanes[1];
        m_cameraFrustumPlanes[1] = m_cameraFrustumPlanes[2];
        m_cameraFrustumPlanes[2] = temp;

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
    private Vector3 Plane3Intersect(Vector4 p1, Vector4 p2, Vector4 p3)
    {
        Vector3 p1Norm = new Vector3(p1.x, p1.y, p1.z);
        Vector3 p2Norm = new Vector3(p2.x, p2.y, p2.z);
        Vector3 p3Norm = new Vector3(p3.x, p3.y, p3.z);

        return ((-p1.w * Vector3.Cross(p2Norm, p3Norm)) +
                (-p2.w * Vector3.Cross(p3Norm, p1Norm)) +
                (-p3.w * Vector3.Cross(p1Norm, p2Norm))) /
            (Vector3.Dot(p1Norm, Vector3.Cross(p2Norm, p3Norm)));
    }

    private void CalculateCameraFrustumPlanes()
    {
        Matrix4x4 M = m_camera.transform.localToWorldMatrix;
        Matrix4x4 V = m_camera.worldToCameraMatrix;
        Matrix4x4 P = m_camera.projectionMatrix;
        m_MVP = P * V;//*M;

        // Left clipping plane.
        m_cameraFrustumPlanes[0] = new Vector4(m_MVP[3] + m_MVP[0], m_MVP[7] + m_MVP[4], m_MVP[11] + m_MVP[8], m_MVP[15] + m_MVP[12]);
        m_cameraFrustumPlanes[0] /= Mathf.Sqrt(m_cameraFrustumPlanes[0].x * m_cameraFrustumPlanes[0].x + m_cameraFrustumPlanes[0].y * m_cameraFrustumPlanes[0].y + m_cameraFrustumPlanes[0].z * m_cameraFrustumPlanes[0].z);

        // Right clipping plane.
        m_cameraFrustumPlanes[1] = new Vector4(m_MVP[3] - m_MVP[0], m_MVP[7] - m_MVP[4], m_MVP[11] - m_MVP[8], m_MVP[15] - m_MVP[12]);
        m_cameraFrustumPlanes[1] /= Mathf.Sqrt(m_cameraFrustumPlanes[1].x * m_cameraFrustumPlanes[1].x + m_cameraFrustumPlanes[1].y * m_cameraFrustumPlanes[1].y + m_cameraFrustumPlanes[1].z * m_cameraFrustumPlanes[1].z);

        // Bottom clipping plane.
        m_cameraFrustumPlanes[2] = new Vector4(m_MVP[3] + m_MVP[1], m_MVP[7] + m_MVP[5], m_MVP[11] + m_MVP[9], m_MVP[15] + m_MVP[13]);
        m_cameraFrustumPlanes[2] /= Mathf.Sqrt(m_cameraFrustumPlanes[2].x * m_cameraFrustumPlanes[2].x + m_cameraFrustumPlanes[2].y * m_cameraFrustumPlanes[2].y + m_cameraFrustumPlanes[2].z * m_cameraFrustumPlanes[2].z);

        // Top clipping plane.
        m_cameraFrustumPlanes[3] = new Vector4(m_MVP[3] - m_MVP[1], m_MVP[7] - m_MVP[5], m_MVP[11] - m_MVP[9], m_MVP[15] - m_MVP[13]);
        m_cameraFrustumPlanes[3] /= Mathf.Sqrt(m_cameraFrustumPlanes[3].x * m_cameraFrustumPlanes[3].x + m_cameraFrustumPlanes[3].y * m_cameraFrustumPlanes[3].y + m_cameraFrustumPlanes[3].z * m_cameraFrustumPlanes[3].z);

        // Near clipping plane.
        m_cameraFrustumPlanes[4] = new Vector4(m_MVP[3] + m_MVP[2], m_MVP[7] + m_MVP[6], m_MVP[11] + m_MVP[10], m_MVP[15] + m_MVP[14]);
        m_cameraFrustumPlanes[4] /= Mathf.Sqrt(m_cameraFrustumPlanes[4].x * m_cameraFrustumPlanes[4].x + m_cameraFrustumPlanes[4].y * m_cameraFrustumPlanes[4].y + m_cameraFrustumPlanes[4].z * m_cameraFrustumPlanes[4].z);

        // Far clipping plane.
        m_cameraFrustumPlanes[5] = new Vector4(m_MVP[3] - m_MVP[2], m_MVP[7] - m_MVP[6], m_MVP[11] - m_MVP[10], m_MVP[15] - m_MVP[14]);
        m_cameraFrustumPlanes[5] /= Mathf.Sqrt(m_cameraFrustumPlanes[5].x * m_cameraFrustumPlanes[5].x + m_cameraFrustumPlanes[5].y * m_cameraFrustumPlanes[5].y + m_cameraFrustumPlanes[5].z * m_cameraFrustumPlanes[5].z);
    }

    #endregion

    #region Public Functions

    public void Initialize(IndirectInstanceData[] _instances)
    {
        m_instances = _instances;
        m_numberOfInstanceTypes = m_instances.Length;

        m_00_lodSortingCSKernelID = m_00_lodSortingCS.FindKernel("BitonicSort");
        m_00_lodSortingTransposeCSKernelID = m_00_lodSortingCS.FindKernel("MatrixTranspose");
        m_01_occlusionKernelID = m_01_occlusionCS.FindKernel("CSMain");
        m_02_scanInstancesKernelID = m_02_scanInstancesCS.FindKernel("CSMain");
        m_03_scanGroupSumsKernelID = m_03_scanGroupSumsCS.FindKernel("CSMain");
        m_04_copyInstanceDataKernelID = m_04_copyInstanceDataCS.FindKernel("CSMain");
        m_05_calcInstanceOffsetsKernelID = m_05_calcInstanceOffsetsCS.FindKernel("CSMain");

        int materialPropertyCounter = 0;
        int instanceCounter = 0;

        List<InstanceData> allInstancesPositionsList = new List<InstanceData>();
        m_renderers = new IndirectRenderingMesh[m_numberOfInstanceTypes];
        m_args = new uint[m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE];
        for (int i = 0; i < m_numberOfInstanceTypes; i++)
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
                true,       // Merge Submeshes 
                false,      // Use Matrices
                false       // Has lightmap data
            );

            // Arguments
            int argsIndex = i * NUMBER_OF_ARGS_PER_INSTANCE;

            // Buffer with arguments has to have five integer numbers
            // LOD00
            m_args[argsIndex + 0] = data.lod00Mesh.GetIndexCount(0);                // 0 - index count per instance, 
            m_args[argsIndex + 1] = 0;                                              // 1 - instance count
            m_args[argsIndex + 2] = 0;                                              // 2 - start index location
            m_args[argsIndex + 3] = 0;                                              // 3 - base vertex location
            m_args[argsIndex + 4] = 0;                                              // 4 - start instance location

            // LOD01
            m_args[argsIndex + 5] = data.lod01Mesh.GetIndexCount(0);                // 0 - index count per instance, 
            m_args[argsIndex + 6] = 0;                                              // 1 - instance count
            m_args[argsIndex + 7] = m_args[argsIndex + 0] + m_args[argsIndex + 2];  // 2 - start index location
            m_args[argsIndex + 8] = 0;                                              // 3 - base vertex location
            m_args[argsIndex + 9] = 0;                                              // 4 - start instance location

            // LOD02
            m_args[argsIndex + 10] = data.lod02Mesh.GetIndexCount(0);               // 0 - index count per instance, 
            m_args[argsIndex + 11] = 0;                                             // 1 - instance count
            m_args[argsIndex + 12] = m_args[argsIndex + 5] + m_args[argsIndex + 7]; // 2 - start index location
            m_args[argsIndex + 13] = 0;                                             // 3 - base vertex location
            m_args[argsIndex + 14] = 0;                                             // 4 - start instance location

            // Shadow
            m_args[argsIndex + 15] = m_args[argsIndex + 10];                        // 0 - index count per instance, 
            m_args[argsIndex + 16] = 0;                                             // 1 - instance count
            m_args[argsIndex + 17] = m_args[argsIndex + 12];                        // 2 - start index location
            m_args[argsIndex + 18] = 0;                                             // 3 - base vertex location
            m_args[argsIndex + 19] = 0;                                             // 4 - start instance location

            // Materials
            irm.Lod00MatPropBlock = new MaterialPropertyBlock();
            irm.Lod01MatPropBlock = new MaterialPropertyBlock();
            irm.Lod02MatPropBlock = new MaterialPropertyBlock();
            irm.ShadowMatPropBlock = new MaterialPropertyBlock();

            // ----------------------------------------------------------
            // Silly workaround for a shadow bug.
            // If we don't set a unique value to the property block we 
            // only get shadows in one of our draw calls. 
            irm.Lod00MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, 1);
            irm.Lod01MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, 2);
            irm.Lod02MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, 3);
            irm.ShadowMatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, 4);
            // End of silly workaround!
            // ----------------------------------------------------------

            irm.material = new Material(data.material);

            // Add the instance data (positions, rotations, scaling, bounds...)
            for (int j = 0; j < m_instances[i].positions.Length; j++)
            {
                instanceCounter++;
                IndirectInstanceData _data = m_instances[i];
                InstanceData newData = new InstanceData();

                Bounds b = new Bounds();
                b.Encapsulate(_data.lod00Mesh.bounds);
                b.Encapsulate(_data.lod01Mesh.bounds);
                b.Encapsulate(_data.lod02Mesh.bounds);
                b.extents *= _data.uniformScales[j];

                newData.drawCallID = (uint)i * NUMBER_OF_ARGS_PER_INSTANCE;
                newData.position = _data.positions[j];
                newData.rotation = _data.rotations[j];
                newData.uniformScale = _data.uniformScales[j];
                newData.boundsCenter = _data.positions[j];
                newData.boundsExtents = b.extents;
                newData.distanceToCamera = Vector3.Distance(newData.position, m_camera.transform.position);

                irm.computeInstances.Add(newData);
                allInstancesPositionsList.Add(newData);
            }

            // Add the data to the renderer list
            m_renderers[i] = irm;
        }

        // HACK! 
        // Padding the buffer with data so the size is in power of two.
        // Reason is that the current implementation of the Bitonic Sort 
        // only supports that. On the todo list to fix!
        if (!Mathf.IsPowerOfTwo(instanceCounter))
        {
            int iterations = Mathf.NextPowerOfTwo(instanceCounter) - instanceCounter;
            for (int j = 0; j < iterations; j++)
            {
                InstanceData newData = new InstanceData()
                {
                    drawCallID = HACK_POT_PADDING_DRAW_ID
                };
                m_renderers[0].computeInstances.Add(newData);
                allInstancesPositionsList.Add(newData);
            }
        }

        instancesPositionsArray = allInstancesPositionsList.ToArray();
        m_numberOfInstances = allInstancesPositionsList.Count;

        int computeShaderInputSize = Marshal.SizeOf(typeof(InstanceData));
        int computeShaderOutputSize = Marshal.SizeOf(typeof(InstanceDrawData));

        m_argsBuffer = new ComputeBuffer(m_numberOfInstanceTypes, sizeof(uint) * NUMBER_OF_ARGS_PER_INSTANCE, ComputeBufferType.IndirectArguments);
        m_instanceDataBuffer = new ComputeBuffer(m_numberOfInstances, computeShaderInputSize, ComputeBufferType.Default);
        m_lodDistancesTempBuffer = new ComputeBuffer(m_numberOfInstances, computeShaderInputSize, ComputeBufferType.Default);
        m_culledInstanceBuffer = new ComputeBuffer(m_numberOfInstances, computeShaderOutputSize, ComputeBufferType.Default);
        m_isVisibleBuffer = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
        m_scannedInstancePredicates = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
        m_groupSumArray = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
        m_scannedGroupSumBuffer = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);

        m_argsBuffer.SetData(m_args);
        m_instanceDataBuffer.SetData(instancesPositionsArray);
        m_lodDistancesTempBuffer.SetData(instancesPositionsArray);

        for (int i = 0; i < m_renderers.Length; i++)
        {
            IndirectRenderingMesh irm = m_renderers[i];
            irm.Lod00MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.Lod01MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.Lod02MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.ShadowMatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);

            int argsIndex = i * NUMBER_OF_ARGS_PER_INSTANCE;
            if (Application.platform == RuntimePlatform.WindowsEditor
                || Application.platform == RuntimePlatform.WindowsPlayer)
            {
                irm.Lod00MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
                irm.Lod01MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
                irm.Lod02MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
                irm.ShadowMatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);

                irm.Lod00MatPropBlock.SetInt("_ArgsOffset", argsIndex + 4);
                irm.Lod01MatPropBlock.SetInt("_ArgsBuffer", argsIndex + 9);
                irm.Lod02MatPropBlock.SetInt("_ArgsBuffer", argsIndex + 14);
                irm.ShadowMatPropBlock.SetInt("_ArgsBuffer", argsIndex + 19);
            }
        }
    }

    private bool AreDirectionsFacingEachother(Vector3 dir1, Vector3 dir2)
    {
        return Vector3.Dot(dir1, dir2) <= 0.0f;
    }

    #endregion


    #region Debugging

    void OnDestroyEditor()
    {
        Destroy(m_debugBoundsMesh);

        if (m_debugDrawBoundsArgsBuffer != null)
        {
            m_debugDrawBoundsArgsBuffer.Release();
        }
        if (m_debugDrawBoundsPositionBuffer != null)
        {
            m_debugDrawBoundsPositionBuffer.Release();
        }
    }

    public void DrawBounds()
    {
        int threadGroupCount = m_numberOfInstances;
        m_debugDrawBoundsCS.SetBuffer(m_debugDrawBoundsCS.FindKernel("DrawBounds"), "_InstanceBuffer", m_instanceDataBuffer);

        int sizeOfBuffer = threadGroupCount * 3 * 12 * 2;

        // Allocate/Reallocate the compute buffers when it hasn't been
        // initialized or the triangle count was changed from the last frame.
        if (m_debugDrawBoundsPositionBuffer == null || m_debugDrawBoundsPositionBuffer.count != sizeOfBuffer)
        {
            // Mesh with single triangle.
            m_debugBoundsMesh = new Mesh();
            m_debugBoundsMesh.vertices = new Vector3[3];
            m_debugBoundsMesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            m_debugBoundsMesh.UploadMeshData(true);

            // Allocate the indirect draw args buffer.
            m_debugDrawBoundsArgsBuffer = new ComputeBuffer(
                1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments
            );

            // Clone the given material before using.
            m_debugDrawBoundsMaterial = new Material(m_debugDrawBoundsMaterial);
            m_debugDrawBoundsMaterial.name += " (cloned)";
            m_debugDrawBoundsProps = new MaterialPropertyBlock();

            if (m_debugDrawBoundsPositionBuffer != null)
            {
                m_debugDrawBoundsPositionBuffer.Release();
            }

            m_debugDrawBoundsPositionBuffer = new ComputeBuffer(sizeOfBuffer, 16);
            m_debugDrawBoundsArgsBuffer.SetData(new uint[5] { 3, (uint)sizeOfBuffer / 3, 0, 0, 0 });
        }

        // Invoke the update compute kernel.
        var kernel = m_debugDrawBoundsCS.FindKernel("DrawBounds");

        m_debugDrawBoundsCS.SetBuffer(kernel, "_PositionBuffer", m_debugDrawBoundsPositionBuffer);
        m_debugDrawBoundsCS.Dispatch(kernel, threadGroupCount, 1, 1);

        // Draw the mesh with instancing.
        m_debugDrawBoundsMaterial.SetMatrix("_LocalToWorld", Matrix4x4.identity);
        m_debugDrawBoundsMaterial.SetMatrix("_WorldToLocal", Matrix4x4.identity);
        m_debugDrawBoundsMaterial.SetBuffer("_PositionBuffer", m_debugDrawBoundsPositionBuffer);

        Graphics.DrawMeshInstancedIndirect(
            m_debugBoundsMesh, 0, m_debugDrawBoundsMaterial,
            m_bounds,
            m_debugDrawBoundsArgsBuffer, 0, m_debugDrawBoundsProps, ShadowCastingMode.Off, false
        );
    }

    private void Log00Sorting()
    {
        if (m_debugLog != DebugLog.AfterSorting)
        {
            return;
        }
        m_debugLog = DebugLog.DontLog;

        StringBuilder sb = new StringBuilder();
        InstanceData[] distanceData = new InstanceData[m_numberOfInstances];
        m_instanceDataBuffer.GetData(distanceData);
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
        uint[] argsData = new uint[m_instances.Length * NUMBER_OF_ARGS_PER_INSTANCE];
        m_argsBuffer.GetData(argsData);
        sb.AppendLine("01 argsData:");
        for (int i = 0; i < argsData.Length; i++)
        {
            sb.Append(argsData[i] + " ");

            if ((i + 1) % 5 == 0)
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
        InstanceDrawData[] culledInstancesData = new InstanceDrawData[m_numberOfInstances];
        m_culledInstanceBuffer.GetData(culledInstancesData);
        sb.AppendLine("04 culledInstances:");
        for (int i = 0; i < culledInstancesData.Length; i++)
        {
            sb.AppendLine(i + ": " + culledInstancesData[i].position + " " + culledInstancesData[i].rotation + ": " + culledInstancesData[i].uniformScale);
        }
        Debug.Log(sb.ToString());
    }

    private void Log05CalculateInstanceOffsets()
    {
        if (m_debugLog != DebugLog.AfterCalcInstanceOffsets)
        {
            return;
        }
        m_debugLog = DebugLog.DontLog;

        StringBuilder sb = new StringBuilder();
        uint[] argsData = new uint[m_instances.Length * NUMBER_OF_ARGS_PER_INSTANCE];
        m_argsBuffer.GetData(argsData);
        sb.AppendLine("05 argsData:");
        for (int i = 0; i < argsData.Length; i++)
        {
            sb.Append(argsData[i] + " ");

            if ((i + 1) % 5 == 0)
            {
                sb.AppendLine("");
            }
        }
        Debug.Log(sb.ToString());
    }

    #endregion
}