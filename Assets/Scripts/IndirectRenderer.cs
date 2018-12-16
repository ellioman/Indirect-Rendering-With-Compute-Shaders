using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// 16 * 4 bytes = 64 bytes
[System.Serializable]
public struct InstanceData
{
    public uint drawDataID;           // 1
    public uint drawCallID;           // 2
    public Vector3 position;          // 5
    public Vector3 rotation;          // 8
    public float uniformScale;        // 9
    public Vector3 boundsCenter;      // 12
    public Vector3 boundsExtents;     // 15
    public float distanceToCamera;    // 16
}

// 32 * 4 bytes = 128 bytes
public struct InstanceDrawData
{
    public Matrix4x4 unity_ObjectToWorld;    // 16
    public Matrix4x4 unity_WorldToObject;    // 32
};

[System.Serializable]
public class IndirectRenderingMesh
{
    public List<InstanceData> computeInstances = new List<InstanceData>();
    public Mesh mesh;
    public Mesh shadowMesh;
    public Material material;
    public MaterialPropertyBlock lod00MatPropBlock;
    public MaterialPropertyBlock lod01MatPropBlock;
    public MaterialPropertyBlock lod02MatPropBlock;
    public MaterialPropertyBlock shadowMatPropBlock;
    public uint numOfVerticesLod00;
    public uint numOfVerticesLod01;
    public uint numOfVerticesLod02;
    public uint numOfVerticesShadow;
    public uint numOfIndicesLod00;
    public uint numOfIndicesLod01;
    public uint numOfIndicesLod02;
    public uint numOfIndicesShadow;
}

public class IndirectRenderer : MonoBehaviour
{
    #region Variables
    
    [Header("Settings")]
    public bool isEnabled = true;
    public bool enableFrustumCulling = true;
    public bool enableOcclusionCulling = true;
    public bool enableDetailCulling = true;
    public bool enableLOD = true;
    public bool enableSimpleShadows = true;
    [Range(0f, 0.02f)] public float detailCullingScreenPercentage = 0.005f;
    public float sortCamDistance = 10f;
    
    [Header("References")]
    public ComputeShader createInstanceDrawBufferCS;
    public ComputeShader lodSortingCS;
    public ComputeShader occlusionCS;
    public ComputeShader scanInstancesCS;
    public ComputeShader scanGroupSumsCS;
    public ComputeShader copyInstanceDataCS;
    public HiZBuffer hiZBuffer;
    public Camera mainCamera;
    public Camera debugCamera;
    public Material shadowMatrixMaterial;

    // Debugging Variables
    [Header("Debug")]
    public bool debugDrawLOD;
    public bool debugDrawHiZ;
    [Range(0, 10)] public int debugHiZLOD;
    public bool debugLogStats;
    
    [Space(10f)]
    
    // Compute Buffers
    private ComputeBuffer m_argsBuffer;
    private ComputeBuffer m_instanceDataBuffer;
    private ComputeBuffer m_instanceDrawDataBuffer;
    private ComputeBuffer m_culledInstanceBuffer;
    private ComputeBuffer m_lodDistancesTempBuffer;
    private ComputeBuffer m_isVisibleBuffer;
    private ComputeBuffer m_groupSumArray;
    private ComputeBuffer m_scannedGroupSumBuffer;
    private ComputeBuffer m_scannedInstancePredicates;
    
    // Command Buffers
    private CommandBuffer m_sortingCommandBuffer;
    
    // Kernel ID's
    private int m_createInstanceDrawBufferCSKernelID;
    private int m_lodSortingCSKernelID;
    private int m_lodSortingTransposeCSKernelID;
    private int m_occlusionKernelID;
    private int m_scanInstancesKernelID;
    private int m_scanGroupSumsKernelID;
    private int m_copyInstanceDataKernelID;
    
    // Other
    private int m_numberOfInstanceTypes;
    private int m_numberOfInstances;
    private bool m_debugLastShowLOD;
    private uint[] m_args;
    private Bounds m_bounds;
    private Vector3 m_camPosition = Vector3.zero;
    private Vector3 m_lastCamPos = Vector3.zero;
    private Matrix4x4 m_MVP;
    private Transform m_transform;
    private RenderTexture m_shadowMatrixTexture;
    private IndirectInstanceData[] m_instances;
    private IndirectRenderingMesh[] m_indirectMeshes;
    private StringBuilder m_sb;
    
    // Constants
    private const int NUMBER_OF_ARGS_PER_DRAW = 5;
    private const int NUMBER_OF_DRAW_CALLS = 4; // (LOD00 + LOD01 + LOD02 + SHADOW)
    private const int NUMBER_OF_ARGS_PER_INSTANCE_TYPE = NUMBER_OF_ARGS_PER_DRAW * NUMBER_OF_DRAW_CALLS; // 20
    private const int ARGS_BYTE_SIZE_PER_DRAW_CALL = NUMBER_OF_ARGS_PER_DRAW * sizeof(uint); // 20
    private const int ARGS_BYTE_SIZE_PER_INSTANCE_TYPE = NUMBER_OF_ARGS_PER_INSTANCE_TYPE * sizeof(uint); // 80
    private const int SCAN_THREAD_GROUP_SIZE = 64;
    
    #endregion

    #region MonoBehaviour

    private void Awake()
    {
        m_transform = transform;
    }

    private void Update()
    {
        if (!isEnabled)
        {
            return;
        }
        
        hiZBuffer.DebugLodLevel = debugHiZLOD;

        if (m_debugLastShowLOD == debugDrawLOD)
        {
            return;
        }

        m_debugLastShowLOD = debugDrawLOD;
        for (int i = 0; i < m_indirectMeshes.Length; i++)
        {
            m_indirectMeshes[i].lod00MatPropBlock.SetColor("_Color", debugDrawLOD ? Color.red : Color.white);
            m_indirectMeshes[i].lod01MatPropBlock.SetColor("_Color", debugDrawLOD ? Color.green : Color.white);
            m_indirectMeshes[i].lod02MatPropBlock.SetColor("_Color", debugDrawLOD ? Color.blue : Color.white);
        }
    }

    private void OnPreCull()
    {
        if (!isEnabled
            || m_indirectMeshes == null
            || m_indirectMeshes.Length == 0
            || hiZBuffer.HiZDepthTexture == null
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
        if (m_sortingCommandBuffer != null          ) { m_sortingCommandBuffer.Release(); }
        if (m_instanceDataBuffer != null            ) { m_instanceDataBuffer.Release(); }
        if (m_culledInstanceBuffer != null	        ) { m_culledInstanceBuffer.Release(); }
        if (m_scannedGroupSumBuffer != null         ) { m_scannedGroupSumBuffer.Release(); }
        if (m_lodDistancesTempBuffer != null        ) { m_lodDistancesTempBuffer.Release(); }
        if (m_instanceDrawDataBuffer != null        ) { m_instanceDrawDataBuffer.Release(); }
        if (m_scannedInstancePredicates != null     ) { m_scannedInstancePredicates.Release(); }
    }
    
    // http://answers.unity.com/answers/477208/view.html
    public void OnDrawGizmos() 
    {
        if (mainCamera == null)
        {
            return;
        }
        
        Matrix4x4 temp = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(m_transform.position, m_transform.rotation, Vector3.one);
        if (mainCamera.orthographic)
        {
            float spread = mainCamera.farClipPlane - mainCamera.nearClipPlane;
            float center = (mainCamera.farClipPlane + mainCamera.nearClipPlane)*0.5f;
            Gizmos.DrawWireCube(new Vector3(0,0,center), new Vector3(mainCamera.orthographicSize*2*mainCamera.aspect, mainCamera.orthographicSize*2, spread));
        }
        else
        {
            Gizmos.DrawFrustum(Vector3.zero, mainCamera.fieldOfView, mainCamera.farClipPlane, mainCamera.nearClipPlane, mainCamera.aspect);
        }
        Gizmos.matrix = temp;
    }

    #endregion

    #region Private Functions
    
    private void DrawVisibleInstances()
    {
        if (enableSimpleShadows)
        {
            for (int i = 0; i < m_indirectMeshes.Length; i++)
            {
                int argsIndex = i * ARGS_BYTE_SIZE_PER_INSTANCE_TYPE;
                IndirectRenderingMesh irm = m_indirectMeshes[i];
                Graphics.DrawMeshInstancedIndirect(irm.mesh,       0, irm.material, m_bounds, m_argsBuffer, argsIndex + ARGS_BYTE_SIZE_PER_DRAW_CALL * 0, irm.lod00MatPropBlock,  ShadowCastingMode.Off);
                Graphics.DrawMeshInstancedIndirect(irm.mesh,       0, irm.material, m_bounds, m_argsBuffer, argsIndex + ARGS_BYTE_SIZE_PER_DRAW_CALL * 1, irm.lod01MatPropBlock,  ShadowCastingMode.Off);
                Graphics.DrawMeshInstancedIndirect(irm.mesh,       0, irm.material, m_bounds, m_argsBuffer, argsIndex + ARGS_BYTE_SIZE_PER_DRAW_CALL * 2, irm.lod02MatPropBlock,  ShadowCastingMode.Off);
                Graphics.DrawMeshInstancedIndirect(irm.shadowMesh, 0, irm.material, m_bounds, m_argsBuffer, argsIndex + ARGS_BYTE_SIZE_PER_DRAW_CALL * 3, irm.shadowMatPropBlock, ShadowCastingMode.ShadowsOnly);
            }
        }
        else
        {
            for (int i = 0; i < m_indirectMeshes.Length; i++)
            {
                int argsIndex = i * ARGS_BYTE_SIZE_PER_INSTANCE_TYPE;
                IndirectRenderingMesh irm = m_indirectMeshes[i];
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, argsIndex + ARGS_BYTE_SIZE_PER_DRAW_CALL * 0, irm.lod00MatPropBlock, ShadowCastingMode.On);
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, argsIndex + ARGS_BYTE_SIZE_PER_DRAW_CALL * 1, irm.lod01MatPropBlock, ShadowCastingMode.On);
                Graphics.DrawMeshInstancedIndirect(irm.mesh, 0, irm.material, m_bounds, m_argsBuffer, argsIndex + ARGS_BYTE_SIZE_PER_DRAW_CALL * 2, irm.lod02MatPropBlock, ShadowCastingMode.On);
            }
        }
        
        if (debugDrawHiZ)
        {
            debugCamera.Render();
        }
    }
    
    private void CalculateVisibleInstances()
    {
        // Global data
        m_camPosition = mainCamera.transform.position;
        
        //Matrix4x4 m = mainCamera.transform.localToWorldMatrix;
        Matrix4x4 v = mainCamera.worldToCameraMatrix;
        Matrix4x4 p = mainCamera.projectionMatrix;
        m_MVP = p * v;//*m;
        
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
            RunGpuSorting(ref m_camPosition);
        }
        Profiler.EndSample();
        
        //////////////////////////////////////////////////////
        // Set up compute shader to perform the occlusion culling
        //////////////////////////////////////////////////////
        Profiler.BeginSample("02 Occlusion");
        {
            // Input
            occlusionCS.SetInt("_ShouldFrustumCull", enableFrustumCulling ? 1 : 0);
            occlusionCS.SetInt("_ShouldOcclusionCull", enableOcclusionCulling ? 1 : 0);
            occlusionCS.SetInt("_ShouldDetailCull", enableDetailCulling ? 1 : 0);
            occlusionCS.SetInt("_ShouldLOD", enableLOD ? 1 : 0);
            
            occlusionCS.SetInt("_Cascades", QualitySettings.shadowCascades);
            occlusionCS.SetFloat("_DetailCullingScreenPercentage", detailCullingScreenPercentage);
            occlusionCS.SetMatrix("_UNITY_MATRIX_MVP", m_MVP);
            occlusionCS.SetVector("_HiZTextureSize", hiZBuffer.TextureSize);
            occlusionCS.SetVector("_CamPosition", m_camPosition);
            occlusionCS.SetBuffer(m_occlusionKernelID, "_InstanceDataBuffer", m_instanceDataBuffer);
            occlusionCS.SetTexture(m_occlusionKernelID, "_Unity_WorldToShadow", GetWorldToShadowMatrixTexture());
            occlusionCS.SetTexture(m_occlusionKernelID, "_HiZMap", hiZBuffer.HiZDepthTexture);
            
            // Output
            occlusionCS.SetBuffer(m_occlusionKernelID, "_ArgsBuffer", m_argsBuffer);
            occlusionCS.SetBuffer(m_occlusionKernelID, "_IsVisibleBuffer", m_isVisibleBuffer);
            
            // Dispatch
            int groupX = Mathf.Max(m_numberOfInstances / 64, 1);
            occlusionCS.Dispatch(m_occlusionKernelID, groupX, 1, 1);
        }
        Profiler.EndSample();
        
        //////////////////////////////////////////////////////
        // Perform scan of instance predicates
        //////////////////////////////////////////////////////
        Profiler.BeginSample("03 Scan Instances");
        {
            int groupX = m_numberOfInstances / (2 * SCAN_THREAD_GROUP_SIZE);
            
            // Input
            scanInstancesCS.SetBuffer(m_scanInstancesKernelID, "_InstancePredicatesIn", m_isVisibleBuffer);
            
            // Output
            scanInstancesCS.SetBuffer(m_scanInstancesKernelID, "_GroupSumArray", m_groupSumArray);
            scanInstancesCS.SetBuffer(m_scanInstancesKernelID, "_ScannedInstancePredicates", m_scannedInstancePredicates);
            
            // Dispatch
            scanInstancesCS.Dispatch(m_scanInstancesKernelID, groupX, 1, 1);
        }
        Profiler.EndSample();
        
        //////////////////////////////////////////////////////
        // Perform scan of group sums
        //////////////////////////////////////////////////////
        Profiler.BeginSample("04 Scan Thread Groups");
        {
            // Input
            int numOfGroups = m_numberOfInstances / (2 * SCAN_THREAD_GROUP_SIZE);
            scanGroupSumsCS.SetInt("_NumOfGroups", numOfGroups);
            scanGroupSumsCS.SetBuffer(m_scanGroupSumsKernelID, "_GroupSumArrayIn", m_groupSumArray);
            
            // Output
            scanGroupSumsCS.SetBuffer(m_scanGroupSumsKernelID, "_GroupSumArrayOut", m_scannedGroupSumBuffer);
            
            // Dispatch
            scanGroupSumsCS.Dispatch(m_scanGroupSumsKernelID, 1, 1, 1);
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
            copyInstanceDataCS.SetInt("_NumberOfInstanceTypes", m_numberOfInstanceTypes * NUMBER_OF_DRAW_CALLS);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, "_InstanceData", m_instanceDataBuffer);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, "_InstanceDrawData", m_instanceDrawDataBuffer);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, "_InstancePredicatesIn", m_isVisibleBuffer);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, "_GroupSumArray", m_scannedGroupSumBuffer);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, "_ScannedInstancePredicates", m_scannedInstancePredicates);
            
            // Output
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, "_DrawcallDataOut", m_argsBuffer);
            copyInstanceDataCS.SetBuffer(m_copyInstanceDataKernelID, "_InstanceDataOut", m_culledInstanceBuffer);
            
            // Dispatch
            copyInstanceDataCS.Dispatch(m_copyInstanceDataKernelID, groupX, 1, 1);
        }
        Profiler.EndSample();
        
        LogStats();
    }
    
    private RenderTexture GetWorldToShadowMatrixTexture()
    {
        if (m_shadowMatrixTexture == null)
        {
            m_shadowMatrixTexture = new RenderTexture(width:7, height:1, depth:0, format: RenderTextureFormat.ARGBFloat)
            {
                filterMode = FilterMode.Point
            };
        }
        shadowMatrixMaterial.SetFloat("_Cascades", QualitySettings.shadowCascades);
        Graphics.Blit(null, m_shadowMatrixTexture, shadowMatrixMaterial);
        return m_shadowMatrixTexture;
    }

    private void RunGpuSorting(ref Vector3 _cameraPosition)
    {
        if (!(Vector3.Distance(m_lastCamPos, _cameraPosition) > sortCamDistance))
        {
            return;
        }

        m_lastCamPos = _cameraPosition;
        Graphics.ExecuteCommandBufferAsync(m_sortingCommandBuffer, ComputeQueueType.Background);
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

        m_sortingCommandBuffer = new CommandBuffer {name = "AsyncGPUSorting"};

        // Sort the data
        // First sort the rows for the levels <= to the block size
        for (uint level = 2; level <= BITONIC_BLOCK_SIZE; level <<= 1)
        {
            SetGPUSortConstants(ref m_sortingCommandBuffer, ref lodSortingCS, ref level, ref level, ref MATRIX_HEIGHT, ref MATRIX_WIDTH);
            
            // Sort the row data
            m_sortingCommandBuffer.SetComputeBufferParam(lodSortingCS, m_lodSortingCSKernelID, "Data", m_instanceDataBuffer);
            m_sortingCommandBuffer.DispatchCompute(lodSortingCS, m_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
        }
        
        // Then sort the rows and columns for the levels > than the block size
        // Transpose. Sort the Columns. Transpose. Sort the Rows.
        for (uint level = (BITONIC_BLOCK_SIZE << 1); level <= NUM_ELEMENTS; level <<= 1)
        {
            // Transpose the data from buffer 1 into buffer 2
            uint l = (level / BITONIC_BLOCK_SIZE);
            uint lm = (level & ~NUM_ELEMENTS) / BITONIC_BLOCK_SIZE;
            SetGPUSortConstants(ref m_sortingCommandBuffer, ref lodSortingCS, ref l, ref lm, ref MATRIX_WIDTH, ref MATRIX_HEIGHT);
            m_sortingCommandBuffer.SetComputeBufferParam(lodSortingCS, m_lodSortingTransposeCSKernelID, "Input", m_instanceDataBuffer);
            m_sortingCommandBuffer.SetComputeBufferParam(lodSortingCS, m_lodSortingTransposeCSKernelID, "Data", m_lodDistancesTempBuffer);
            m_sortingCommandBuffer.DispatchCompute(lodSortingCS, m_lodSortingTransposeCSKernelID, (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), 1);
            
            // Sort the transposed column data
            m_sortingCommandBuffer.SetComputeBufferParam(lodSortingCS, m_lodSortingCSKernelID, "Data", m_lodDistancesTempBuffer);
            m_sortingCommandBuffer.DispatchCompute(lodSortingCS, m_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
            
            // Transpose the data from buffer 2 back into buffer 1
            SetGPUSortConstants(ref m_sortingCommandBuffer, ref lodSortingCS, ref BITONIC_BLOCK_SIZE, ref level, ref MATRIX_HEIGHT, ref MATRIX_WIDTH);
            m_sortingCommandBuffer.SetComputeBufferParam(lodSortingCS, m_lodSortingTransposeCSKernelID, "Input", m_lodDistancesTempBuffer);
            m_sortingCommandBuffer.SetComputeBufferParam(lodSortingCS, m_lodSortingTransposeCSKernelID, "Data", m_instanceDataBuffer);
            m_sortingCommandBuffer.DispatchCompute(lodSortingCS, m_lodSortingTransposeCSKernelID, (int)(MATRIX_HEIGHT / TRANSPOSE_BLOCK_SIZE), (int)(MATRIX_WIDTH / TRANSPOSE_BLOCK_SIZE), 1);
            
            // Sort the row data
            m_sortingCommandBuffer.SetComputeBufferParam(lodSortingCS, m_lodSortingCSKernelID, "Data", m_instanceDataBuffer);
            m_sortingCommandBuffer.DispatchCompute(lodSortingCS, m_lodSortingCSKernelID, (int)(NUM_ELEMENTS / BITONIC_BLOCK_SIZE), 1, 1);
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
        
        m_lodSortingCSKernelID = lodSortingCS.FindKernel("BitonicSort");
        m_lodSortingTransposeCSKernelID = lodSortingCS.FindKernel("MatrixTranspose");
        m_occlusionKernelID = occlusionCS.FindKernel("CSMain");
        m_scanInstancesKernelID = scanInstancesCS.FindKernel("CSMain");
        m_scanGroupSumsKernelID = scanGroupSumsCS.FindKernel("CSMain");
        m_copyInstanceDataKernelID = copyInstanceDataCS.FindKernel("CSMain");
        
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
                
                newData.drawDataID = (uint)(instanceCounter);
                newData.drawCallID = (uint)argsIndex;
                newData.position = _data.positions[j];
                newData.rotation = _data.rotations[j];
                newData.uniformScale = _data.uniformScales[j];
                newData.boundsCenter = b.center;//_data.positions[j];
                newData.boundsExtents = b.extents * _data.uniformScales[j];
                newData.distanceToCamera = Vector3.Distance(_data.positions[j], mainCamera.transform.position);
                irm.computeInstances.Add(newData);
                allInstancesPositionsList.Add(newData);
                
                instanceCounter++;
            }
            
            // Add the data to the renderer list
            m_indirectMeshes[i] = irm;
        }
        
        InstanceData[] instancesPositionsArray = allInstancesPositionsList.ToArray();
        m_numberOfInstances = allInstancesPositionsList.Count;
        
        int computeShaderInputSize = Marshal.SizeOf(typeof(InstanceData));
        int computeShaderOutputSize = Marshal.SizeOf(typeof(InstanceDrawData));

        m_argsBuffer = new ComputeBuffer(m_numberOfInstanceTypes * 20, sizeof(uint), ComputeBufferType.IndirectArguments);
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
            
            irm.lod00MatPropBlock = new MaterialPropertyBlock();
            irm.lod01MatPropBlock = new MaterialPropertyBlock();
            irm.lod02MatPropBlock = new MaterialPropertyBlock();
            irm.shadowMatPropBlock = new MaterialPropertyBlock();
            
            // ----------------------------------------------------------
            // Silly workaround for a shadow bug in Unity.
            // If we don't set a unique value to the property block we 
            // only get shadows in one of our draw calls. 
            irm.lod00MatPropBlock.SetFloat("_Whatever", + materialPropertyCounter++);
            irm.lod01MatPropBlock.SetFloat("_Whatever", + materialPropertyCounter++);
            irm.lod02MatPropBlock.SetFloat("_Whatever", + materialPropertyCounter++);
            irm.shadowMatPropBlock.SetFloat("_Whatever", + materialPropertyCounter++);
            // End of silly workaround!
            // ----------------------------------------------------------
            
            irm.lod00MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.lod01MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.lod02MatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            irm.shadowMatPropBlock.SetBuffer("_InstanceDrawDataBuffer", m_culledInstanceBuffer);
            
            irm.lod00MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
            irm.lod01MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
            irm.lod02MatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
            irm.shadowMatPropBlock.SetBuffer("_ArgsBuffer", m_argsBuffer);
            
            irm.lod00MatPropBlock.SetInt("_ArgsOffset", argsIndex + 4);
            irm.lod01MatPropBlock.SetInt("_ArgsOffset", argsIndex + 9);
            irm.lod02MatPropBlock.SetInt("_ArgsOffset", argsIndex + 14);
            irm.shadowMatPropBlock.SetInt("_ArgsOffset", argsIndex + 19);
        }
        
        // Create the buffer containing draw data for all instances
        m_createInstanceDrawBufferCSKernelID = createInstanceDrawBufferCS.FindKernel("CSMain");
        createInstanceDrawBufferCS.SetBuffer(m_createInstanceDrawBufferCSKernelID, "_InstanceDataIn", m_instanceDataBuffer);
        createInstanceDrawBufferCS.SetBuffer(m_createInstanceDrawBufferCSKernelID, "_InstanceDataOut", m_instanceDrawDataBuffer);
        int groupX = m_numberOfInstances / (2 * SCAN_THREAD_GROUP_SIZE);
        createInstanceDrawBufferCS.Dispatch(m_createInstanceDrawBufferCSKernelID, groupX, 1, 1);
        
        CreateCommandBuffers();
    }
    
    #endregion


    #region Debugging
    
    private void LogStats()
    {
        if (!debugLogStats)
        {
            return;
        }
        debugLogStats = false;

        if (m_sb == null)
        {
            m_sb = new StringBuilder();
        }
        
        uint[] argsData = new uint[m_instances.Length * NUMBER_OF_ARGS_PER_INSTANCE_TYPE];
        m_argsBuffer.GetData(argsData);
        uint totalNumOfInstances = 0;
        uint totalNumOfIndices = 0;
        uint totalNumOfVertices = 0;
        uint totalShadowVertices = 0;
        uint totalShadowIndices = 0;

        m_sb.AppendLine("---------------");
        int counter = 0;
        for (int i = 0; i < argsData.Length; i = i + 20)
        {
            IndirectRenderingMesh irm = m_indirectMeshes[counter];
            if (i > 0)
            {
                m_sb.AppendLine();
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
            uint numOfShadowIndices = (enableSimpleShadows) ? numOfInstances * numOfLod02Indices : numOfIndices;
            uint numOfShadowVertices = (enableSimpleShadows) ? numOfInstances * numOfLod02Vertices : numOfVertices;
            
            totalNumOfInstances += numOfInstances;
            totalNumOfIndices += numOfIndices;
            totalNumOfVertices += numOfVertices;
            totalShadowIndices += numOfShadowIndices;
            totalShadowVertices += numOfShadowVertices;
            
            m_sb.AppendLine("Instances: " + numOfInstances.ToString("N0") + " ("
                        + numOfLod00Instances.ToString("N0") + ", "
                        + numOfLod01Instances.ToString("N0") + ", "
                        + numOfLod02Instances.ToString("N0") + ")"
            );
            m_sb.AppendLine("Vertices: " + numOfVertices.ToString("N0") + " ("
                        + numOfLod00Vertices.ToString("N0") + ", "
                        + numOfLod01Vertices.ToString("N0") + ", "
                        + numOfLod02Vertices.ToString("N0") + ")"
            );
            m_sb.AppendLine("Indices: " + numOfIndices.ToString("N0") + " ("
                        + numOfLod00Indices.ToString("N0") + ", "
                        + numOfLod01Indices.ToString("N0") + ", "
                        + numOfLod02Indices.ToString("N0") + ")"
            );
            m_sb.AppendLine("Shadow: " + numOfShadowVertices.ToString("N0") + " Vertices "
                        + numOfShadowIndices.ToString("N0") + " indices"
            );
            
            counter++;
        }

        StringBuilder total = new StringBuilder();
        total.Append("Total Instances: ");
        total.AppendLine(totalNumOfInstances.ToString("N0"));
        total.Append("Total Vertices: ");
        total.AppendLine(totalNumOfVertices.ToString("N0"));
        total.Append("Total Indices: ");
        total.AppendLine(totalNumOfIndices.ToString("N0"));
        total.Append("Shadow Vertices: ");
        total.AppendLine(totalShadowIndices.ToString("N0"));
        total.Append("Shadow Indices: ");
        total.AppendLine(totalShadowVertices.ToString("N0"));
        
        Debug.Log(total.ToString());
        Debug.Log(m_sb.ToString());
    }
    
    #endregion
}