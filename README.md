Indirect Rendering With Compute Shaders
========

An example of rendering numerous instances in Unity3D using Compute shaders for culling and LOD'ing and Graphics.DrawMeshInstancedIndirect to draw.

![Generative](https://raw.githubusercontent.com/ellioman/Indirect-Rendering-With-Compute-Shaders/master/Gifs/IndirectRendering_01_Culling.gif)

![Generative](https://raw.githubusercontent.com/ellioman/Indirect-Rendering-With-Compute-Shaders/master/Gifs/IndirectRendering_02_LOD.gif)

## Note
This project has only been tested on a Macbook Pro using Metal. When using DirectX, it is very likely you need to add the arguments buffer into the instance rendering shader and add the offset to unity_InstanceID to get the correct instance.

## Features

- Quasirandom squences to place the instances
- Draw things using Graphics.DrawMeshInstancedIndirect
- GPU Sorting with Bitonic sorting
- Compute shader: Frustum and shadow caster culling 
- Compute shader: Occlusion culling with HierarchicalZBuffer
- Compute shader: Detail (Screen Size) Culling
- Compute shader: LOD objects using the distance from camera to object
- Simple shadow mode: Only use one shadow mesh for each instance type to lower setpass calls

## Project Setup
- "_Example.cs" script on the "Example" game object:
	- Creates the instance data and sends it to the Indirect Renderer class.
- "IndirectRenderer.cs" on the "MainCamera" game object:
	- Is the main class in the project. It initializes all the buffers and compute shaders and then dispatches the compute shaders and draws the objects when Unity calls the PreCull() function.
- "HiZBuffer.cs" script on the "OccluderCamera" game object:
	- Creates a hierarchial Z-Buffer texture that contains the depth texture and shadow map which are used when doing frustum and occlusion culling

## TODO
- Improve the instance sorting:
	- On my Macbook pro (mid 2014) sorting takes approx 50% of the GPU time
	- The GPU sorting must support instance numbers that are not in the non power of two 
	- Find a better performant approach for the CPU Sorting
- Try to make the compute buffers smaller in size. Ex: Pack the three floats, used for bounds size, into one uint.
- Create the Hi-Z Texture with compute shaders
- Try out Raster Occlusion instead of Hi-Z (See "NVidia Siggraph 2014" & "Github - nvpro-samples" below)

## Resources

[The Unreasonable Effectiveness of Quasirandom Sequences](http://extremelearning.com.au/unreasonable-effectiveness-of-quasirandom-sequences/)

[Kostas Anagnostou - GPU Driven Rendering Experiments](http://bit.ly/Kostas-GPUDrivenRenderingExperiments)

[Kostas Anagnostou - Experiments in GPU-based occlusion culling](https://interplayoflight.wordpress.com/2017/11/15/experiments-in-gpu-based-occlusion-culling/)

[Kostas Anagnostou - Experiments in GPU-based occlusion culling part 2](https://interplayoflight.wordpress.com/2018/01/15/experiments-in-gpu-based-occlusion-culling-part-2-multidrawindirect-and-mesh-lodding/)

[Sakib Saikia - Going Indirect on UE3](https://sakibsaikia.github.io/graphics/2017/08/18/Going-Indirect-On-UE3.html)

[RasterGrid - Hierarchical-Z map based occlusion culling](http://rastergrid.com/blog/2010/10/hierarchical-z-map-based-occlusion-culling/)

[StackOverflow - Hierachical Z-Buffering for occlusion culling](https://gamedev.stackexchange.com/questions/112155/hierachical-z-buffering-for-occlusion-culling)

[bazhenovc -  GPU Driven Occlusion Culling in Life is Feudal ](https://bazhenovc.github.io/blog/post/gpu-driven-occlusion-culling-slides-lif/)

[NVIDIA - Siggraph 2014 - Scene Rendering Techniques](http://on-demand.gputechconf.com/siggraph/2014/presentation/SG4117-OpenGL-Scene-Rendering-Techniques.pdf)

[Github - nvpro-samples/gl_occlusion_culling](https://github.com/nvpro-samples/gl_occlusion_culling)

[GPU Gems 2 - Hardware Occlusion Queries Made Useful](https://developer.nvidia.com/gpugems/GPUGems2/gpugems2_chapter06.html)

[ARM Developer Center - hiz_cull.cs](https://arm-software.github.io/opengl-es-sdk-for-android/hiz__cull_8cs_source.html)

[L. Spiro - Tightly Culling Shadow Casters for Directional Lights (Part 1)](http://lspiroengine.com/?p=153)

[L. Spiro - Tightly Culling Shadow Casters for Directional Lights (Part 2)](http://lspiroengine.com/?p=187)

[nonoptimalrobot - Shadow Volume Culling](https://nonoptimalrobot.wordpress.com/2012/04/19/shadow-volume-culling/)

[Stephen Hill and Daniel Collin - Practical, Dynamic Visibility for Games](https://blog.selfshadow.com/publications/practical-visibility/)


License
-------

Copyright (C) 2017-2018 Elvar Orn Unnthorsson

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
