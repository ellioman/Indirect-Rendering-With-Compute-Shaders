using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// 16 * 4 bytes = 64 bytes
[System.Serializable]
public struct InstanceData
{
    public uint drawDataID;       // 1
    public uint drawCallID;		  // 2
    public Vector3 position;      // 5
    public Vector3 rotation;      // 8
    public float uniformScale;     // 9
    public Vector3 boundsCenter;  // 12
    public Vector3 boundsExtents; // 15
    public float distanceToCamera; // 16
}

// 32 * 4 bytes = 128 bytes
public struct InstanceDrawData
{
    public Matrix4x4 unity_ObjectToWorld;
    public Matrix4x4 unity_WorldToObject;
};

[System.Serializable]
public class IndirectRenderingMesh
{
    public List<InstanceData> computeInstances = new List<InstanceData>();
    public Mesh mesh = null;
    public Mesh shadowMesh = null;
    public Material material = null;
    public MaterialPropertyBlock Lod00MatPropBlock;
    public MaterialPropertyBlock Lod01MatPropBlock;
    public MaterialPropertyBlock Lod02MatPropBlock;
    public MaterialPropertyBlock ShadowMatPropBlock;
    public uint numOfVerticesLod00 = 0;
    public uint numOfVerticesLod01 = 0;
    public uint numOfVerticesLod02 = 0;
    public uint numOfVerticesShadow = 0;
    public uint numOfIndicesLod00 = 0;
    public uint numOfIndicesLod01 = 0;
    public uint numOfIndicesLod02 = 0;
    public uint numOfIndicesShadow = 0;
}

public class IndirectRenderer : MonoBehaviour
{
    #region Variables

    [Header("Settings")]
    public bool isEnabled = true;
    public bool m_enableFrustumCulling = true;
    public bool m_enableOcclusionCulling = true;
    public bool m_enableDetailCulling = true;
    public bool m_enableLOD = true;
    public bool m_enableSimpleShadows = false;
    [Range(0f, 0.02f)] public float m_detailCullingScreenPercentage = 0.005f;
    public float sortCamDistance = 10f;


    [Header("References")]
    public ComputeShader m_00_CreateInstanceDrawBufferCS = null;
    public ComputeShader m_01_lodSortingCS = null;
    public ComputeShader m_02_occlusionCS = null;
    public ComputeShader m_03_scanInstancesCS = null;
    public ComputeShader m_04_scanGroupSumsCS = null;
    public ComputeShader m_05_copyInstanceDataCS = null;
    public HiZBuffer m_hiZBuffer;
    public Camera m_camera;
    public Camera debugCamera;
    public Material shadowMatrixMaterial;

    // Debugging Variables
    [Header("Debug")]
    public bool m_debugDrawLOD = false;
    public bool m_debugDrawHiZ = false;
    [Range(0, 10)] public int m_debugHiZLod = 0;
    [Space(10f)]
    public bool m_debugLogStats = false;
    public bool m_debugLog00 = false;
    public bool m_debugLog01 = false;
    public bool m_debugLog02 = false;
    public bool m_debugLog03 = false;
    public bool m_debugLog04 = false;
    public bool m_debugLog05 = false;

    
    [Space(10f)]

    // Compute Buffers
    private ComputeBuffer m_argsBuffer = null;
    private ComputeBuffer m_instanceDataBuffer = null;
    private ComputeBuffer m_instanceDrawDataBuffer = null;
    private ComputeBuffer m_culledInstanceBuffer = null;
    private ComputeBuffer m_lodDistancesTempBuffer = null;
    private ComputeBuffer m_isVisibleBuffer = null;
    private ComputeBuffer m_groupSumArray = null;
    private ComputeBuffer m_scannedGroupSumBuffer = null;
    private ComputeBuffer m_scannedInstancePredicates = null;
    private ComputeBuffer m_debugDrawBoundsArgsBuffer;
    private ComputeBuffer m_debugDrawBoundsPositionBuffer;

    // Command Buffers
    private CommandBuffer sortingCommandBuffer = null;

    // Kernel ID's
    private int m_00_CreateInstanceDrawBufferCSKernelID;
    private int m_01_lodSortingCSKernelID;
    private int m_01_lodSortingTransposeCSKernelID;
    private int m_02_occlusionKernelID;
    private int m_03_scanInstancesKernelID;
    private int m_04_scanGroupSumsKernelID;
    private int m_05_copyInstanceDataKernelID;

    // Other
    private int m_numberOfInstanceTypes = 0;
    private int m_numberOfInstances = 0;
    private bool m_debugLastShowLOD = false;
    private Mesh m_debugBoundsMesh = null;
    private uint[] m_args = null;
    private Bounds m_bounds = new Bounds();
    private Vector3 m_camPosition = Vector3.zero;
    private Vector3 m_lastCamPos = Vector3.zero;
    private Matrix4x4 m_MVP;
    private RenderTexture shadowMatrixTexture = null;
    private InstanceData[] instancesPositionsArray = null;
    private MaterialPropertyBlock m_debugDrawBoundsProps;
    private IndirectInstanceData[] m_instances = null;
    private IndirectRenderingMesh[] m_indirectMeshes = null;

    // Constants
    private const int NUMBER_OF_ARGS_PER_DRAW = 5;
    private const int NUMBER_OF_DRAW_CALLS = 4; // (LOD00 + LOD01 + LOD02 + SHADOW)
    private const int NUMBER_OF_ARGS_PER_INSTANCE_TYPE = NUMBER_OF_ARGS_PER_DRAW * NUMBER_OF_DRAW_CALLS; // 20
    private const int ARGS_BYTE_SIZE_PER_INSTANCE_TYPE = NUMBER_OF_ARGS_PER_INSTANCE_TYPE * sizeof(uint); // 80
    private const int SCAN_THREAD_GROUP_SIZE = 64;
    
    #endregion

    #region MonoBehaviour

    private void Update()
    {
        if (!isEnabled)
        {
            return;
        }
        
        m_hiZBuffer.DebugLodLevel = m_debugHiZLod;
        
        if (m_debugLastShowLOD != m_debugDrawLOD)
        {
            m_debugLastShowLOD = m_debugDrawLOD;
            for (int i = 0; i < m_indirectMeshes.Length; i++)
            {
                m_indirectMeshes[i].Lod00MatPropBlock.SetColor("_Color", m_debugDrawLOD ? Color.red : Color.white);
                m_indirectMeshes[i].Lod01MatPropBlock.SetColor("_Color", m_debugDrawLOD ? Color.green : Color.white);
                m_indirectMeshes[i].Lod02MatPropBlock.SetColor("_Color", m_debugDrawLOD ? Color.blue : Color.white);
            }
        }
    }

    private void OnPreCull()
    {
        if (!isEnabled
            || m_indirectMeshes == null
            || m_indirectMeshes.Length == 0
            || m_hiZBuffer.HiZDepthTexture == null
            )
        {
            return;
        }
        
        CalculateVisibleInstances();
        DrawVisibleInstances();
    }

    private void OnDestroy()
    {
        if (m_argsBuffer != null                    ) { m_argsBuffer.Release(); }
        if (m_groupSumArray != null                 ) { m_groupSumArray.Release(); }
        if (m_isVisibleBuffer != null               ) { m_isVisibleBuffer.Release(); }
        if (sortingCommandBuffer != null            ) { sortingCommandBuffer.Release(); }
        if (m_instanceDataBuffer != null            ) { m_instanceDataBuffer.Release(); }
        if (m_culledInstanceBuffer != null	        ) { m_culledInstanceBuffer.Release(); }
        if (m_scannedGroupSumBuffer != null         ) { m_scannedGroupSumBuffer.Release(); }
        if (m_lodDistancesTempBuffer != null        ) { m_lodDistancesTempBuffer.Release(); }
        if (m_instanceDrawDataBuffer != null        ) { m_instanceDrawDataBuffer.Release(); }
        if (m_scannedInstancePredicates != null     ) { m_scannedInstancePredicates.Release(); }
        if (m_debugDrawBoundsArgsBuffer != null     ) { m_debugDrawBoundsArgsBuffer.Release(); }
        if (m_debugDrawBoundsPositionBuffer != null ) { m_debugDrawBoundsPositionBuffer.Release(); }
        
        Destroy(m_debugBoundsMesh);
    }
    
    // http://answers.unity.com/answers/477208/view.html
    public void OnDrawGizmos() 
    {
        if (m_camera == null)
        {
            return;
        }
        
        Matrix4x4 temp = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        if (m_camera.orthographic)
        {
            float spread = m_camera.farClipPlane - m_camera.nearClipPlane;
            float center = (m_camera.farClipPlane + m_camera.nearClipPlane)*0.5f;
            Gizmos.DrawWireCube(new Vector3(0,0,center), new Vector3(m_camera.orthographicSize*2*m_camera.aspect, m_camera.orthographicSize*2, spread));
        }
        else
        {
            Gizmos.DrawFrustum(Vector3.zero, m_camera.fieldOfView, m_camera.farClipPlane, m_camera.nearClipPlane, m_camera.aspect);
        }
        Gizmos.matrix = temp;
    }

    #endregion

    #region Private Functions

    private void DrawVisibleInstances()
    {
        if (m_enableSimpleShadows)
        {
            for (int i = 0; i < m_indirectMeshes.Length; i++)
            {
                int argsIndex = i * ARGS_BYTE_SIZE_PER_INSTANCE_TYPE;
                IndirectRenderingMesh irm = m_indirectMeshes[i];
                Graphics.DrawMeshInstancedIndirect(irm.mesh,       0, irm.material, m_bounds, m_argsBuffer, argsIndex + NUMBER_OF_ARGS_PER_INSTANCE_TYPE * 0, irm.Lod00MatPropBlock,  ShadowCastingMode.Off);
                Graphics.DrawMeshInstancedIndirect(irm.mesh,       0, irm.material, m_bounds, m_argsBuffer, argsIndex + NUMBER_OF_ARGS_PER_INSTANCE_TYPE * 1, irm.Lod01MatPropBlock,  ShadowCastingMode.Off);
                Graphics.DrawMeshInstancedIndirect(irm.mesh,       0, irm.material, m_bounds, m_argsBuffer, argsIndex + NUMBER_OF_ARGS_PER_INSTANCE_TYPE * 2, irm.Lod02MatPropBlock,  ShadowCastingMode.Off);
                Graphics.DrawMeshInstancedIndirect(irm.shadowMesh, 0, irm.material, m_bounds, m_argsBuffer, argsIndex + NUMBER_OF_ARGS_PER_INSTANCE_TYPE * 3, irm.ShadowMatPropBlock, ShadowCastingMode.ShadowsOnly);
            }
        }
        else
        {
            for (int i = 0; i < m_indirectMeshes.Length; i++)
            {
                int argsIndex = i * ARGS_BYTE_SIZE_PER_INSTANCE_TYPE;
                IndirectRenderingMesh irm = m_indirectMeshes[i];
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, argsIndex + NUMBER_OF_ARGS_PER_INSTANCE_TYPE * 0, irm.Lod00MatPropBlock, ShadowCastingMode.On);
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, argsIndex + NUMBER_OF_ARGS_PER_INSTANCE_TYPE * 1, irm.Lod01MatPropBlock, ShadowCastingMode.On);
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, argsIndex + NUMBER_OF_ARGS_PER_INSTANCE_TYPE * 2, irm.Lod02MatPropBlock, ShadowCastingMode.On);
            }
        }
        
        if (m_debugDrawHiZ)
        {
            debugCamera.Render();
        }
    }
    
    private void CalculateVisibleInstances()
    {
        // Global data
        m_camPosition = m_camera.transform.position;
        
        //Matrix4x4 M = m_camera.transform.localToWorldMatrix;
        Matrix4x4 V = m_camera.worldToCameraMatrix;
        Matrix4x4 P = m_camera.projectionMatrix;
        m_MVP = P * V;//*M;
        
        m_bounds.center = m_camPosition;
        m_bounds.extents = Vector3.one * 10000;
        
        
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
        Profiler.BeginSample("01 LOD Sorting");
        {
            RunGPUSorting(ref m_camPosition);
            Log01Sorting();
        }
        Profiler.EndSample();
        
        //////////////////////////////////////////////////////
        // Set up compute shader to perform the occlusion culling
        //////////////////////////////////////////////////////
        Profiler.BeginSample("02 Occlusion");
        {
            // Input
            m_02_occlusionCS.SetInt("_Cascades", QualitySettings.shadowCascades);
            m_02_occlusionCS.SetInt("_ShouldFrustumCull", m_enableFrustumCulling ? 1 : 0);
            m_02_occlusionCS.SetInt("_ShouldOcclusionCull", m_enableOcclusionCulling ? 1 : 0);
            m_02_occlusionCS.SetInt("_ShouldDetailCull", m_enableDetailCulling ? 1 : 0);
            m_02_occlusionCS.SetInt("_ShouldLOD", m_enableLOD ? 1 : 0);
            m_02_occlusionCS.SetFloat("_DetailCullingScreenPercentage", m_detailCullingScreenPercentage);
            m_02_occlusionCS.SetMatrix("_UNITY_MATRIX_MVP", m_MVP);
            m_02_occlusionCS.SetVector("_HiZTextureSize", m_hiZBuffer.TextureSize);
            m_02_occlusionCS.SetVector("_CamPosition", m_camPosition);
            m_02_occlusionCS.SetBuffer(m_02_occlusionKernelID, "_InstanceDataBuffer", m_instanceDataBuffer);
            m_02_occlusionCS.SetTexture(m_02_occlusionKernelID, "_Unity_WorldToShadow", GetWorldToShadowMatrixTexture());
            m_02_occlusionCS.SetTexture(m_02_occlusionKernelID, "_HiZMap", m_hiZBuffer.HiZDepthTexture);
            
            // Output
            m_02_occlusionCS.SetBuffer(m_02_occlusionKernelID, "_ArgsBuffer", m_argsBuffer);
            m_02_occlusionCS.SetBuffer(m_02_occlusionKernelID, "_IsVisibleBuffer", m_isVisibleBuffer);
            
            // Dispatch
            int groupX = Mathf.Max(m_numberOfInstances / 64, 1);
            m_02_occlusionCS.Dispatch(m_02_occlusionKernelID, groupX, 1, 1);
            
            // Debug
            Log02Culling();
        }
        Profiler.EndSample();
        
        //////////////////////////////////////////////////////
        // Perform scan of instance predicates
        //////////////////////////////////////////////////////
        Profiler.BeginSample("03 Scan Instances");
        {
            int groupX = m_numberOfInstances / (2 * SCAN_THREAD_GROUP_SIZE);
            
            // Input
            m_03_scanInstancesCS.SetBuffer(m_03_scanInstancesKernelID, "_InstancePredicatesIn", m_isVisibleBuffer);
            
            // Output
            m_03_scanInstancesCS.SetBuffer(m_03_scanInstancesKernelID, "_GroupSumArray", m_groupSumArray);
            m_03_scanInstancesCS.SetBuffer(m_03_scanInstancesKernelID, "_ScannedInstancePredicates", m_scannedInstancePredicates);
            
            // Dispatch
            m_03_scanInstancesCS.Dispatch(m_03_scanInstancesKernelID, groupX, 1, 1);
            
            // Debug
            Log03ScanInstances();
        }
        Profiler.EndSample();
        
        //////////////////////////////////////////////////////
        // Perform scan of group sums
        //////////////////////////////////////////////////////
        Profiler.BeginSample("04 Scan Thread Groups");
        {
            // Input
            int numOfGroups = m_numberOfInstances / (2 * SCAN_THREAD_GROUP_SIZE);
            m_04_scanGroupSumsCS.SetInt("_NumOfGroups", numOfGroups);
            m_04_scanGroupSumsCS.SetBuffer(m_04_scanGroupSumsKernelID, "_GroupSumArrayIn", m_groupSumArray);
            
            // Output
            m_04_scanGroupSumsCS.SetBuffer(m_04_scanGroupSumsKernelID, "_GroupSumArrayOut", m_scannedGroupSumBuffer);
            
            // Dispatch
            m_04_scanGroupSumsCS.Dispatch(m_04_scanGroupSumsKernelID, 1, 1, 1);
            
            // Debug
            Log04ScanGroupSums();
        }
        Profiler.EndSample();
        
        //////////////////////////////////////////////////////
        // Perform stream compaction 
        // Calculate instance offsets and store in drawcall arguments buffer
        //////////////////////////////////////////////////////
        Profiler.BeginSample("05 Copy Instance Data");
        {
            int groupX = m_numberOfInstances / (2 * SCAN_THREAD_GROUP_SIZE);
            
            // Input
            m_05_copyInstanceDataCS.SetInt("_NumberOfInstanceTypes", m_numberOfInstanceTypes * 4);
            m_05_copyInstanceDataCS.SetBuffer(m_05_copyInstanceDataKernelID, "_InstanceDrawData", m_instanceDrawDataBuffer);
            m_05_copyInstanceDataCS.SetBuffer(m_05_copyInstanceDataKernelID, "_InstancePredicatesIn", m_isVisibleBuffer);
            m_05_copyInstanceDataCS.SetBuffer(m_05_copyInstanceDataKernelID, "_GroupSumArray", m_scannedGroupSumBuffer);
            m_05_copyInstanceDataCS.SetBuffer(m_05_copyInstanceDataKernelID, "_ScannedInstancePredicates", m_scannedInstancePredicates);
            
            // Output
            m_05_copyInstanceDataCS.SetBuffer(m_05_copyInstanceDataKernelID, "_DrawcallDataOut", m_argsBuffer);
            m_05_copyInstanceDataCS.SetBuffer(m_05_copyInstanceDataKernelID, "_InstanceDataOut", m_culledInstanceBuffer);
            
            // Dispatch
            m_05_copyInstanceDataCS.Dispatch(m_05_copyInstanceDataKernelID, groupX, 1, 1);
            
            // Debug
            Log05CopyInstanceData();
        }
        Profiler.EndSample();
        
        LogStats();
    }
    
    private RenderTexture GetWorldToShadowMatrixTexture()
    {
        if (shadowMatrixTexture == null)
        {
            shadowMatrixTexture = new RenderTexture(7, 1, 0, RenderTextureFormat.ARGBFloat);
            shadowMatrixTexture.filterMode = FilterMode.Point;
        }
        shadowMatrixMaterial.SetFloat("_Cascades", QualitySettings.shadowCascades);
        Graphics.Blit(null, shadowMatrixTexture, shadowMatrixMaterial);
        return shadowMatrixTexture;
    }

    private void RunGPUSorting(ref Vector3 _cameraPosition)
    {
        if (Vector3.Distance(m_lastCamPos, _cameraPosition) > sortCamDistance)
        {
            m_lastCamPos = _cameraPosition;
            Graphics.ExecuteCommandBufferAsync(sortingCommandBuffer, ComputeQueueType.Background);
        }
    }

    private void CreateCommandBuffers()
    {
        CreateSortingCommandBuffer();
    }

    private void CreateSortingCommandBuffer()
    {
        uint BITONIC_BLOCK_SIZE = 256;
        uint TRANSPOSE_BLOCK_SIZE = 8;
        
        // Determine parameters.
        uint NUM_ELEMENTS = (uint)m_numberOfInstances;
        uint MATRIX_WIDTH = BITONIC_BLOCK_SIZE;
        uint MATRIX_HEIGHT = (uint)NUM_ELEMENTS / BITONIC_BLOCK_SIZE;
        
        sortingCommandBuffer = new CommandBuffer();
        sortingCommandBuffer.name = "AsyncGPUSorting";
        
        // Sort the data
        // First sort the rows for the levels <= to the block size
        for (uint level = 2; level <= BITONIC_BLOCK_SIZE; level <<= 1)
        {
            SetGPUSortConstants(ref sortingCommandBuffer, ref m_01_lodSortingCS, ref level, ref level, ref MATRIX_HEIGHT, ref MATRIX_WIDTH);
            
            // Sort the row data
            sortingCommandBuffer.SetComputeBufferParam(m_01_lodSortingCS, m_01_lodSortingCSKernelID, "Data", m_instanceDataBuffer);
            sortingCommandBuffer.DispatchCompute(m_01_lodSortingCS, m_01_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
        }
        
        // Then sort the rows and columns for the levels > than the block size
        // Transpose. Sort the Columns. Transpose. Sort the Rows.
        for (uint level = (BITONIC_BLOCK_SIZE << 1); level <= NUM_ELEMENTS; level <<= 1)
        {
            // Transpose the data from buffer 1 into buffer 2
            uint l = (level / BITONIC_BLOCK_SIZE);
            uint lm = (level & ~NUM_ELEMENTS) / BITONIC_BLOCK_SIZE;
            SetGPUSortConstants(ref sortingCommandBuffer, ref m_01_lodSortingCS, ref l, ref lm, ref MATRIX_WIDTH, ref MATRIX_HEIGHT);
            sortingCommandBuffer.SetComputeBufferParam(m_01_lodSortingCS, m_01_lodSortingTransposeCSKernelID, "Input", m_instanceDataBuffer);
            sortingCommandBuffer.SetComputeBufferParam(m_01_lodSortingCS, m_01_lodSortingTransposeCSKernelID, "Data", m_lodDistancesTempBuffer);
            sortingCommandBuffer.DispatchCompute(m_01_lodSortingCS, m_01_lodSortingTransposeCSKernelID, (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), 1);
            
            // Sort the transposed column data
            sortingCommandBuffer.SetComputeBufferParam(m_01_lodSortingCS, m_01_lodSortingCSKernelID, "Data", m_lodDistancesTempBuffer);
            sortingCommandBuffer.DispatchCompute(m_01_lodSortingCS, m_01_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
            
            // Transpose the data from buffer 2 back into buffer 1
            SetGPUSortConstants(ref sortingCommandBuffer, ref m_01_lodSortingCS, ref BITONIC_BLOCK_SIZE, ref level, ref MATRIX_HEIGHT, ref MATRIX_WIDTH);
            sortingCommandBuffer.SetComputeBufferParam(m_01_lodSortingCS, m_01_lodSortingTransposeCSKernelID, "Input", m_lodDistancesTempBuffer);
            sortingCommandBuffer.SetComputeBufferParam(m_01_lodSortingCS, m_01_lodSortingTransposeCSKernelID, "Data", m_instanceDataBuffer);
            sortingCommandBuffer.DispatchCompute(m_01_lodSortingCS, m_01_lodSortingTransposeCSKernelID, (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), 1);
            
            // Sort the row data
            sortingCommandBuffer.SetComputeBufferParam(m_01_lodSortingCS, m_01_lodSortingCSKernelID, "Data", m_instanceDataBuffer);
            sortingCommandBuffer.DispatchCompute(m_01_lodSortingCS, m_01_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
        }
    }
    
    private void SetGPUSortConstants(ref CommandBuffer commandBuffer, ref ComputeShader cs, ref uint level, ref uint levelMask, ref uint width, ref uint height)
    {
        commandBuffer.SetComputeIntParam(cs, "_Level", (int)level);
        commandBuffer.SetComputeIntParam(cs, "_LevelMask", (int)levelMask);
        commandBuffer.SetComputeIntParam(cs, "_Width", (int)width);
        commandBuffer.SetComputeIntParam(cs, "_Height", (int)height);
    }

    #endregion

    #region Public Functions

    public void Initialize(IndirectInstanceData[] _instances)
    {
        m_instances = _instances;
        m_numberOfInstanceTypes = m_instances.Length;
        
        m_01_lodSortingCSKernelID = m_01_lodSortingCS.FindKernel("BitonicSort");
        m_01_lodSortingTransposeCSKernelID = m_01_lodSortingCS.FindKernel("MatrixTranspose");
        m_02_occlusionKernelID = m_02_occlusionCS.FindKernel("CSMain");
        m_03_scanInstancesKernelID = m_03_scanInstancesCS.FindKernel("CSMain");
        m_04_scanGroupSumsKernelID = m_04_scanGroupSumsCS.FindKernel("CSMain");
        m_05_copyInstanceDataKernelID = m_05_copyInstanceDataCS.FindKernel("CSMain");
        
        int instanceCounter = 0;
        List<InstanceData> allInstancesPositionsList = new List<InstanceData>();
        m_indirectMeshes = new IndirectRenderingMesh[m_numberOfInstanceTypes];
        m_args = new uint[m_numberOfInstanceTypes * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
        for (int i = 0; i < m_numberOfInstanceTypes; i++)
        {
            IndirectRenderingMesh irm = new IndirectRenderingMesh();
            IndirectInstanceData data = m_instances[i];
            
            // Initialize Mesh
            irm.numOfVerticesLod00 = (uint)data.lod00Mesh.vertexCount;
            irm.numOfVerticesLod01 = (uint)data.lod01Mesh.vertexCount;
            irm.numOfVerticesLod02 = (uint)data.lod02Mesh.vertexCount;
            irm.numOfVerticesShadow = (uint)data.shadowMesh.vertexCount;
            irm.numOfIndicesLod00 = data.lod00Mesh.GetIndexCount(0);
            irm.numOfIndicesLod01 = data.lod01Mesh.GetIndexCount(0);
            irm.numOfIndicesLod02 = data.lod02Mesh.GetIndexCount(0);
            irm.numOfIndicesShadow = data.shadowMesh.GetIndexCount(0);
            
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
            
            irm.shadowMesh = data.shadowMesh;
            
            // Arguments
            int argsIndex = i * NUMBER_OF_ARGS_PER_INSTANCE_TYPE;
            
            // Buffer with arguments has to have five integer numbers
            // LOD00
            m_args[argsIndex + 0] = irm.numOfIndicesLod00;                          // 0 - index count per instance, 
            m_args[argsIndex + 1] = 0;                                              // 1 - instance count
            m_args[argsIndex + 2] = 0;                                              // 2 - start index location
            m_args[argsIndex + 3] = 0;                                              // 3 - base vertex location
            m_args[argsIndex + 4] = 0;                                              // 4 - start instance location
            
            // LOD01
            m_args[argsIndex + 5] = irm.numOfIndicesLod01;                          // 0 - index count per instance, 
            m_args[argsIndex + 6] = 0;                                              // 1 - instance count
            m_args[argsIndex + 7] = m_args[argsIndex + 0] + m_args[argsIndex + 2];  // 2 - start index location
            m_args[argsIndex + 8] = 0;                                              // 3 - base vertex location
            m_args[argsIndex + 9] = 0;                                              // 4 - start instance location
            
            // LOD02
            m_args[argsIndex + 10] = irm.numOfIndicesLod02;                         // 0 - index count per instance, 
            m_args[argsIndex + 11] = 0;                                             // 1 - instance count
            m_args[argsIndex + 12] = m_args[argsIndex + 5] + m_args[argsIndex + 7]; // 2 - start index location
            m_args[argsIndex + 13] = 0;                                             // 3 - base vertex location
            m_args[argsIndex + 14] = 0;                                             // 4 - start instance location
            
            // Shadow
            m_args[argsIndex + 15] = irm.numOfIndicesShadow;                        // 0 - index count per instance, 
            m_args[argsIndex + 16] = 0;                                             // 1 - instance count
            m_args[argsIndex + 17] = 0;                                             // 2 - start index location
            m_args[argsIndex + 18] = 0;                                             // 3 - base vertex location
            m_args[argsIndex + 19] = 0;                                             // 4 - start instance location
            
            // Materials
            irm.material = new Material(data.material);
            
            // Add the instance data (positions, rotations, scaling, bounds...)
            for (int j = 0; j < m_instances[i].positions.Length; j++)
            {
                instanceCounter++;
                IndirectInstanceData _data = m_instances[i];
                InstanceData newData = new InstanceData();
                
                // Calculate the renderer bounds
                GameObject obj = Instantiate(m_instances[i].prefab);
                obj.transform.localScale = obj.transform.localScale;
                Renderer[] rends = obj.GetComponentsInChildren<Renderer>();
                Bounds b = new Bounds();
                for (int r = 0; r < rends.Length; r++)
                {
                    b.Encapsulate(rends[r].bounds);
                }
                DestroyImmediate(obj);
                
                newData.drawDataID = (uint)(instanceCounter - 1);
                newData.drawCallID = (uint)argsIndex;
                newData.position = _data.positions[j];
                newData.rotation = _data.rotations[j];
                newData.uniformScale = _data.uniformScales[j];
                newData.boundsCenter = b.center;//_data.positions[j];
                newData.boundsExtents = b.extents * _data.uniformScales[j];
                newData.distanceToCamera = Vector3.Distance(_data.positions[j], m_camera.transform.position);
                irm.computeInstances.Add(newData);
                allInstancesPositionsList.Add(newData);
            }
            
            // Add the data to the renderer list
            m_indirectMeshes[i] = irm;
        }
        
        instancesPositionsArray = allInstancesPositionsList.ToArray();
        m_numberOfInstances = allInstancesPositionsList.Count;
        
        int computeShaderInputSize = Marshal.SizeOf(typeof(InstanceData));
        int computeShaderOutputSize = Marshal.SizeOf(typeof(InstanceDrawData));
        
        m_argsBuffer = new ComputeBuffer(m_numberOfInstanceTypes, sizeof(uint) * NUMBER_OF_ARGS_PER_INSTANCE_TYPE, ComputeBufferType.IndirectArguments);
        m_instanceDataBuffer = new ComputeBuffer(m_numberOfInstances, computeShaderInputSize, ComputeBufferType.Default);
        m_instanceDrawDataBuffer = new ComputeBuffer(m_numberOfInstances, computeShaderOutputSize, ComputeBufferType.Default);
        m_lodDistancesTempBuffer = new ComputeBuffer(m_numberOfInstances, computeShaderInputSize, ComputeBufferType.Default);
        m_culledInstanceBuffer = new ComputeBuffer(m_numberOfInstances, computeShaderOutputSize, ComputeBufferType.Default);
        m_isVisibleBuffer = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
        m_scannedInstancePredicates = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
        m_groupSumArray = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
        m_scannedGroupSumBuffer = new ComputeBuffer(m_numberOfInstances, sizeof(uint), ComputeBufferType.Default);
        
        m_argsBuffer.SetData(m_args);
        m_instanceDataBuffer.SetData(instancesPositionsArray);
        m_lodDistancesTempBuffer.SetData(instancesPositionsArray);
        
        // Setup the Material Property blocks for our meshes...
        int materialPropertyCounter = 0;
        for (int i = 0; i < m_indirectMeshes.Length; i++)
        {
            IndirectRenderingMesh irm = m_indirectMeshes[i];
            int argsIndex = i * NUMBER_OF_ARGS_PER_INSTANCE_TYPE;
            
            irm.Lod00MatPropBlock = new MaterialPropertyBlock();
            irm.Lod01MatPropBlock = new MaterialPropertyBlock();
            irm.Lod02MatPropBlock = new MaterialPropertyBlock();
            irm.ShadowMatPropBlock = new MaterialPropertyBlock();
            
            // ----------------------------------------------------------
            // Silly workaround for a shadow bug in Unity.
            // If we don't set a unique value to the property block we 
            // only get shadows in one of our draw calls. 
            irm.Lod00MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, materialPropertyCounter);
            irm.Lod01MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, materialPropertyCounter);
            irm.Lod02MatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, materialPropertyCounter);
            irm.ShadowMatPropBlock.SetFloat("_Whatever" + materialPropertyCounter++, materialPropertyCounter);
            // End of silly workaround!
            // ----------------------------------------------------------
            
            irm.Lod00MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.Lod01MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.Lod02MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.ShadowMatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            
            irm.Lod00MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
            irm.Lod01MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
            irm.Lod02MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
            irm.ShadowMatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
            
            irm.Lod00MatPropBlock.SetInt("_ArgsOffset", argsIndex + 4);
            irm.Lod01MatPropBlock.SetInt("_ArgsBuffer", argsIndex + 9);
            irm.Lod02MatPropBlock.SetInt("_ArgsBuffer", argsIndex + 14);
            irm.ShadowMatPropBlock.SetInt("_ArgsBuffer", argsIndex + 19);
        }
        
        // Create the buffer containing draw data for all instances
        m_00_CreateInstanceDrawBufferCSKernelID = m_00_CreateInstanceDrawBufferCS.FindKernel("CSMain");
        m_00_CreateInstanceDrawBufferCS.SetBuffer(m_00_CreateInstanceDrawBufferCSKernelID, "_InstanceDataIn", m_instanceDataBuffer);
        m_00_CreateInstanceDrawBufferCS.SetBuffer(m_00_CreateInstanceDrawBufferCSKernelID, "_InstanceDataOut", m_instanceDrawDataBuffer);
        int groupX = m_numberOfInstances / (2 * SCAN_THREAD_GROUP_SIZE);
        m_00_CreateInstanceDrawBufferCS.Dispatch(m_00_CreateInstanceDrawBufferCSKernelID, groupX, 1, 1);
        
        CreateCommandBuffers();
    }
    
    #endregion


    #region Debugging
    
    private void Log01Sorting()
    {
        if (!m_debugLog01)
        {
            return;
        }
        m_debugLog01 = false;
        
        StringBuilder sb = new StringBuilder();
        InstanceData[] distanceData = new InstanceData[m_numberOfInstances];
        m_instanceDataBuffer.GetData(distanceData);
        sb.AppendLine("01 distances:");
        for (int i = 0; i < distanceData.Length; i++)
        {
            if (i % 350 == 0)
            {
                Debug.Log(sb.ToString());
                sb = new StringBuilder();
            }
            //sb.AppendLine(i + ": " + distanceData[i].drawCallID + " => " + distanceData[i].distanceToCamera + " => " + distanceData[i].position);
        }
        Debug.Log(sb.ToString());
    }

    private void Log02Culling()
    {
        if (!m_debugLog02)
        {
            return;
        }
        m_debugLog02 = false;
        
        StringBuilder sb = new StringBuilder();
        uint[] isVisibleData = new uint[m_numberOfInstances];
        m_isVisibleBuffer.GetData(isVisibleData);
        sb.AppendLine("02 IsVisible:");
        for (int i = 0; i < isVisibleData.Length; i++)
        {
            sb.AppendLine(i + ": " + isVisibleData[i]);
        }
        Debug.Log(sb.ToString());
        
        sb = new StringBuilder();
        uint[] argsData = new uint[m_instances.Length * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
        m_argsBuffer.GetData(argsData);
        sb.AppendLine("02 argsData:");
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

    private void Log03ScanInstances()
    {
        if (!m_debugLog03)
        {
            return;
        }
        m_debugLog03 = false;
        
        StringBuilder sb = new StringBuilder();
        uint[] scannedData = new uint[m_numberOfInstances];
        m_scannedInstancePredicates.GetData(scannedData);
        sb.AppendLine("03 Scanned:");
        for (int i = 0; i < scannedData.Length; i++)
        {
            sb.AppendLine(i + ": " + scannedData[i]);
        }
        Debug.Log(sb.ToString());
        
        sb = new StringBuilder();
        uint[] groupSumData = new uint[m_numberOfInstances];
        m_groupSumArray.GetData(groupSumData);
        sb.AppendLine("03 GroupSum");
        for (int i = 0; i < groupSumData.Length; i++)
        {
            sb.AppendLine(i + ": " + groupSumData[i]);
        }
        Debug.Log(sb.ToString());
    }

    private void Log04ScanGroupSums()
    {
        if (!m_debugLog04)
        {
            return;
        }
        m_debugLog04 = false;
        
        StringBuilder sb = new StringBuilder();
        uint[] groupSumArrayOutData = new uint[m_numberOfInstances];
        m_scannedGroupSumBuffer.GetData(groupSumArrayOutData);
        sb.AppendLine("04 GroupSumArray:");
        for (int i = 0; i < groupSumArrayOutData.Length; i++)
        {
            sb.AppendLine(i + ": " + groupSumArrayOutData[i]);
        }
        Debug.Log(sb.ToString());
        
        
        sb = new StringBuilder();
        uint[] groupSumArrayOutDataShadows = new uint[m_numberOfInstances];
        m_scannedGroupSumBuffer.GetData(groupSumArrayOutDataShadows);
        sb.AppendLine("04 GroupSumArray Shadows:");
        for (int i = 0; i < groupSumArrayOutDataShadows.Length; i++)
        {
            sb.AppendLine(i + ": " + groupSumArrayOutDataShadows[i]);
        }
        Debug.Log(sb.ToString());
    }

    private void Log05CopyInstanceData()
    {
        if (!m_debugLog05)
        {
            return;
        }
        m_debugLog05 = false;
        
        // InstanceDrawData[] culledInstancesData = new InstanceDrawData[m_numberOfInstances];
        // m_culledInstanceBuffer.GetData(culledInstancesData);
        // StringBuilder sb = new StringBuilder();
        // sb.AppendLine("04 culledInstances (" + culledInstancesData.Length + ")");
        // for (int i = 0; i < culledInstancesData.Length; i++)
        // {
        //     if (i % 298 == 0)
        //     {
        //         Debug.Log(sb.ToString());
        //         sb = new StringBuilder();
        //     }
        //     sb.AppendLine(i + ": " + culledInstancesData[i].position + " " + culledInstancesData[i].rotation + ": " + culledInstancesData[i].uniformScale);
        // }
        // Debug.Log(sb.ToString());
        
        StringBuilder sb = new StringBuilder();
        uint[] argsData = new uint[m_instances.Length * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
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

    private void LogStats()
    {
        if (!m_debugLogStats)
        {
            return;
        }
        m_debugLogStats = false;
        
        StringBuilder sb = new StringBuilder();
        uint[] argsData = new uint[m_instances.Length * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
        m_argsBuffer.GetData(argsData);
        uint totalNumOfInstances = 0;
        uint totalNumOfIndices = 0;
        uint totalNumOfVertices = 0;
        uint totalShadowVertices = 0;
        uint totalShadowIndices = 0;

        sb.AppendLine("---------------");
        int counter = 0;
        for (int i = 0; i < argsData.Length; i = i + 20)
        {
            IndirectRenderingMesh irm = m_indirectMeshes[counter];
            if (i > 0)
            {
                sb.AppendLine();
            }

            uint numOfLod00Instances = argsData[i + 1];
            uint numOfLod01Instances = argsData[i + 6];
            uint numOfLod02Instances = argsData[i + 11];
            
            uint numOfLod00Indices = argsData[i + 0];
            uint numOfLod01Indices = argsData[i + 5];
            uint numOfLod02Indices = argsData[i + 10];
            
            uint numOfLod00Vertices = (uint)irm.numOfVerticesLod00;
            uint numOfLod01Vertices = (uint)irm.numOfVerticesLod01;
            uint numOfLod02Vertices = (uint)irm.numOfVerticesLod02;
            
            uint numOfInstances =
                numOfLod00Instances
                + numOfLod01Instances
                + numOfLod02Instances;
            uint numOfIndices =
                  numOfLod00Instances * numOfLod00Indices
                + numOfLod01Instances * numOfLod01Indices
                + numOfLod02Instances * numOfLod02Indices;
            uint numOfVertices =
                  numOfLod00Instances * numOfLod00Vertices
                + numOfLod01Instances * numOfLod01Vertices
                + numOfLod02Instances * numOfLod02Vertices;
            uint numOfShadowIndices = (m_enableSimpleShadows) ? numOfInstances * numOfLod02Indices : numOfIndices;
            uint numOfShadowVertices = (m_enableSimpleShadows) ? numOfInstances * numOfLod02Vertices : numOfVertices;
            
            totalNumOfInstances += numOfInstances;
            totalNumOfIndices += numOfIndices;
            totalNumOfVertices += numOfVertices;
            totalShadowIndices += numOfShadowIndices;
            totalShadowVertices += numOfShadowVertices;
            sb.AppendLine("Instances: " + numOfInstances.ToString("N0") + " ("
                        + numOfLod00Instances.ToString("N0") + ", "
                        + numOfLod01Instances.ToString("N0") + ", "
                        + numOfLod02Instances.ToString("N0") + ")"
            );
            sb.AppendLine("Vertices: " + numOfVertices.ToString("N0") + " ("
                        + numOfLod00Vertices.ToString("N0") + ", "
                        + numOfLod01Vertices.ToString("N0") + ", "
                        + numOfLod02Vertices.ToString("N0") + ")"
            );
            sb.AppendLine("Indices: " + numOfIndices.ToString("N0") + " ("
                        + numOfLod00Indices.ToString("N0") + ", "
                        + numOfLod01Indices.ToString("N0") + ", "
                        + numOfLod02Indices.ToString("N0") + ")"
            );
            sb.AppendLine("Shadow: " + numOfShadowVertices.ToString("N0") + " Vertices "
                        + numOfShadowIndices.ToString("N0") + " indices"
            );
            
            counter++;
        }
        
        string total = "Total Instances: " + totalNumOfInstances.ToString("N0") + "\n"
                    + "Total Vertices: " + totalNumOfVertices.ToString("N0") + "\n"
                    + "Total Indices: " + totalNumOfIndices.ToString("N0") + "\n"
                    + "Shadow Vertices: " + totalShadowIndices.ToString("N0") + "\n"
                    + "Shadow Indices: " + totalShadowVertices.ToString("N0") + "\n";
        Debug.Log(total + sb.ToString());
    }
    
    #endregion
}