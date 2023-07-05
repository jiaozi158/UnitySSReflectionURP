#ifndef URP_SCREEN_SPACE_REFLECTION_HLSL
#define URP_SCREEN_SPACE_REFLECTION_HLSL

// Quality Presets
//==============================================================================
#define STEP_SIZE             _StepSize
#define MAX_STEP              uint(_MaxStep)
//#define RAY_BOUNCE            1
//==============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

TEXTURE2D_X_HALF(_GBuffer0); // color.rgb + materialFlags.a
TEXTURE2D_X_HALF(_GBuffer1); // specular.rgb + oclusion.a
TEXTURE2D_X_HALF(_GBuffer2); // normalWS.rgb + smoothness.a

#if defined(_BACKFACE_ENABLED)
TEXTURE2D_X(_CameraBackDepthTexture);
#endif

SAMPLER(sampler_BlitTexture);
SAMPLER(my_point_clamp_sampler);

// URP pre-defined the following variable on 2023.2+.
#if UNITY_VERSION < 202320
float4 _BlitTexture_TexelSize;
#endif

#ifndef kMaterialFlagSpecularSetup
#define kMaterialFlagSpecularSetup 8 // Lit material use specular setup instead of metallic setup
#endif

#ifndef kDieletricSpec
#define kDieletricSpec half4(0.04, 0.04, 0.04, 1.0 - 0.04) // standard dielectric reflectivity coef at incident angle (= 4%)
#endif

uint UnpackMaterialFlags(float packedMaterialFlags)
{
    return uint((packedMaterialFlags * 255.0h) + 0.5h);
}

#if defined(_GBUFFER_NORMALS_OCT)
half3 UnpackNormal(half3 pn)
{
    half2 remappedOctNormalWS = half2(Unpack888ToFloat2(pn));           // values between [ 0, +1]
    half2 octNormalWS = remappedOctNormalWS.xy * half(2.0) - half(1.0); // values between [-1, +1]
    return half3(UnpackNormalOctQuadEncode(octNormalWS));               // values between [-1, +1]
}
#else
half3 UnpackNormal(half3 pn) { return pn; }                             // values between [-1, +1]
#endif

// position  : world space ray origin
// direction : world space ray direction
struct Ray
{
    float3 position;
    half3  direction;
};

// position  : world space hit position
// distance  : distance that ray travels
// ...       : surfaceData of hit position
struct RayHit
{
    float3 position;
    float  distance;
    float2 uv;
    half3  albedo;
    half3  specular;
    half   occlusion;
    half3  normal;
    half   smoothness;
};

// position : the intersection between Ray and Scene.
// distance : the distance from Ray's starting position to intersection.
// normal   : the normal direction of the intersection.
// ...      : material information from GBuffer.
RayHit InitializeRayHit()
{
    RayHit rayHit;
    rayHit.position = float3(0.0, 0.0, 0.0);
    rayHit.distance = REAL_EPS;
    rayHit.uv = float2(0.0, 0.0);
    rayHit.albedo = half3(0.0, 0.0, 0.0);
    rayHit.specular = half3(0.0, 0.0, 0.0);
    rayHit.occlusion = 1.0;
    rayHit.normal = half3(0.0, 0.0, 0.0);
    rayHit.smoothness = 0.0;
    return rayHit;
}

// Modified from HDRP's ScreenSpaceReflection.compute
// 
// GGX VNDF via importance sampling
half3 ImportanceSampleGGX_VNDF(float2 random, half3 normalWS, half3 viewDirWS, half smoothness, out bool valid)
{
    half roughness = (1.0 - smoothness); // roughness: [(1.0 - smoothness) * (1.0 - smoothness)]
    roughness *= roughness;

    half3x3 localToWorld = GetLocalFrame(normalWS);

    half VdotH;
    half3 localV, localH;
    SampleGGXVisibleNormal(random, viewDirWS, localToWorld, roughness, localV, localH, VdotH);

    // Compute the reflection direction
    half3 localL = 2.0 * VdotH * localH - localV;
    half3 outgoingDir = mul(localL, localToWorld);

    half NdotL = dot(normalWS, outgoingDir);

    valid = (NdotL >= 0.001);

    return outgoingDir;
}

void HitSurfaceDataFromGBuffers(float2 screenUV, inout half3 albedo, inout half3 specular, inout half occlusion, inout half3 normal, inout half smoothness)
{
    half4 gBuffer0 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer0, my_point_clamp_sampler, screenUV, 0);
    half4 gBuffer1 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer1, my_point_clamp_sampler, screenUV, 0);
    half4 gBuffer2 = SAMPLE_TEXTURE2D_X_LOD(_GBuffer2, my_point_clamp_sampler, screenUV, 0);

    albedo = gBuffer0.rgb;
    specular = (UnpackMaterialFlags(gBuffer0.a) == kMaterialFlagSpecularSetup) ? gBuffer1.rgb : lerp(kDieletricSpec.rgb, max(albedo.rgb, kDieletricSpec.rgb), gBuffer1.r); // Specular & Metallic setup conversion
    occlusion = gBuffer1.a;
    normal = UnpackNormal(gBuffer2.rgb);
    smoothness = gBuffer2.a;
}

float GenerateRandomFloat(float2 screenUV)
{
#if defined(_SSR_ACCUM)
    float time = unity_DeltaTime.y * _Time.y + _Seed; // accumulate the noise over time (frames)
#else
    float time = _Seed;
#endif
    _Seed += 1.0;
    return GenerateHashedRandomFloat(uint3(screenUV * _ScreenSize.xy * _DownSample, time));
}

// Supports perspective and orthographic projections
float ConvertLinearEyeDepth(float deviceDepth)
{
    UNITY_BRANCH
    if (unity_OrthoParams.w == 0.0)
        return LinearEyeDepth(deviceDepth, _ZBufferParams);
    else
    {
    #if (UNITY_REVERSED_Z == 1)
        deviceDepth = 1.0 - deviceDepth;
    #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, deviceDepth);
    }
        
}

// If no intersection, "rayHit.distance" will remain "REAL_EPS".
RayHit RayMarching(Ray ray, half dither, float distance)
{
    RayHit rayHit = InitializeRayHit();
    
    // Use a constant initial STEP_SIZE.
    half stepSize = STEP_SIZE;
    half marchingThickness = _Thickness;
    half accumulatedStep = 0.0;

    //float lastDepthDiff = 0.0;
    //float2 lastRayPositionNDC = float2(0.0, 0.0);
    //float3 lastRayPositionWS = float3(0.0, 0.0, 0.0);
    bool startBinarySearch = false;
    UNITY_LOOP
    for (uint i = 1; i <= MAX_STEP; i++)
    {
        accumulatedStep += stepSize + stepSize * dither;

        float3 rayPositionWS = ray.position + accumulatedStep * ray.direction;

        float3 rayPositionNDC = ComputeNormalizedDeviceCoordinatesWithZ(rayPositionWS, GetWorldToHClipMatrix());
        rayPositionNDC.xy = UnityStereoTransformScreenSpaceTex(rayPositionNDC.xy);

#if (UNITY_REVERSED_Z == 0) // OpenGL platforms
        rayPositionNDC.z = rayPositionNDC.z * 0.5 + 0.5; // -1..1 to 0..1
#endif

        // Stop marching the ray when outside screen space.
        bool isScreenSpace = (rayPositionNDC.x > 0.0 && rayPositionNDC.y > 0.0 && rayPositionNDC.x < 1.0 && rayPositionNDC.y < 1.0) ? true : false;
        if (!isScreenSpace)
            break;

        float deviceDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, rayPositionNDC.xy, 0).r; // z buffer depth

        float sceneDepth = ConvertLinearEyeDepth(deviceDepth);
        float hitDepth = ConvertLinearEyeDepth(rayPositionNDC.z); // Non-GL (DirectX): rayPositionNDC.z is (near to far) 1..0

        float depthDiff = sceneDepth - hitDepth;

#if defined(_BACKFACE_ENABLED)
        float deviceBackDepth = SAMPLE_TEXTURE2D_X_LOD(_CameraBackDepthTexture, my_point_clamp_sampler, rayPositionNDC.xy, 0).r;
        // Avoid infinite thickness for objects with no thickness (ex. Plane).
    #if (UNITY_REVERSED_Z == 1)
        bool backDepthValid = deviceBackDepth != 0.0 ? true : false;
    #else
        bool backDepthValid = deviceBackDepth != 1.0 ? true : false; // OpenGL Platforms.
    #endif
        float sceneBackDepth = ConvertLinearEyeDepth(deviceBackDepth);
        
        backDepthValid = backDepthValid && (sceneBackDepth > sceneDepth + marchingThickness);
        float backDepthDiff;
        if (backDepthValid)
            backDepthDiff = hitDepth - sceneBackDepth;
        else
            backDepthDiff = depthDiff - marchingThickness;
#endif
        // Sign is positive : ray is in front of the actual intersection.
        // Sign is negative : ray is behind the actual intersection.
        half Sign;
    #if defined(_BACKFACE_ENABLED)
        if (hitDepth > sceneBackDepth && backDepthValid)
            Sign = FastSign(backDepthDiff);
        else
            Sign = FastSign(depthDiff);
    #else
        Sign = FastSign(depthDiff);
    #endif
        startBinarySearch = startBinarySearch || (Sign == -1) ? true : false; // Start binary search when the ray is behind the actual intersection.

        // Half the step size each time when binary search starts.
        // If the ray passes through the intersection, we flip the sign of step size.
        if (startBinarySearch && FastSign(stepSize) != Sign)
        {
            stepSize = stepSize * Sign * 0.5;
            marchingThickness = marchingThickness * 0.5;
        }

        bool isSky;
    #if (UNITY_REVERSED_Z == 1)
        isSky = deviceDepth == 0.0 ? true : false;
    #else
        isSky = deviceDepth == 1.0 ? true : false; // OpenGL Platforms.
    #endif

        bool hitSuccessful;
    #if defined(_BACKFACE_ENABLED)
        if (backDepthValid)
            hitSuccessful = (depthDiff <= 0.0 && (hitDepth <= sceneBackDepth) && !isSky) ? true : false;
        else
            hitSuccessful = (depthDiff <= 0.0 && (depthDiff >= -marchingThickness) && !isSky) ? true : false;
    #else
        hitSuccessful = (depthDiff <= 0.0 && (depthDiff >= -marchingThickness) && !isSky) ? true : false;
    #endif
        
        if (hitSuccessful)
        {
            rayHit.distance = length(rayPositionWS - ray.position);
            // Position is unused because we don't support multiple bounced reflection yet.
            //rayHit.position = rayPositionWS;
            rayHit.uv = rayPositionNDC.xy;
            break;

            // This is unused because we don't support multiple bounced reflection yet.
            /*
            // Lerp the world space position according to depth difference.
            // From https://baddogzz.github.io/2020/03/06/Accurate-Hit/
            // 
            // x: position from last marching
            // y: current ray marching position (successfully hit the scene)
            //           |
            // Cam->--x--|-y
            //           |
            // Using the position between "x" and "y" is more accurate than using "y" directly.
            if (Sign != FastSign(lastDepthDiff))
            {
                // Seems that interpolating screenUV is giving worse results, so do it for positionWS only.
                //rayPositionNDC.xy = lerp(lastRayPositionNDC, rayPositionNDC.xy, lastDepthDiff * rcp(lastDepthDiff - depthDiff));
                rayHit.position = lerp(lastRayPositionWS, rayHit.position, lastDepthDiff * rcp(lastDepthDiff - depthDiff));
                rayHit.uv = rayPositionNDC.xy;
            }
            HitSurfaceDataFromGBuffers(rayPositionNDC.xy, rayHit.albedo, rayHit.specular, rayHit.occlusion, rayHit.normal, rayHit.smoothness);
            break;
            */
        }
        // [Optimization] Exponentially increase the stepSize when the ray hasn't passed through the intersection.
        // From https://blog.voxagon.se/2018/01/03/screen-space-path-tracing-diffuse.html
        // The "1.33" is the exponential constant, which should be above "1.0".
        else if (!startBinarySearch)
        {
            // As the distance increases, the accuracy of ray intersection test becomes less important.
            stepSize = stepSize < STEP_SIZE ? stepSize * _StepSizeMultiplier + STEP_SIZE * _StepSizeMultiplier * 0.1 : stepSize * _StepSizeMultiplier;
            marchingThickness = marchingThickness * _StepSizeMultiplier;
        }
        // Update last step's depth difference.
        //lastDepthDiff = depthDiff;
        //lastRayPositionNDC = rayPositionNDC.xy;
        //lastRayPositionWS = rayPositionWS.xyz;
    }
    return rayHit;
}

// Performs fading at the edge of the screen. 
float EdgeOfScreenFade(float2 screenUV)
{
    UNITY_BRANCH
    if (_EdgeFade == 0.0)
    {
        return 1.0;
    }
    else
    {
        half fadeRcpLength = rcp(_EdgeFade);
        float2 coordCS = screenUV * 2.0 - 1.0;
        float2 t = Remap10(abs(coordCS.xy), fadeRcpLength, fadeRcpLength);
        return Smoothstep01(t.x) * Smoothstep01(t.y);
    }
}

#endif // URP_SCREEN_SPACE_REFLECTION_HLSL