using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public class Example : MonoBehaviour
{
    #region Variables

    // Public
    public bool indirectRenderingEnabled = true;
    public bool createInstancesOnAwake = false;
    public bool shouldInstantiatePrefabs = false;
    public float areaSize = 5000f;
    public NumberOfInstances numberOfInstances;
    public IndirectRenderer indirectRenderer;
    public IndirectInstanceData[] instances;

    // Enums
    public enum NumberOfInstances
    {
        _2048 = 2048,
        _4096 = 4096,
        _8192 = 8192,
        _16384 = 16384,
        _32768 = 32768,
        _65536 = 65536,
        _131072 = 131072,
        _262144 = 262144
    }

    private bool lastIndirectRenderingEnabled = false;
    private bool lastIndirectDrawShadows = false;
    private GameObject normalInstancesParent;
    #endregion

    #region MonoBehaviour

    private void Start()
    {
        if (!AssertInstanceData())
        {
            enabled = false;
            return;
        }
        
        if (createInstancesOnAwake)
        {
            CreateInstanceData();
        }
        
        lastIndirectRenderingEnabled = indirectRenderingEnabled;
        lastIndirectDrawShadows = indirectRenderer.drawInstanceShadows;
        
        if (shouldInstantiatePrefabs)
        {
            InstantiateInstance();
        }
        
        indirectRenderer.Initialize(ref instances);
        indirectRenderer.StartDrawing();
    }
    
    private bool AssertInstanceData()
    {
        for (int i = 0; i < instances.Length; i++)
        {
            if (instances[i].prefab == null)
            {
                Debug.LogError("Missing Prefab on instance at index: " + i + "! Aborting.");
                return false;
            }
            
            if (instances[i].normalMaterial == null)
            {
                Debug.LogError("Missing normalMaterial on instance at index: " + i + "! Aborting.");
                return false;
            }
            
            if (instances[i].indirectMaterial == null)
            {
                Debug.LogError("Missing indirectMaterial on instance at index: " + i + "! Aborting.");
                return false;
            }
            
            if (instances[i].lod00Mesh == null)
            {
                Debug.LogError("Missing lod00Mesh on instance at index: " + i + "! Aborting.");
                return false;
            }
            
            if (instances[i].lod01Mesh == null)
            {
                Debug.LogError("Missing lod01Mesh on instance at index: " + i + "! Aborting.");
                return false;
            }
            
            if (instances[i].lod02Mesh == null)
            {
                Debug.LogError("Missing lod02Mesh on instance at index: " + i + "! Aborting.");
                return false;
            }
        }
        
        return true;
    }

    private void Update()
    {
        if (lastIndirectRenderingEnabled != indirectRenderingEnabled)
        {
            lastIndirectRenderingEnabled = indirectRenderingEnabled;
            
            if (normalInstancesParent != null)
            {
                normalInstancesParent.SetActive(!indirectRenderingEnabled);
            }
            
            if (indirectRenderingEnabled)
            {
                indirectRenderer.Initialize(ref instances);
                indirectRenderer.StartDrawing();
            }
            else
            {
                indirectRenderer.StopDrawing(true);
            }
        }
        
        if (lastIndirectDrawShadows != indirectRenderer.drawInstanceShadows)
        {
            lastIndirectDrawShadows = indirectRenderer.drawInstanceShadows;
            
            if (normalInstancesParent != null)
            {
                SetShadowCastingMode(lastIndirectDrawShadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
            }
        }
    }

    #endregion

    #region Private Functions

    private void SetShadowCastingMode(ShadowCastingMode newMode)
    {
        Renderer[] rends = normalInstancesParent.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < rends.Length; i++)
        {
            rends[i].shadowCastingMode = newMode;
        }
    }

    [ContextMenu("CreateInstanceData()")]
    private void CreateInstanceData()
    {
        if (instances.Length == 0)
        {
            Debug.LogError("Instances list is empty!", this);
            return;
        }
        
        int numOfInstancesPerType = ((int)numberOfInstances) / instances.Length;
        int instanceCounter = 0;
        for (int i = 0; i < instances.Length; i++)
        {
            instances[i].positions = new Vector3[numOfInstancesPerType];
            instances[i].rotations = new Vector3[numOfInstancesPerType];
            instances[i].scales    = new Vector3[numOfInstancesPerType];
            
            Vector2 L = Vector2.one * i;
            for (int k = 0; k < numOfInstancesPerType; k++)
            {
                Vector3 rotation = Vector3.zero;
                Vector3 scale = Vector3.one * Random.Range(instances[i].scaleRange.x, instances[i].scaleRange.y);
                Vector3 pos = Nth_weyl(L, instanceCounter++) * areaSize;
                pos = new Vector3(pos.x - areaSize * 0.5f, 0f, pos.y - areaSize * 0.5f) + instances[i].positionOffset;
                
                instances[i].positions[k] = pos;
                instances[i].rotations[k] = rotation;
                instances[i].scales[k] = scale;
            }
        }
    }
    
    private void InstantiateInstance()
    {
        Profiler.BeginSample("InstantiateInstance");
        normalInstancesParent = new GameObject("InstancesParent");
        
        Profiler.BeginSample("for instance.Count...");
        for (int i = 0; i < instances.Length; i++)
        {
            IndirectInstanceData instance = instances[i];
            
            GameObject parentObj = new GameObject(instance.prefab.name);
            parentObj.transform.parent = normalInstancesParent.transform;
            
            Profiler.BeginSample("for instance.positions.Length...");
            for (int j = 0; j < instance.positions.Length; j++)
            {
                GameObject obj = Instantiate(instance.prefab);
                obj.transform.position = instance.positions[j];
                obj.transform.localScale = instance.scales[j];
                obj.transform.rotation = Quaternion.Euler(instance.rotations[j]);
                obj.transform.parent = parentObj.transform;
            }
            Profiler.EndSample();
            
            Profiler.BeginSample("for parentObj.Renderers.Length...");
            Renderer[] renderers = parentObj.GetComponentsInChildren<Renderer>();
            for (int r = 0; r < renderers.Length; r++)
            {
                renderers[r].shadowCastingMode = lastIndirectDrawShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            Profiler.EndSample();
        }
        Profiler.EndSample();
        
        
        normalInstancesParent.SetActive(!indirectRenderingEnabled);
        Profiler.EndSample();
    }

    // Taken from:
    // http://extremelearning.com.au/unreasonable-effectiveness-of-quasirandom-sequences/
    // https://www.shadertoy.com/view/4dtBWH
    private Vector2 Nth_weyl(Vector2 p0, float n)
    {
        Vector2 res = p0 + n * new Vector2(0.754877669f, 0.569840296f);
        res.x %= 1;
        res.y %= 1;
        return res;
    }

    #endregion
}
