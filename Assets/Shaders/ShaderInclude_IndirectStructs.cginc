
#ifndef __INDIRECT_INCLUDE__
#define __INDIRECT_INCLUDE__

struct ComputeShaderInputData
{
	uint drawCallID;		// 1
	float3 position; 		// 3
	float3 rotation; 		// 6
	float uniformScale;		// 7
	float3 boundsCenter; 	// 10
	float3 boundsExtents; 	// 13
	float distanceToCamera; // 14
};

struct OutputData
{
    float3 position;// 3
    float3 rotation;// 6
    float uniformScale;	// 7
};

struct FrustumPlane
{
	float4 normal;
	float3 pointOnPlane;
};

struct BoundingBox
{
	float4 corners[8];
	float3 center;
	float3 minPos;
	float3 maxPos;
};

#endif
