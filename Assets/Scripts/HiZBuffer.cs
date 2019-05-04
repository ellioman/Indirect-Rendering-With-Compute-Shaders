// https://github.com/gokselgoktas/hi-z-buffer

using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[RequireComponent(typeof(Camera))]
public class HiZBuffer : MonoBehaviour
{
    #region Variables

    [Header("References")]
    public RenderTexture topDownView = null;
    public IndirectRenderer m_indirectRenderer;
    public Camera mainCamera = null;
    public Light light = null;
    public Shader generateBufferShader = null;
    public Shader debugShader = null;

    // Private 
    private int m_LODCount = 0;
    private int[] m_Temporaries = null;
    private CameraEvent m_CameraEvent = CameraEvent.AfterReflections;
    private Vector2 m_textureSize;
    private Material m_generateBufferMaterial = null;
    private Material m_debugMaterial = null;
    private RenderTexture m_HiZDepthTexture = null;
    private CommandBuffer m_CommandBuffer = null;
    private CameraEvent m_lastCameraEvent = CameraEvent.AfterReflections;
    private RenderTexture m_ShadowmapCopy;
    private RenderTargetIdentifier m_shadowmap;
    private CommandBuffer m_lightShadowCommandBuffer;
    
    // Public Properties
    public int DebugLodLevel { get; set; }
    public Vector2 TextureSize { get { return m_textureSize; } }
    public RenderTexture Texture { get { return m_HiZDepthTexture; } }

    // Consts
    private const int MAXIMUM_BUFFER_SIZE = 1024;

    // Enums
    private enum Pass
    {
        Blit,
        Reduce
    }

    #endregion

    #region MonoBehaviour

    private void Awake()
    {
        m_generateBufferMaterial = new Material(generateBufferShader);
        m_debugMaterial = new Material(debugShader);
        mainCamera.depthTextureMode = DepthTextureMode.Depth;
    }

    public void Start()
    {
        // m_shadowmap = BuiltinRenderTextureType.CurrentActive;
        // m_ShadowmapCopy = new RenderTexture(1024, 1024, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
        
        // m_lightShadowCommandBuffer = new CommandBuffer();
        
        // // Change shadow sampling mode for m_Light's shadowmap.
        // m_lightShadowCommandBuffer.SetShadowSamplingMode(m_shadowmap, ShadowSamplingMode.RawDepth);
        
        // // The shadowmap values can now be sampled normally - copy it to a different render texture.
        // m_lightShadowCommandBuffer.Blit(m_shadowmap, new RenderTargetIdentifier(m_ShadowmapCopy));
        
        // // Execute after the shadowmap has been filled.
        // light.AddCommandBuffer(LightEvent.AfterShadowMapPass, m_lightShadowCommandBuffer);
        
        // Sampling mode is restored automatically after this command buffer completes, so shadows will render normally.
    }

    private void OnDisable()
    {
        if (mainCamera != null)
        {
            if (m_CommandBuffer != null)
            {
                mainCamera.RemoveCommandBuffer(m_CameraEvent, m_CommandBuffer);
                m_CommandBuffer = null;
            }
        }
        
        if (m_HiZDepthTexture != null)
        {
            m_HiZDepthTexture.Release();
            m_HiZDepthTexture = null;
        }
    }
    
    public void InitializeTexture()
    {
        if (m_HiZDepthTexture != null)
        {
            m_HiZDepthTexture.Release();
        }
        
        int size = (int)Mathf.Max((float)mainCamera.pixelWidth, (float)mainCamera.pixelHeight);
        size = (int)Mathf.Min((float)Mathf.NextPowerOfTwo(size), (float)MAXIMUM_BUFFER_SIZE);
        m_textureSize.x = size;
        m_textureSize.y = size;
        
        m_HiZDepthTexture = new RenderTexture(size, size, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
        m_HiZDepthTexture.filterMode = FilterMode.Point;
        m_HiZDepthTexture.useMipMap = true;
        m_HiZDepthTexture.autoGenerateMips = false;
        m_HiZDepthTexture.Create();
        m_HiZDepthTexture.hideFlags = HideFlags.HideAndDontSave;
    }

    private void OnPreRender()
    {
        int size = (int)Mathf.Max((float)mainCamera.pixelWidth, (float)mainCamera.pixelHeight);
        size = (int)Mathf.Min((float)Mathf.NextPowerOfTwo(size), (float)MAXIMUM_BUFFER_SIZE);
        m_textureSize.x = size;
        m_textureSize.y = size;
        m_LODCount = (int)Mathf.Floor(Mathf.Log(size, 2f));
        
        if (m_LODCount == 0)
        {
            return;
        }
        
        bool isCommandBufferInvalid = false;
        if (m_HiZDepthTexture == null
            || (m_HiZDepthTexture.width != size
            || m_HiZDepthTexture.height != size)
            || m_lastCameraEvent != m_CameraEvent
            )
        {
            InitializeTexture();
            
            m_lastCameraEvent = m_CameraEvent;
            isCommandBufferInvalid = true;
        }
        
        if (m_CommandBuffer == null || isCommandBufferInvalid == true)
        {
            m_Temporaries = new int[m_LODCount];
            
            if (m_CommandBuffer != null)
            {
                mainCamera.RemoveCommandBuffer(m_CameraEvent, m_CommandBuffer);
            }
            
            m_CommandBuffer = new CommandBuffer();
            m_CommandBuffer.name = "Hi-Z Buffer";
            
            RenderTargetIdentifier id = new RenderTargetIdentifier(m_HiZDepthTexture);
            m_CommandBuffer.SetGlobalTexture("_LightTexture", m_ShadowmapCopy);
            m_CommandBuffer.Blit(null, id, m_generateBufferMaterial, (int)Pass.Blit);
            
            for (int i = 0; i < m_LODCount; ++i)
            {
                m_Temporaries[i] = Shader.PropertyToID("_09659d57_Temporaries" + i.ToString());
                size >>= 1;
                size = Mathf.Max(size, 1);
                
                m_CommandBuffer.GetTemporaryRT(m_Temporaries[i], size, size, 0, FilterMode.Point, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
                
                if (i == 0)
                {
                    m_CommandBuffer.Blit(id, m_Temporaries[0], m_generateBufferMaterial, (int)Pass.Reduce);
                }
                else
                {
                    m_CommandBuffer.Blit(m_Temporaries[i - 1], m_Temporaries[i], m_generateBufferMaterial, (int)Pass.Reduce);
                }
                
                m_CommandBuffer.CopyTexture(m_Temporaries[i], 0, 0, id, 0, i + 1);
                
                if (i >= 1)
                {
                    m_CommandBuffer.ReleaseTemporaryRT(m_Temporaries[i - 1]);
                }
            }
            
            m_CommandBuffer.ReleaseTemporaryRT(m_Temporaries[m_LODCount - 1]);
            mainCamera.AddCommandBuffer(m_CameraEvent, m_CommandBuffer);
        }
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (m_indirectRenderer.debugDrawHiZ)
        {
            Camera.main.rect = new Rect(0.0f, 0.5f, 0.5f, 0.5f);
            Graphics.Blit(source, destination);
            
            
            Camera.main.rect = new Rect(0.0f, 0.0f, 0.5f, 0.5f);
            m_debugMaterial.SetInt("_NUM", 0);
            m_debugMaterial.SetInt("_LOD", m_indirectRenderer.debugHiZLOD);
            Graphics.Blit(m_HiZDepthTexture, destination, m_debugMaterial);
            
            Camera.main.rect = new Rect(0.5f, 0.0f, 0.5f, 0.5f);
            m_debugMaterial.SetInt("_NUM", 1);
            m_debugMaterial.SetInt("_LOD", 0);
            Graphics.Blit(m_HiZDepthTexture, destination, m_debugMaterial);
            
            Camera.main.rect = new Rect(0.5f, 0.5f, 0.5f, 0.5f);
            Graphics.Blit(topDownView, destination);
            
            Camera.main.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
        }
        else
        {
            Graphics.Blit(source, destination);
        }
    }

    #endregion
}