using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

public partial class IndirectRenderer : MonoBehaviour
{
    #region Variables
    
    [Header("Settings")]
    [Space(10f)]
    [Range(0f, 500f)] public float m_shadowCullingLength = 250f;
    public ShadowCastingMode m_shadowCastingMode;

    [Header("References")]
    public ComputeShader m_computeShader;
    public HiZOcclusionBufferGenerate m_hiZBuffer;
    public Camera m_camera;
    public Light m_light;

    [Header("Debug")]
    [Space(10f)]
    public bool m_showHiZTexture = false;
    [SerializeField] [Range(0, 16)] private int m_hiZTextureLodLevel = 0;

    // Private Variables
    private bool m_shouldDraw = false;
    private Bounds m_drawBounds = new Bounds();
    private Vector3 m_camFarCenter = Vector3.zero;
    private List<IndirectInstanceData> m_instances = new List<IndirectInstanceData>();
    private List<IndirectRenderObject> m_renderers = new List<IndirectRenderObject>();

    #endregion

    #region MonoBehaviour

    private void Update()
    {
        if (m_shouldDraw == false
            || m_renderers == null
            || m_renderers.Count == 0
            || m_hiZBuffer.HiZDepthTexture == null)
        {
            return;
        }

        m_hiZBuffer.DebugModeEnabled = m_showHiZTexture;
        m_hiZBuffer.DebugLodLevel = m_hiZTextureLodLevel;

        DrawInstances();
    }

    private void OnDestroy()
    {
        m_shouldDraw = false;
        Clear();
    }

    #endregion

    #region Private Functions

    private void Initialize()
    {
        m_renderers.Clear();
        
        if (m_instances == null || m_instances.Count == 0)
        {
            return;
        }
        
        m_renderers = new List<IndirectRenderObject>(m_instances.Count);
        for (int i = 0; i < m_instances.Count; i++)
        {
            IndirectRenderObject renderer = new IndirectRenderObject();
            renderer.Initialize(m_computeShader, m_instances[i]);
            m_renderers.Add(renderer);
        }
    }

    private void DrawInstances()
    {
        // Update Compute();
        CalculateCameraFrustumPlanes();

        // Matrix4x4 M = m_camera.transform.localToWorldMatrix;
        Matrix4x4 V = m_camera.worldToCameraMatrix;
        Matrix4x4 P = m_camera.projectionMatrix;
        Matrix4x4 MVP = P * V;//*M;

        // Global data
        m_drawBounds.center = m_camera.transform.position;
        m_drawBounds.extents = Vector3.one * 10000;

        for (int i = 0; i < m_renderers.Count; i++)
        {
            m_renderers[i].Draw(m_hiZBuffer, MVP, m_camera.transform.position, m_light.transform.forward, m_shadowCullingLength, m_drawBounds, m_shadowCastingMode);
        }
    }

    private void Clear()
	{
        ClearBuffers();
        m_renderers.Clear();
        m_instances.Clear();
	}

    private void ClearBuffers()
    {
        for (int i = 0; i < m_renderers.Count; i++)
        {
            m_renderers[i].Clear();
        }
    }

    #endregion

    #region Public Functions

    public void AddInstances(List<IndirectInstanceData> _instances)
    {
        m_instances.AddRange(_instances);
    }

    public void ClearData()
    {
        Clear();
    }

    public void StartDrawing()
    {
        m_shouldDraw = true;
        Initialize();
    }

    public void StopDrawing()
    {
        m_shouldDraw = false;
    }

    #endregion
}