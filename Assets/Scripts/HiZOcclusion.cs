using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;
using Random = UnityEngine.Random;


public class HiZOcclusion : MonoBehaviour
{
    [Header("TODO PROBLEMS")]
    public float mipLevelAddon;
    
    [Header("Settings")]
    [SerializeField] private int m_numOfInstances = 1024;
    [Space(10f)]
    [SerializeField] private bool m_shouldFrustumCull = true;
    [SerializeField] private bool m_shouldHiZCull = true;
    [SerializeField] private bool m_shouldLOD = true;
    [Space(10f)]
    [SerializeField] private float m_lod00Distance = 150;
    [SerializeField] private float m_lod01Distance = 250;
    [SerializeField] private float m_lod02Distance = 1500;
    [SerializeField] private Vector2 m_objectScale = new Vector2(1f, 10f);
    
    [Header("Debug")]
    [SerializeField] private bool m_shouldDisplayGUI = false;
    [SerializeField] private bool m_showHiZTexture = false;
    [SerializeField] [Range(0, 16)] private int m_hiZTextureLodLevel = 0;
    [SerializeField] private bool m_shouldDrawBoundingBoxes = false;
    
    [Header("References")]
    [SerializeField] private ComputeShader m_computeShader;
    [SerializeField] private HiZOcclusionBufferGenerate m_hiZBuffer;
    [SerializeField] private Camera m_camera;
    [SerializeField] private Material m_objectMaterial;
    [SerializeField] private Mesh m_lod00Mesh;
    [SerializeField] private Mesh m_lod01Mesh;
    [SerializeField] private Mesh m_lod02Mesh;

    // Constants
    private Bounds SPAWN_BOUNDS = new Bounds(new Vector3(0f, 0f, 175f), new Vector3(500f, 2.5f, 500f));

    // Private Variables
    private int KernelId;
    private Vector4 m_boundingBoxCenter = Vector4.zero;
    private Vector4 m_boundingBoxExtents = Vector4.zero;
    private Vector4[] positions = null;
    private Material m_lod00Material = null;
    private Material m_lod01Material = null;
    private Material m_lod02Material = null;
    private GUIStyle m_guiStyle = new GUIStyle();
    private Transform m_cachedMainCameraTransform;
    private ComputeBuffer m_lod00Buffer = null;
    private ComputeBuffer m_lod01Buffer = null;
    private ComputeBuffer m_lod02Buffer = null;
    private ComputeBuffer m_lod00ArgsBuffer = null;
    private ComputeBuffer m_lod01ArgsBuffer = null;
    private ComputeBuffer m_lod02ArgsBuffer = null;
    private ComputeBuffer m_positionsBuffer = null;
    private ComputeBuffer m_colorBuffer = null;

    // Buffer with arguments, bufferWithArgs, has to have five integer numbers at given argsOffset offset
    // 0 - index count per instance, 
    // 1 - instance count,
    // 2 - start index location, 
    // 3 - base vertex location
    // 4 - start instance location.
    private uint[] m_lod00Args = new uint[5] { 0, 0, 0, 0, 0 };
    private uint[] m_lod01Args = new uint[5] { 0, 0, 0, 0, 0 };
    private uint[] m_lod02Args = new uint[5] { 0, 0, 0, 0, 0 };

    
    private void Start()
    {
        
        m_numOfInstances = Mathf.ClosestPowerOfTwo(m_numOfInstances);
        Initialize();
    }

    private void LateUpdate()
    {
        if (m_hiZBuffer.HiZDepthTexture == null)
        {
            return;
        }
        
        m_hiZBuffer.DebugModeEnabled = m_showHiZTexture;
        m_hiZBuffer.DebugLodLevel = m_hiZTextureLodLevel;
        
        UpdateCompute();
    }

    private void OnDrawGizmos()
    {
        if (!m_shouldDrawBoundingBoxes) return;
        
        for (int i = 0; i < positions.Length; i++)
        {
            Gizmos.DrawWireCube(positions[i] + m_boundingBoxCenter, (m_boundingBoxExtents * 2.0f) * (positions[i].w));
        }
    }

    private void UpdateCompute()
    {
        m_computeShader.SetFloat("_MipLevelAddon", mipLevelAddon);
        
        // Compute
        m_computeShader.SetBool("_ShouldFrustumCull", m_shouldFrustumCull);
        m_computeShader.SetBool("_ShouldHiZCull", m_shouldHiZCull);
        m_computeShader.SetBool("_ShouldLOD", m_shouldLOD);

        m_computeShader.SetVector("_BoundsCenter", m_boundingBoxCenter);
        m_computeShader.SetVector("_BoundsExtents", m_boundingBoxExtents);
        m_computeShader.SetVector("_TexSize", m_hiZBuffer.TextureSize);

        m_computeShader.SetFloat("_LOD00Distance", m_lod00Distance);
        m_computeShader.SetFloat("_LOD01Distance", m_lod01Distance);
        m_computeShader.SetFloat("_LOD02Distance", m_lod02Distance);
        m_computeShader.SetMatrix("_UNITY_MATRIX_MVP", m_camera.projectionMatrix * m_camera.worldToCameraMatrix);
        m_computeShader.SetVector("_CamPos", m_cachedMainCameraTransform.position);
        m_computeShader.SetTexture(KernelId, "_HiZMap", m_hiZBuffer.HiZDepthTexture);

        // Dispatch
        m_lod00Buffer.SetCounterValue(0);
        m_lod01Buffer.SetCounterValue(0);
        m_lod02Buffer.SetCounterValue(0);
        m_computeShader.Dispatch(KernelId, m_numOfInstances, 1, 1);

        ComputeBuffer.CopyCount(m_lod00Buffer, m_lod00ArgsBuffer, 1 * sizeof(uint));
        ComputeBuffer.CopyCount(m_lod01Buffer, m_lod01ArgsBuffer, 1 * sizeof(uint));
        ComputeBuffer.CopyCount(m_lod02Buffer, m_lod02ArgsBuffer, 1 * sizeof(uint));
        
        if (m_shouldDisplayGUI)
        {
            m_lod00ArgsBuffer.GetData(m_lod00Args);
            m_lod01ArgsBuffer.GetData(m_lod01Args);
            m_lod02ArgsBuffer.GetData(m_lod02Args);
        }

        Graphics.DrawMeshInstancedIndirect(m_lod00Mesh, 0, m_lod00Material, new Bounds(Vector3.zero, Vector3.one * 1000f), m_lod00ArgsBuffer, 0, null, ShadowCastingMode.On, true);
        Graphics.DrawMeshInstancedIndirect(m_lod01Mesh, 0, m_lod01Material, new Bounds(Vector3.zero, Vector3.one * 1000f), m_lod01ArgsBuffer, 0, null, ShadowCastingMode.On, true);
        Graphics.DrawMeshInstancedIndirect(m_lod02Mesh, 0, m_lod02Material, new Bounds(Vector3.zero, Vector3.one * 1000f), m_lod02ArgsBuffer, 0, null, ShadowCastingMode.On, true);
    }

    private void OnGUI()
    {
        if (m_shouldDisplayGUI == false)
        {
            return;
        }

        m_guiStyle.fontSize = 35;
        GUI.skin.label.fontSize = 30;
        GUI.color = Color.blue;
        GUI.Label(new Rect(5,  5, 400, 40), "LOD00: " + m_lod00Args[1].ToString("N0") + " Objects");
        GUI.Label(new Rect(5, 45, 400, 40), "LOD01: " + m_lod01Args[1].ToString("N0") + " Objects");
        GUI.Label(new Rect(5, 85, 400, 40), "LOD02: " + m_lod02Args[1].ToString("N0") + " Objects");
    }

    private void OnDisable()
    {
        Clear();
    }

    private void Initialize()
	{
        Clear();

        m_cachedMainCameraTransform = m_camera.transform;

        InitComputeShader();
    }

    private void InitComputeShader()
    {
        m_boundingBoxExtents = m_lod00Mesh.bounds.extents;
        m_boundingBoxCenter = new Vector4(m_lod00Mesh.bounds.center.x, m_lod00Mesh.bounds.center.y, m_lod00Mesh.bounds.center.z, 0.0f);

        // Original position of every instance...
        m_positionsBuffer = new ComputeBuffer(m_numOfInstances, 4 * sizeof(float));
        m_colorBuffer     = new ComputeBuffer(m_numOfInstances, 4 * sizeof(float));
        m_lod00Buffer = new ComputeBuffer(m_numOfInstances, 4 * sizeof(float), ComputeBufferType.Append);
        m_lod01Buffer = new ComputeBuffer(m_numOfInstances, 4 * sizeof(float), ComputeBufferType.Append);
        m_lod02Buffer = new ComputeBuffer(m_numOfInstances, 4 * sizeof(float), ComputeBufferType.Append);

        positions = CreatePositions();
        m_positionsBuffer.SetData(positions);
        m_colorBuffer.SetData(CreateColors());
        m_lod00Buffer.SetCounterValue(0);
        m_lod01Buffer.SetCounterValue(0);
        m_lod02Buffer.SetCounterValue(0);

        KernelId = m_computeShader.FindKernel("HiZExample");
        
        m_computeShader.SetBuffer(KernelId, "lod0Buffer", m_lod00Buffer);
        m_computeShader.SetBuffer(KernelId, "lod1Buffer", m_lod01Buffer);
        m_computeShader.SetBuffer(KernelId, "lod2Buffer", m_lod02Buffer);
        m_computeShader.SetBuffer(KernelId, "positionBuffer", m_positionsBuffer);
        m_computeShader.SetBuffer(KernelId, "colorBuffer", m_colorBuffer);

        m_lod00Material = new Material(m_objectMaterial);
        m_lod01Material = new Material(m_objectMaterial);
        m_lod02Material = new Material(m_objectMaterial);
        m_lod00Material.color = new Color(1.0f, 0.35f, 0.35f);
        m_lod01Material.color = new Color(0.35f, 1.0f, 0.35f);
        m_lod02Material.color = new Color(0.35f, 0.35f, 1.0f);
        
        m_lod00Material.SetBuffer("colorBuffer", m_colorBuffer);
        m_lod01Material.SetBuffer("colorBuffer", m_colorBuffer);
        m_lod02Material.SetBuffer("colorBuffer", m_colorBuffer);
        m_lod00Material.SetBuffer("positionBuffer", m_lod00Buffer);
        m_lod01Material.SetBuffer("positionBuffer", m_lod01Buffer);
        m_lod02Material.SetBuffer("positionBuffer", m_lod02Buffer);

        m_lod00ArgsBuffer = new ComputeBuffer(5, m_lod00Args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        m_lod00Args[0] = (uint) m_lod00Mesh.GetIndexCount(0);   // - index count per instance, 
        m_lod00Args[1] = (uint) m_numOfInstances;               // - instance count,
        m_lod00Args[2] = (uint) 0;                              // - start index location, 
        m_lod00Args[3] = (uint) 0;                              // - base vertex location
        m_lod00Args[4] = (uint) 0;                              // - start instance location.
        m_lod00ArgsBuffer.SetData(m_lod00Args);

        m_lod01ArgsBuffer = new ComputeBuffer(5, m_lod01Args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        m_lod01Args[0] = (uint) m_lod01Mesh.GetIndexCount(0);   // - index count per instance, 
        m_lod01Args[1] = (uint) m_numOfInstances;               // - instance count,
        m_lod01Args[2] = (uint) 0;                              // - start index location, 
        m_lod01Args[3] = (uint) 0;                              // - base vertex location
        m_lod01Args[4] = (uint) 0;                              // - start instance location.
        m_lod01ArgsBuffer.SetData(m_lod01Args);

        m_lod02ArgsBuffer = new ComputeBuffer(5, m_lod02Args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        m_lod02Args [0] = (uint)m_lod02Mesh.GetIndexCount(0);   // - index count per instance, 
        m_lod02Args[1] = (uint) m_numOfInstances;               // - instance count,
        m_lod02Args[2] = (uint) 0;                              // - start index location, 
        m_lod02Args[3] = (uint) 0;                              // - base vertex location
        m_lod02Args[4] = (uint) 0;                              // - start instance location.
        m_lod02ArgsBuffer.SetData(m_lod02Args);
    }

    private void Clear()
    {
        if (m_lod00Buffer != null) { m_lod00Buffer.Release(); }
        if (m_lod01Buffer != null) { m_lod01Buffer.Release(); }
        if (m_lod02Buffer != null) { m_lod02Buffer.Release(); }
        if (m_lod00ArgsBuffer != null) { m_lod00ArgsBuffer.Release(); }
        if (m_lod01ArgsBuffer != null) { m_lod01ArgsBuffer.Release(); }
        if (m_lod02ArgsBuffer != null) { m_lod02ArgsBuffer.Release(); }
        if (m_positionsBuffer != null) { m_positionsBuffer.Release(); }

        m_lod00Buffer = null;
        m_lod01Buffer = null;
        m_lod02Buffer = null;
        m_lod00ArgsBuffer = null;
        m_lod01ArgsBuffer = null;
        m_lod02ArgsBuffer = null;
        m_positionsBuffer = null;
    }

    private Vector4[] CreatePositions()
    {
        Vector4[] posArray = new Vector4[m_numOfInstances];
        int index = 0;
        
        while (index < m_numOfInstances)
        {
            posArray [index] = new Vector4
            (
                Random.Range(SPAWN_BOUNDS.center.x + SPAWN_BOUNDS.min.x, SPAWN_BOUNDS.center.x + SPAWN_BOUNDS.max.x),
                Random.Range(SPAWN_BOUNDS.center.y + SPAWN_BOUNDS.min.y, SPAWN_BOUNDS.center.y + SPAWN_BOUNDS.max.y), 
                Random.Range(SPAWN_BOUNDS.center.z + SPAWN_BOUNDS.min.z, SPAWN_BOUNDS.center.z + SPAWN_BOUNDS.max.z),
                Random.Range(m_objectScale.x, m_objectScale.y)
            );
            
            index++;
        }
        return posArray;
    }
    
    private Vector4[] CreateColors()
    {
        Vector4[] colorArray = new Vector4[m_numOfInstances];
        int index = 0;
        while (index < m_numOfInstances)
        {
            colorArray[index] = Color.white;
            index++;
        }
        return colorArray;
    }
}