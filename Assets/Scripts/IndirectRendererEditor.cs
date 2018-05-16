using System;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System.Linq;
using Random = UnityEngine.Random;


public partial class IndirectRenderer : MonoBehaviour
{
    #region Editor

    #if UNITY_EDITOR

    private Vector4[] m_cameraFrustumPlanes = new Vector4[6];
    
    public struct FrustumPlane
    {
        public Vector3 normal;
        public Vector3 pointOnPlane;
        public Vector3 v0;
        public Vector3 v1;
        public Vector3 v2;
        public void DebugDraw(Color c)
        {
            Gizmos.color = c;
            Gizmos.DrawLine(v0, v1);
            Gizmos.DrawLine(v1, v2);
            Gizmos.DrawLine(v2, v0);
        }
    }

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
        Gizmos.color = Color.green;
        Gizmos.DrawLine(m_camera.transform.position, m_camera.transform.position + m_camera.transform.forward * m_camera.farClipPlane);

        FrustumPlane[] frustumPlanes = CalculateCameraFrustum(m_camera);
        for(int i = 0; i < frustumPlanes.Length; i++)
        {
            bool ifFacingAway = !AreDirectionsFacingEachother(frustumPlanes[i].normal, m_light.transform.forward);
            Color c = ifFacingAway ? Color.green : Color.red;
            if (ifFacingAway) {
                Vector3 change = -m_light.transform.forward * m_shadowCullingLength;
                frustumPlanes[i].pointOnPlane += change;
                frustumPlanes[i].v0  += change;
                frustumPlanes[i].v1  += change;
                frustumPlanes[i].v2  += change;
            }
            frustumPlanes[i].DebugDraw(c);
        }
    }

    private FrustumPlane[] CalculateCameraFrustum(Camera cam)
    {
        CalculateCameraFrustumPlanes();

        // Swap [1] and [2] so the order is better for the loop
        Vector4 temp;
        temp = m_cameraFrustumPlanes[1]; 
        m_cameraFrustumPlanes[1] = m_cameraFrustumPlanes[2]; 
        m_cameraFrustumPlanes[2] = temp; 
        
        m_camFarCenter = Vector3.zero;
        Vector3[] m_cameraNearPlaneVertices = new Vector3[4];
        Vector3[] m_cameraFarPlaneVertices = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            m_cameraNearPlaneVertices[i] = Plane3Intersect( m_cameraFrustumPlanes[4], m_cameraFrustumPlanes[i], m_cameraFrustumPlanes[( i + 1 ) % 4] ); //near corners on the created projection matrix
            m_cameraFarPlaneVertices[i] = Plane3Intersect( m_cameraFrustumPlanes[5], m_cameraFrustumPlanes[i], m_cameraFrustumPlanes[( i + 1 ) % 4] ); //far corners on the created projection matrix
            m_camFarCenter += m_cameraFarPlaneVertices[i];
        }
        m_camFarCenter /= 4f;

        // Revert the swap
        temp = m_cameraFrustumPlanes[1]; m_cameraFrustumPlanes[1] = m_cameraFrustumPlanes[2]; m_cameraFrustumPlanes[2] = temp;

        Vector3 camPos = m_camera.transform.position;
        return new FrustumPlane[] {
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Left],   pointOnPlane = camPos,          v0 = camPos, v1 = m_cameraFarPlaneVertices[0], v2 = m_cameraFarPlaneVertices[3] },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Right],  pointOnPlane = camPos,          v0 = camPos, v1 = m_cameraFarPlaneVertices[2], v2 = m_cameraFarPlaneVertices[1] },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Bottom], pointOnPlane = camPos,          v0 = camPos, v1 = m_cameraFarPlaneVertices[1], v2 = m_cameraFarPlaneVertices[0] },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Top],    pointOnPlane = camPos,          v0 = camPos, v1 = m_cameraFarPlaneVertices[3], v2 = m_cameraFarPlaneVertices[2] },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Near],   pointOnPlane = camPos,          v0 = camPos, v1 = camPos, v2 = camPos },
            new FrustumPlane() { normal = m_cameraFrustumPlanes[(int) PlaneSide.Far],    pointOnPlane = m_camFarCenter,  v0 = camPos, v1 = camPos, v2 = camPos },
        };
    }

    void CalculateCameraFrustumPlanes()
	{
        // Matrix4x4 M = m_camera.transform.localToWorldMatrix;
        Matrix4x4 V = m_camera.worldToCameraMatrix;
        Matrix4x4 P = m_camera.projectionMatrix;
        Matrix4x4 MVP = P*V;//*M;

		// Left clipping plane.
		m_cameraFrustumPlanes[0] = new Vector4( MVP[3]+MVP[0], MVP[7]+MVP[4], MVP[11]+MVP[8], MVP[15]+MVP[12] );
        m_cameraFrustumPlanes[0] /= Mathf.Sqrt(m_cameraFrustumPlanes[0].x * m_cameraFrustumPlanes[0].x + m_cameraFrustumPlanes[0].y * m_cameraFrustumPlanes[0].y + m_cameraFrustumPlanes[0].z * m_cameraFrustumPlanes[0].z);
		// Right clipping plane.
		m_cameraFrustumPlanes[1] = new Vector4( MVP[3]-MVP[0], MVP[7]-MVP[4], MVP[11]-MVP[8], MVP[15]-MVP[12] );
        m_cameraFrustumPlanes[1] /= Mathf.Sqrt(m_cameraFrustumPlanes[1].x * m_cameraFrustumPlanes[1].x + m_cameraFrustumPlanes[1].y * m_cameraFrustumPlanes[1].y + m_cameraFrustumPlanes[1].z * m_cameraFrustumPlanes[1].z);
		// Bottom clipping plane.
		m_cameraFrustumPlanes[2] = new Vector4( MVP[3]+MVP[1], MVP[7]+MVP[5], MVP[11]+MVP[9], MVP[15]+MVP[13] );
        m_cameraFrustumPlanes[2] /= Mathf.Sqrt(m_cameraFrustumPlanes[2].x * m_cameraFrustumPlanes[2].x + m_cameraFrustumPlanes[2].y * m_cameraFrustumPlanes[2].y + m_cameraFrustumPlanes[2].z * m_cameraFrustumPlanes[2].z);
		// Top clipping plane.
		m_cameraFrustumPlanes[3] = new Vector4( MVP[3]-MVP[1], MVP[7]-MVP[5], MVP[11]-MVP[9], MVP[15]-MVP[13] );
        m_cameraFrustumPlanes[3] /= Mathf.Sqrt(m_cameraFrustumPlanes[3].x * m_cameraFrustumPlanes[3].x + m_cameraFrustumPlanes[3].y * m_cameraFrustumPlanes[3].y + m_cameraFrustumPlanes[3].z * m_cameraFrustumPlanes[3].z);
		// Near clipping plane.
		m_cameraFrustumPlanes[4] = new Vector4( MVP[3]+MVP[2], MVP[7]+MVP[6], MVP[11]+MVP[10], MVP[15]+MVP[14] );
        m_cameraFrustumPlanes[4] /= Mathf.Sqrt(m_cameraFrustumPlanes[4].x * m_cameraFrustumPlanes[4].x + m_cameraFrustumPlanes[4].y * m_cameraFrustumPlanes[4].y + m_cameraFrustumPlanes[4].z * m_cameraFrustumPlanes[4].z);
		// Far clipping plane.
		m_cameraFrustumPlanes[5] = new Vector4( MVP[3]-MVP[2], MVP[7]-MVP[6], MVP[11]-MVP[10], MVP[15]-MVP[14] );
        m_cameraFrustumPlanes[5] /= Mathf.Sqrt(m_cameraFrustumPlanes[5].x * m_cameraFrustumPlanes[5].x + m_cameraFrustumPlanes[5].y * m_cameraFrustumPlanes[5].y + m_cameraFrustumPlanes[5].z * m_cameraFrustumPlanes[5].z);
	}

    // Get intersection point of 3 planes
    private Vector3 Plane3Intersect ( Vector4 p1, Vector4 p2, Vector4 p3 )
    { 
        Vector3 p1Norm = new Vector3(p1.x, p1.y, p1.z);
        Vector3 p2Norm = new Vector3(p2.x, p2.y, p2.z);
        Vector3 p3Norm = new Vector3(p3.x, p3.y, p3.z);
        
        return (( -p1.w * Vector3.Cross( p2Norm, p3Norm ) ) +
                ( -p2.w * Vector3.Cross( p3Norm, p1Norm ) ) +
                ( -p3.w * Vector3.Cross( p1Norm, p2Norm ) ) ) /
            ( Vector3.Dot( p1Norm, Vector3.Cross( p2Norm, p3Norm ) ) );
    }

    #endif
    #endregion
}