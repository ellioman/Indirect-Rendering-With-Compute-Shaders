using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class Example : MonoBehaviour
{
    #region Variables

    // Public
    public bool createInstancesOnAwake;
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

    #endregion

    #region MonoBehaviour

    private void Start()
    {
        if (createInstancesOnAwake)
        {
            CreateInstanceData();
        }
        indirectRenderer.Initialize(instances.ToArray());
    }

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

    #endregion

    #region Private Functions

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
