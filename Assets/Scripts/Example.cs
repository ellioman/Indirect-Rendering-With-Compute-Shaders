using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class Example : MonoBehaviour
{
    #region Variables

    // Public
    public bool createInstancesOnAwake = false;
    public bool indirectRenderingEnabled = true;
    public float areaSize = 5000f;
    public NumberOfInstances numberOfInstances;
    public IndirectRenderer indirectRenderer;
    public List<IndirectInstanceData> instances = new List<IndirectInstanceData>();

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
    private GameObject normalInstancesParent;
    #endregion

    #region MonoBehaviour

    private void Start()
    {
        if (createInstancesOnAwake)
        {
            CreateInstanceData();
        }
        
        InstantiateInstance();
        indirectRenderer.Initialize(instances.ToArray());
        
        lastIndirectRenderingEnabled = indirectRenderingEnabled;
        indirectRenderer.isEnabled = indirectRenderingEnabled;
    }

    private void Update()
    {
        if (lastIndirectRenderingEnabled != indirectRenderingEnabled)
        {
            lastIndirectRenderingEnabled = indirectRenderingEnabled;
            normalInstancesParent.SetActive(!indirectRenderingEnabled);
            indirectRenderer.isEnabled = indirectRenderingEnabled;
        }
    }

    #endregion

    #region Private Functions

    [ContextMenu("CreateInstanceData()")]
    private void CreateInstanceData()
    {
        if (instances.Count == 0)
        {
            Debug.LogError("Instances list is empty!", this);
            return;
        }
        
        int numOfInstancesPerType = ((int)numberOfInstances) / instances.Count;
        int instanceCounter = 0;
        for (int i = 0; i < instances.Count; i++)
        {
            instances[i].positions = new Vector3[numOfInstancesPerType];
            instances[i].rotations = new Vector3[numOfInstancesPerType];
            instances[i].uniformScales = new float[numOfInstancesPerType];
            
            Vector2 L = Vector2.one * i;
            for (int k = 0; k < numOfInstancesPerType; k++)
            {
                Vector2 pos = Nth_weyl(L, instanceCounter++);
                pos *= areaSize;
                instances[i].positions[k] = new Vector3(pos.x - areaSize * 0.5f, 35f, pos.y - areaSize * 0.5f) + instances[i].positionOffset;
                instances[i].rotations[k] = new Vector3(0f, 0f, 0f);
                instances[i].uniformScales[k] = Random.Range(instances[i].scaleRange.x, instances[i].scaleRange.y);
            }
        }
    }
    
    private void InstantiateInstance()
    {
        normalInstancesParent = new GameObject("InstancesParent");
        for (int i = 0; i < instances.Count; i++)
        {
            for (int j = 0; j < instances[i].positions.Length; j++)
            {
                GameObject obj = Instantiate(instances[i].prefab);
                obj.transform.position = instances[i].positions[j];
                obj.transform.localScale = Vector3.one * instances[i].uniformScales[j];
                obj.transform.rotation = Quaternion.Euler(instances[i].rotations[j]);
                obj.transform.parent = normalInstancesParent.transform;
            }
        }
        
        normalInstancesParent.SetActive(!indirectRenderingEnabled);
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
