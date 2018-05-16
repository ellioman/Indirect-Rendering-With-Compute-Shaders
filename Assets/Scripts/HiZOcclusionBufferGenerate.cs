// https://github.com/gokselgoktas/hi-z-buffer

using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[RequireComponent(typeof (Camera))]
public class HiZOcclusionBufferGenerate : MonoBehaviour
{
    #region Variables

    // Unity Editor Variables
    [Header("References")]
    [SerializeField] private Shader m_generateBufferShader = null;
    [SerializeField] private Shader m_debugShader = null;
    public IndirectRenderer hiZController;
    // Private 
    private int m_LODCount = 0;
    private int[] m_Temporaries = null;
    private Camera m_camera = null;
    private Material m_generateBufferMaterial = null;
    private Material m_debugMaterial = null;
    private RenderTexture m_HiZDepthTexture = null;
    private CommandBuffer m_CommandBuffer = null;
    private CameraEvent m_CameraEvent = CameraEvent.AfterReflections;
    
    // Public Properties
    public int DebugLodLevel { get; set; }
    public bool DebugModeEnabled { get; set; }
    public Vector2 TextureSize { get; private set; }
    public RenderTexture HiZDepthTexture { get { return m_HiZDepthTexture; } }
    
    // Consts
    private const int MAXIMUM_BUFFER_SIZE = 512;

    // Enums
    private enum Pass
    {
        Blit,
        Reduce
    }

    #endregion

    #region MonoBehaviour

    private void Start()
    {
        m_generateBufferMaterial = new Material(m_generateBufferShader);
        m_debugMaterial = new Material(m_debugShader);

        m_camera = GetComponent<Camera>();
        m_camera.depthTextureMode = DepthTextureMode.Depth;
    }

    private void OnDisable()
    {
        Clear();
    }

    public void Clear()
    {
        if (m_camera != null)
        {
            if (m_CommandBuffer != null)
            {
                m_camera.RemoveCommandBuffer(m_CameraEvent, m_CommandBuffer);
                m_CommandBuffer = null;
            }
        }

        if (m_HiZDepthTexture != null)
        {
            m_HiZDepthTexture.Release();
            m_HiZDepthTexture = null;
        }
    }
    
    private void Update()
    {
        TextureSize = new Vector2(Mathf.NextPowerOfTwo(m_camera.pixelWidth), Mathf.NextPowerOfTwo(m_camera.pixelHeight));
        m_LODCount = (int) Mathf.Floor(Mathf.Log(TextureSize.x, 2f));
        
        bool isCommandBufferInvalid = false;
        if (m_LODCount == 0)
        {
            return;
        }

        if (m_HiZDepthTexture == null || (m_HiZDepthTexture.width != (int) TextureSize.x || m_HiZDepthTexture.height != (int) TextureSize.y))
        {
            if (m_HiZDepthTexture != null)
            {
                m_HiZDepthTexture.Release();
            }

            m_HiZDepthTexture = new RenderTexture((int)TextureSize.x, (int) TextureSize.y, 0, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear);
            m_HiZDepthTexture.filterMode = FilterMode.Point;

            m_HiZDepthTexture.useMipMap = true;
            m_HiZDepthTexture.autoGenerateMips = false;

            m_HiZDepthTexture.Create();

            m_HiZDepthTexture.hideFlags = HideFlags.HideAndDontSave;
            isCommandBufferInvalid = true;
        }
        
        if (m_CommandBuffer == null || isCommandBufferInvalid == true)
        {
            m_Temporaries = new int[m_LODCount];

            if (m_CommandBuffer != null)
            {
                m_camera.RemoveCommandBuffer(m_CameraEvent, m_CommandBuffer);
            }

            m_CommandBuffer = new CommandBuffer();
            m_CommandBuffer.name = "Hi-Z Buffer";
            
            // m_CommandBuffer.DrawMeshInstancedIndirect(hiZController.m_boundingBoxMesh, 0, hiZController.m_allMaterial, 0, hiZController.m_allArgsBuffer,0, null);

            RenderTargetIdentifier id = new RenderTargetIdentifier(m_HiZDepthTexture);
            m_CommandBuffer.SetRenderTarget(m_HiZDepthTexture);
            
            m_CommandBuffer.Blit(null, id, m_generateBufferMaterial, (int) Pass.Blit);


            for (int i = 0; i < m_LODCount; ++i)
            {
                m_Temporaries[i] = Shader.PropertyToID("_09659d57_Temporaries" + i.ToString());

                int width = ((int) TextureSize.x);
                width = width >>= 1;
                int height = ((int) TextureSize.y);
                height = height >>= 1;

                if (width == 0)
                {
                    width = 1;
                }
                
                if (height == 0)
                {
                    height = 1;
                }
                TextureSize = new Vector2(width, height);

                m_CommandBuffer.GetTemporaryRT(m_Temporaries[i], width, height, 0, FilterMode.Point, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear);
                
                if (i == 0)
                {
                    m_CommandBuffer.Blit(id, m_Temporaries [0], m_generateBufferMaterial, (int)Pass.Reduce);
                }
                else
                {
                    m_CommandBuffer.Blit(m_Temporaries [i - 1], m_Temporaries [i], m_generateBufferMaterial, (int)Pass.Reduce);
                }

                m_CommandBuffer.CopyTexture(m_Temporaries[i], 0, 0, id, 0, i + 1);

                if (i >= 1)
                {
                    m_CommandBuffer.ReleaseTemporaryRT(m_Temporaries [i - 1]);
                }
            }

            m_CommandBuffer.ReleaseTemporaryRT(m_Temporaries[m_LODCount - 1]);
            // m_CommandBuffer.ClearRenderTarget(true, true, Color.cyan);
            
            m_camera.AddCommandBuffer(m_CameraEvent, m_CommandBuffer);
        }
    }
    
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (DebugModeEnabled == false || HiZDepthTexture == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        m_debugMaterial.SetInt("_LOD", DebugLodLevel);
        Graphics.Blit(HiZDepthTexture, destination, m_debugMaterial);
    }


    #endregion
}