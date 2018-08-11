#if UNITY_EDITOR

using System;
using UnityEngine;

public partial class IndirectRenderer : MonoBehaviour
{
    #region Editor

    bool AreDirectionsFacingEachother(Vector3 dir1, Vector3 dir2)
    {
        return Vector3.Dot( dir1, dir2) <= 0.0f;
    }

    bool IsBoxInFrustum( FrustumPlane[] fru, Bounds box)
    {
        int result = 0;

        // check box outside/inside of frustum
        for(int i = 0; i < fru.Length; i++)
        {
            result = 0;
            result += ((Vector3.Dot( fru[i].normal, new Vector3(box.min.x, box.min.y, box.min.z) - fru[i].pointOnPlane ) <= 0.0 ) ? 1 : 0);
            result += ((Vector3.Dot( fru[i].normal, new Vector3(box.max.x, box.min.y, box.min.z) - fru[i].pointOnPlane ) <= 0.0 ) ? 1 : 0);
            result += ((Vector3.Dot( fru[i].normal, new Vector3(box.min.x, box.max.y, box.min.z) - fru[i].pointOnPlane ) <= 0.0 ) ? 1 : 0);
            result += ((Vector3.Dot( fru[i].normal, new Vector3(box.max.x, box.max.y, box.min.z) - fru[i].pointOnPlane ) <= 0.0 ) ? 1 : 0);
            result += ((Vector3.Dot( fru[i].normal, new Vector3(box.min.x, box.min.y, box.max.z) - fru[i].pointOnPlane ) <= 0.0 ) ? 1 : 0);
            result += ((Vector3.Dot( fru[i].normal, new Vector3(box.max.x, box.min.y, box.max.z) - fru[i].pointOnPlane ) <= 0.0 ) ? 1 : 0);
            result += ((Vector3.Dot( fru[i].normal, new Vector3(box.min.x, box.max.y, box.max.z) - fru[i].pointOnPlane ) <= 0.0 ) ? 1 : 0);
            result += ((Vector3.Dot( fru[i].normal, new Vector3(box.max.x, box.max.y, box.max.z) - fru[i].pointOnPlane ) <= 0.0 ) ? 1 : 0);
            if( result==8 ) return false;
        }

        // check frustum outside/inside box
        // result=0; for( int i=0; i<fru.mPoints.Length; i++ ) result += ((fru.mPoints[i].x > box.max.x)?1:0); if( result==8 ) return false;
        // result=0; for( int i=0; i<fru.mPoints.Length; i++ ) result += ((fru.mPoints[i].x < box.min.x)?1:0); if( result==8 ) return false;
        // result=0; for( int i=0; i<fru.mPoints.Length; i++ ) result += ((fru.mPoints[i].y > box.max.y)?1:0); if( result==8 ) return false;
        // result=0; for( int i=0; i<fru.mPoints.Length; i++ ) result += ((fru.mPoints[i].y < box.min.y)?1:0); if( result==8 ) return false;
        // result=0; for( int i=0; i<fru.mPoints.Length; i++ ) result += ((fru.mPoints[i].z > box.max.z)?1:0); if( result==8 ) return false;
        // result=0; for( int i=0; i<fru.mPoints.Length; i++ ) result += ((fru.mPoints[i].z < box.min.z)?1:0); if( result==8 ) return false;

        return true;
    }

    private void OnDrawGizmos()
    {
        if (m_camera == null)
        {
            return;
        }

        // if (instancesPositionsArray != null)
        // {
        //     Gizmos.color = new Color(1,1,1, 0.25f);
        //     for (int i = 0; i < instancesPositionsArray.Length; i++)
        //     {
        //         Gizmos.DrawWireCube(
        //             instancesPositionsArray[i].position, 
        //             instancesPositionsArray[i].boundsExtents * 2f);
        //     }
        // }

        CalculateCameraFrustum(ref m_camera);
        
        Gizmos.color = Color.green;
        Gizmos.DrawLine(m_camera.transform.position, m_camera.transform.position + m_camera.transform.forward * m_camera.farClipPlane);

        for(int i = 0; i < m_frustumPlanes.Length; i++)
        {
            bool ifFacingAway = !AreDirectionsFacingEachother(m_frustumPlanes[i].normal, m_light.transform.forward);
            Color c = ifFacingAway ? Color.green : Color.red;
            if (ifFacingAway) {
                Vector3 change = -m_light.transform.forward * m_shadowCullingLength;
                m_frustumPlanes[i].pointOnPlane += change;
                m_frustumPlanes[i].v0 += change;
                m_frustumPlanes[i].v1 += change;
                m_frustumPlanes[i].v2 += change;
            }
            m_frustumPlanes[i].DebugDraw(c);
        }
    }

    #endregion
}

#endif