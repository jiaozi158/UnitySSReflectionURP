UnitySSReflectionURP
=============
 
 Screen Space Reflection for Unity URP (Universal Render Pipeline).
 
 **Please read the Documentation and Requirements before using this repository.**
 
Screenshots
------------
**Sample Scene**
 
Approximation:
 
 ![SSRApproximation](https://github.com/jiaozi158/UnitySSReflectionURP/blob/main/Documentation/Images/SampleScene/ApproximationMode.jpg)
 
PBR Accumulation:
 
 ![PBRAccumulation](https://github.com/jiaozi158/UnitySSReflectionURP/blob/main/Documentation/Images/SampleScene/PBRAccumulationMode.jpg)
 
**Not Included**
 
[Stormtrooper Star Wars VII](https://www.blendswap.com/blend/13953) by ScottGraham (CC-BY-3.0)
 
Enable SSR:
 
 ![StormTrooperSSROn](https://github.com/jiaozi158/UnitySSReflectionURP/blob/main/Documentation/Images/Others/StormTrooperSSR.jpg)
 
Disable SSR:
 
 ![StormTrooperSSROff](https://github.com/jiaozi158/UnitySSReflectionURP/blob/main/Documentation/Images/Others/StormTrooper.jpg)
 
Documentation
------------
[Here](https://github.com/jiaozi158/UnitySSReflectionURP/blob/main/Documentation/Documentation.md).

Requirements
------------
- Unity 2022.2 and URP 14 or above.
- Any rendering path.
- Any camera projection type. (perspective or orthographic)
- Multiple Render Targets support. (at least OpenGL ES 3.0 or equivalent)
- [Extra steps](https://github.com/jiaozi158/UnitySSPathTracingURP/blob/main/Documentation/ForwardPathSupport.md#opengl-platforms-extra-setup) are needed for OpenGL APIs.
 
Known Issues & Limitations
------------
- The matrices used to reconstruct world positions are incorrect on XR platforms. (Try porting it to a fullscreen shader graph?)
- The reflection blending is inaccurate on ForwardOnly objects (Complex Lit) in Deferred rendering path. (Not happening in Forward path)
- PBR Accumulation mode doesn't work properly with distortion post-processing effects due to URP limitations. (motion vectors)
- Transparent objects are ignored by reflections.
- Reflections are only computed once. (Ray bounce is 1)
 
License
------------
MIT ![MIT License](http://img.shields.io/badge/license-MIT-blue.svg?style=flat)
 
Details
------------
Part of the code is modified from [UnitySSPathTracingURP](https://github.com/jiaozi158/UnitySSPathTracingURP).
 