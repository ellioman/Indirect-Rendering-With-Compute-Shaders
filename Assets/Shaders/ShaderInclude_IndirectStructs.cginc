
#ifndef __INDIRECT_INCLUDE__
#define __INDIRECT_INCLUDE__

struct InstanceData
{
	uint drawDataID;				// 1
	uint drawCallID;				// 2
	float3 position;				// 5
	float3 rotation;				// 8
	float uniformScale;				// 9
	float3 boundsCenter;			// 12
	float3 boundsExtents;			// 15
	float distanceToCamera;			// 16
};

struct InstanceDrawData
{
	float4x4 unity_ObjectToWorld;	// 16
	float4x4 unity_WorldToObject;	// 32
};

struct BoundingBox
{
	float3 minPos;					// 3
	float3 maxPos;					// 6
	float4 clipMinMaxXY;			// 10
	float clipMinZ;					// 11
	float clipMaxZ;					// 12
	float4 clipLightMinMaxXY;		// 16
};

#endif
