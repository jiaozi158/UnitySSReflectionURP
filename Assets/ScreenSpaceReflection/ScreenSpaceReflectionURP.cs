using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Experimental.Rendering;
using System.Reflection;

[DisallowMultipleRendererFeature("Screen Space Reflection URP")]
[Tooltip("Add this Renderer Feature to support screen space reflection in URP Volume.")]
public class ScreenSpaceReflectionURP : ScriptableRendererFeature
{
    public enum Resolution
    {
        [InspectorName("100%")]
        [Tooltip("Do ray marching at 100% resolution.")]
        Full = 4,

        [InspectorName("75%")]
        [Tooltip("Do ray marching at 75% resolution.")]
        ThreeQuarters = 3,

        [InspectorName("50%")]
        [Tooltip("Do ray marching at 50% resolution.")]
        Half = 2,

        [InspectorName("25%")]
        [Tooltip("Do ray marching at 25% resolution.")]
        Quarter = 1
    }

    public enum MipmapsMode
    {
        [Tooltip("Disable rough reflections in approximation mode.")]
        None = 0,

        [Tooltip("Use trilinear mipmaps to compute rough reflections in approximation mode.")]
        Trilinear = 1
    }

    [Header("Setup")]
    [Tooltip("The post-processing material of screen space reflection.")]
    public Material material;
    [Tooltip("Enable this to execute SSR in Rendering Debugger view. This is disabled by default to avoid affecting the individual lighting previews.")]
    public bool renderingDebugger = false;
    [Header("Performance")]
    [Tooltip("The resolution of screen space ray marching.")]
    public Resolution resolution = Resolution.Full;
    [Header("Approximation")]
    [Tooltip("Controls how URP compute rough reflections in approximation mode.")]
    public MipmapsMode mipmapsMode = MipmapsMode.Trilinear;
    [Header("PBR Accumulation")]
    [Tooltip("Enable this to denoise SSR at anytime in SceneView. This is disabled by default because URP SceneView only updates motion vectors in play mode.")]
    public bool sceneView = false;

    private const string ssrShaderName = "Hidden/Lighting/ScreenSpaceReflection";
    private ScreenSpaceReflectionPass screenSpaceReflectionPass;
    private BackFaceDepthPass backFaceDepthPass;
    private ForwardGBufferPass forwardGBufferPass;

    // Pirnt message only once when using the rendering debugger.
    private bool isLogPrinted = false;

    // Render GBuffers in Forward path.
    private readonly static FieldInfo renderingModeFieldInfo = typeof(UniversalRenderer).GetField("m_RenderingMode", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly static FieldInfo normalsTextureFieldInfo = typeof(UniversalRenderer).GetField("m_NormalsTexture", BindingFlags.NonPublic | BindingFlags.Instance);

    public Material SSRMaterial
    {
        get { return material; }
        set { material = (value.shader == Shader.Find(ssrShaderName)) ? value : material; }
    }
    public bool RenderingDebugger
    {
        get { return renderingDebugger; }
        set { renderingDebugger = value; }
    }

    public Resolution DownSampling
    {
        get { return resolution; }
        set { resolution = value; }
    }

    public MipmapsMode ColorMipmapsMode
    {
        get { return mipmapsMode; }
        set { mipmapsMode = value; }
    }

    public override void Create()
    {
        // Check if the screen space reflection material uses the correct shader.
        if (material != null)
        {
            if (material.shader != Shader.Find(ssrShaderName))
            {
                Debug.LogErrorFormat("Screen Space Reflection URP: Material shader should be {0}.", ssrShaderName);
                return;
            }
        }
        // No material applied.
        else
        {
            //Debug.LogError("Screen Space Reflection URP: Post-processing material is empty.");
            return;
        }

        if (backFaceDepthPass == null)
        {
            backFaceDepthPass = new(material);
            backFaceDepthPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques - 1;
        }

        if (screenSpaceReflectionPass == null)
        {
            screenSpaceReflectionPass = new(resolution, mipmapsMode, material);
            screenSpaceReflectionPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }
        else
        {
            // Update every frame to support runtime changes to these properties.
            screenSpaceReflectionPass.resolution = resolution;
            screenSpaceReflectionPass.mipmapsMode = mipmapsMode;
        }

        if (forwardGBufferPass == null)
        {
            forwardGBufferPass = new();
            forwardGBufferPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; // Depth Priming
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (screenSpaceReflectionPass != null)
            screenSpaceReflectionPass.Dispose();
        if (backFaceDepthPass != null)
            backFaceDepthPass.Dispose();
        if (forwardGBufferPass != null)
            forwardGBufferPass.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null)
        {
            Debug.LogErrorFormat("Screen Space Reflection URP: Post-processing material is empty.");
            return;
        }

        var renderingMode = (RenderingMode)renderingModeFieldInfo.GetValue(renderer as UniversalRenderer);
        bool isUsingDeferred = (renderingMode != RenderingMode.Forward) && (renderingMode != RenderingMode.ForwardPlus); // URP may have Deferred+ in the future.

        // URP forces Forward path on OpenGL platforms.
        bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore); // GLES 2 is removed.

        var stack = VolumeManager.instance.stack;
        ScreenSpaceReflection ssrVolume = stack.GetComponent<ScreenSpaceReflection>();
        bool isActive = ssrVolume != null && ssrVolume.IsActive();
        bool isDebugger = DebugManager.instance.isAnyDebugUIActive;

        bool isMotionValid = true;
#if UNITY_EDITOR
        // Motion Vectors of URP SceneView don't get updated each frame when not entering play mode. (Might be fixed when supporting scene view anti-aliasing)
        // Change the method to multi-frame accumulation (offline mode) if SceneView is not in play mode.
        isMotionValid = sceneView || UnityEditor.EditorApplication.isPlaying || renderingData.cameraData.camera.cameraType != CameraType.SceneView;
#endif

        if (renderingData.cameraData.camera.cameraType != CameraType.Preview && isActive && (!isDebugger || renderingDebugger))
        {
            if (!isUsingDeferred || isOpenGL) { renderer.EnqueuePass(forwardGBufferPass); }
            backFaceDepthPass.ssrVolume = ssrVolume;
            renderer.EnqueuePass(backFaceDepthPass);
            screenSpaceReflectionPass.isMotionValid = isMotionValid;
            screenSpaceReflectionPass.renderPassEvent = ssrVolume.algorithm == ScreenSpaceReflection.Algorithm.PBRAccumulation ? (ssrVolume.accumFactor.value == 0.0f ? RenderPassEvent.BeforeRenderingPostProcessing : RenderPassEvent.AfterRenderingPostProcessing) : RenderPassEvent.BeforeRenderingTransparents;
            screenSpaceReflectionPass.ssrVolume = ssrVolume;
            renderer.EnqueuePass(screenSpaceReflectionPass);
            isLogPrinted = false;
        }
        else if (isDebugger && isLogPrinted == false)
        {
            Debug.Log("Screen Space Reflection URP: Disable effect to avoid affecting rendering debugging.");
            isLogPrinted = true;
        }
    }

    public class ScreenSpaceReflectionPass : ScriptableRenderPass
    {
        private readonly Material ssrMaterial;
        public Resolution resolution;
        public MipmapsMode mipmapsMode;
        public bool isMotionValid; // URP SceneView doesn't update motion vectors unless in play mode.
        private RTHandle sourceHandle;
        private RTHandle reflectHandle;
        private RTHandle historyHandle;

        public ScreenSpaceReflection ssrVolume;
        private static readonly int minSmoothness = Shader.PropertyToID("_MinSmoothness");
        private static readonly int fadeSmoothness = Shader.PropertyToID("_FadeSmoothness");
        private static readonly int edgeFade = Shader.PropertyToID("_EdgeFade");
        private static readonly int thickness = Shader.PropertyToID("_Thickness");
        private static readonly int stepSize = Shader.PropertyToID("_StepSize");
        private static readonly int stepSizeMultiplier = Shader.PropertyToID("_StepSizeMultiplier");
        private static readonly int maxStep = Shader.PropertyToID("_MaxStep");
        private static readonly int downSample = Shader.PropertyToID("_DownSample");
        private static readonly int accumFactor = Shader.PropertyToID("_AccumulationFactor");

        public ScreenSpaceReflectionPass(Resolution resolution, MipmapsMode mipmapsMode, Material material)
        {
            this.resolution = resolution;
            this.mipmapsMode = mipmapsMode;
            ssrMaterial = material;
        }

        public void Dispose()
        {
            sourceHandle?.Release();
            reflectHandle?.Release();
            historyHandle?.Release();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;

            if (ssrVolume.algorithm == ScreenSpaceReflection.Algorithm.PBRAccumulation)
            {
                RenderTextureDescriptor descHit = desc;
                descHit.width = (int)resolution * (int)(desc.width * 0.25f);
                descHit.height = (int)resolution * (int)(desc.height * 0.25f);
                descHit.colorFormat = RenderTextureFormat.ARGBHalf; // Store "hitUV.xy" + "fresnel.z"
                RenderingUtils.ReAllocateIfNeeded(ref sourceHandle, descHit, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionHitTexture");
                cmd.SetGlobalTexture("_ScreenSpaceReflectionHitTexture", sourceHandle);

                RenderingUtils.ReAllocateIfNeeded(ref historyHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionHistoryTexture");
                cmd.SetGlobalTexture("_ScreenSpaceReflectionHistoryTexture", historyHandle);
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Motion);

                RenderingUtils.ReAllocateIfNeeded(ref reflectHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionColorTexture");
            }
            else
            {
                RenderingUtils.ReAllocateIfNeeded(ref sourceHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionSourceTexture");

                desc.width = (int)resolution * (int)(desc.width * 0.25f);
                desc.height = (int)resolution * (int)(desc.height * 0.25f);
                desc.useMipMap = (mipmapsMode == MipmapsMode.Trilinear);
                //desc.colorFormat = RenderTextureFormat.ARGBHalf; // needs alpha channel to store hit mask.
                FilterMode filterMode = (mipmapsMode == MipmapsMode.Trilinear) ? FilterMode.Trilinear : FilterMode.Point;

                RenderingUtils.ReAllocateIfNeeded(ref reflectHandle, desc, filterMode, TextureWrapMode.Clamp, name: "_ScreenSpaceReflectionColorTexture");

                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            ConfigureTarget(sourceHandle, sourceHandle);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (sourceHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(sourceHandle.name));
            if (reflectHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(reflectHandle.name));
            if (historyHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(historyHandle.name));
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            sourceHandle = null;
            reflectHandle = null;
            historyHandle = null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler("Screen Space Reflection")))
            {
                // Set the parameters here to avoid using 4 shader keywords.
                if (ssrVolume.quality.value == ScreenSpaceReflection.Quality.Low)
                {
                    ssrMaterial.SetFloat(stepSize, 0.4f);
                    ssrMaterial.SetFloat(stepSizeMultiplier, 1.33f);
                    ssrMaterial.SetFloat(maxStep, 16);
                }
                else if (ssrVolume.quality.value == ScreenSpaceReflection.Quality.Medium)
                {
                    ssrMaterial.SetFloat(stepSize, 0.3f);
                    ssrMaterial.SetFloat(stepSizeMultiplier, 1.33f);
                    ssrMaterial.SetFloat(maxStep, 32);
                }
                else if (ssrVolume.quality.value == ScreenSpaceReflection.Quality.High)
                {
                    ssrMaterial.SetFloat(stepSize, 0.2f);
                    ssrMaterial.SetFloat(stepSizeMultiplier, 1.33f);
                    ssrMaterial.SetFloat(maxStep, 64);
                }
                else
                {
                    ssrMaterial.SetFloat(stepSize, 0.2f);
                    ssrMaterial.SetFloat(stepSizeMultiplier, 1.1f);
                    ssrMaterial.SetFloat(maxStep, ssrVolume.maxStep.value);
                }
                ssrMaterial.SetFloat(minSmoothness, ssrVolume.minSmoothness.value);
                ssrMaterial.SetFloat(fadeSmoothness, ssrVolume.fadeSmoothness.value <= ssrVolume.minSmoothness.value ? ssrVolume.minSmoothness.value + 0.01f : ssrVolume.fadeSmoothness.value);
                ssrMaterial.SetFloat(edgeFade, ssrVolume.edgeFade.value); 
                ssrMaterial.SetFloat(thickness, ssrVolume.thickness.value);
                ssrMaterial.SetFloat(downSample, (float)resolution * 0.25f);

                // Blit() may not handle XR rendering correctly.

                bool isPBRAccumulation = ssrVolume.algorithm == ScreenSpaceReflection.Algorithm.PBRAccumulation;
                if (isPBRAccumulation)
                {
                    ssrMaterial.SetFloat(accumFactor, ssrVolume.accumFactor.value);
                    
                    // 5 passes, can we optimize?
                    
                    // Screen Space Hit
                    Blitter.BlitCameraTexture(cmd, sourceHandle, sourceHandle, ssrMaterial, pass: 2);
                    // Resolve Color
                    Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, reflectHandle, ssrMaterial, pass: 3);
                    // Blit to Screen (required by denoiser)
                    Blitter.BlitCameraTexture(cmd, reflectHandle, renderingData.cameraData.renderer.cameraColorTargetHandle);
                    // Temporal Denoise (alpha blend)
                    if (isMotionValid && ssrVolume.accumFactor.value != 0.0f)
                    {
                        Blitter.BlitCameraTexture(cmd, reflectHandle, renderingData.cameraData.renderer.cameraColorTargetHandle, ssrMaterial, pass: 4);

                        // We need to Load & Store the history texture, or it will not be stored on some platforms.
                        cmd.SetRenderTarget(
                        historyHandle,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store,
                        historyHandle,
                        RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.DontCare);
                        // Update History
                        Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, historyHandle);
                    }
                }
                else
                {
                    if (mipmapsMode == MipmapsMode.Trilinear)
                        ssrMaterial.EnableKeyword("_SSR_APPROX_COLOR_MIPMAPS");
                    else
                        ssrMaterial.DisableKeyword("_SSR_APPROX_COLOR_MIPMAPS");
                    
                    // Copy Scene Color
                    Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, sourceHandle);
                    // Screen Space Reflection
                    Blitter.BlitCameraTexture(cmd, sourceHandle, reflectHandle, ssrMaterial, pass: 0);
                    // Combine Color
                    Blitter.BlitCameraTexture(cmd, reflectHandle, renderingData.cameraData.renderer.cameraColorTargetHandle, ssrMaterial, pass: 1);
                }
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }

    public class BackFaceDepthPass : ScriptableRenderPass
    {
        const string profilerTag = "Render Backface Depth";
        private readonly Material ssrMaterial;
        public ScreenSpaceReflection ssrVolume;
        private RTHandle backFaceDepthHandle;

        private RenderStateBlock depthRenderStateBlock = new(RenderStateMask.Nothing);

        public BackFaceDepthPass(Material material)
        {
            ssrMaterial = material;
        }

        public void Dispose()
        {
            backFaceDepthHandle?.Release();
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;

            RenderingUtils.ReAllocateIfNeeded(ref backFaceDepthHandle, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraBackDepthTexture");
            cmd.SetGlobalTexture("_CameraBackDepthTexture", backFaceDepthHandle);
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            if (backFaceDepthHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(backFaceDepthHandle.name));
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            backFaceDepthHandle = null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (ssrVolume.thicknessMode.value == ScreenSpaceReflection.ThicknessMode.ComputeBackface)
            {
                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
                {
                    cmd.SetRenderTarget(
                    backFaceDepthHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.DontCare,
                    backFaceDepthHandle,
                    RenderBufferLoadAction.DontCare,
                    RenderBufferStoreAction.Store);
                    cmd.ClearRenderTarget(clearDepth: true, clearColor: false, Color.clear);

                    RendererListDesc rendererListDesc = new(new ShaderTagId("DepthOnly"), renderingData.cullResults, renderingData.cameraData.camera);
                    depthRenderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                    depthRenderStateBlock.mask |= RenderStateMask.Depth;
                    depthRenderStateBlock.rasterState = new RasterState(CullMode.Front);
                    depthRenderStateBlock.mask |= RenderStateMask.Raster;
                    rendererListDesc.stateBlock = depthRenderStateBlock;
                    rendererListDesc.sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;
                    rendererListDesc.renderQueueRange = RenderQueueRange.opaque;
                    RendererList rendererList = context.CreateRendererList(rendererListDesc);

                    cmd.DrawRendererList(rendererList);

                    ssrMaterial.EnableKeyword("_BACKFACE_ENABLED");
                }
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
            else
                ssrMaterial.DisableKeyword("_BACKFACE_ENABLED");
        }
    }

    public class ForwardGBufferPass : ScriptableRenderPass
    {
        const string profilerTag = "Render Forward GBuffer";

        // Depth Priming.
        private RenderStateBlock renderStateBlock = new(RenderStateMask.Nothing);

        public RTHandle gBuffer0;
        public RTHandle gBuffer1;
        public RTHandle gBuffer2;
        public RTHandle depthHandle;
        private RTHandle[] gBuffers;

        public ForwardGBufferPass()
        {

        }

        public void Dispose()
        {
            gBuffer0?.Release();
            gBuffer1?.Release();
            gBuffer2?.Release();
            depthHandle?.Release();
        }

        // From "URP-Package/Runtime/DeferredLights.cs".
        public GraphicsFormat GetGBufferFormat(int index)
        {
            if (index == 0) // sRGB albedo, materialFlags
                return QualitySettings.activeColorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 1) // sRGB specular, occlusion
                return GraphicsFormat.R8G8B8A8_UNorm;
            else if (index == 2) // normal normal normal packedSmoothness
                // NormalWS range is -1.0 to 1.0, so we need a signed render texture.
                if (SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_SNorm, FormatUsage.Render))
                    return GraphicsFormat.R8G8B8A8_SNorm;
                else
                    return GraphicsFormat.R16G16B16A16_SFloat;
            else
                return GraphicsFormat.None;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // Color and depth cannot be combined in RTHandles
            desc.stencilFormat = GraphicsFormat.None;
            desc.msaaSamples = 1; // Do not enable MSAA for GBuffers.

            // Albedo.rgb + MaterialFlags.a
            desc.graphicsFormat = GetGBufferFormat(0);
            RenderingUtils.ReAllocateIfNeeded(ref gBuffer0, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer0");
            cmd.SetGlobalTexture("_GBuffer0", gBuffer0);

            // Specular.rgb + Occlusion.a
            desc.graphicsFormat = GetGBufferFormat(1);
            RenderingUtils.ReAllocateIfNeeded(ref gBuffer1, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer1");
            cmd.SetGlobalTexture("_GBuffer1", gBuffer1);

            // If "_CameraNormalsTexture" exists (lacking smoothness info), set the target to it instead of creating a new RT.
            if (normalsTextureFieldInfo.GetValue(renderingData.cameraData.renderer) is not RTHandle normalsTextureHandle || renderingData.cameraData.cameraType == CameraType.SceneView) // There're a problem (wrong render target) of reusing normals texture in scene view.
            {
                // NormalWS.rgb + Smoothness.a
                desc.graphicsFormat = GetGBufferFormat(2);
                RenderingUtils.ReAllocateIfNeeded(ref gBuffer2, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffer2");
                cmd.SetGlobalTexture("_GBuffer2", gBuffer2);
                gBuffers = new RTHandle[] { gBuffer0, gBuffer1, gBuffer2 };
            }
            else
            {
                cmd.SetGlobalTexture("_GBuffer2", normalsTextureHandle);
                gBuffers = new RTHandle[] { gBuffer0, gBuffer1, normalsTextureHandle };
            }

            if (renderingData.cameraData.renderer.cameraDepthTargetHandle.isMSAAEnabled)
            {
                RenderTextureDescriptor depthDesc = renderingData.cameraData.cameraTargetDescriptor;
                depthDesc.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref depthHandle, depthDesc, FilterMode.Point, TextureWrapMode.Clamp, name: "_GBuffersDepthTexture");
                ConfigureTarget(gBuffers, depthHandle);
            }
            else
                ConfigureTarget(gBuffers, renderingData.cameraData.renderer.cameraDepthTargetHandle);

            // [OpenGL] Reusing the depth buffer seems to cause black glitching artifacts, so clear the existing depth.
            bool isOpenGL = (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3) || (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore); // GLES 2 is removed.
            if (isOpenGL || renderingData.cameraData.renderer.cameraDepthTargetHandle.isMSAAEnabled)
                ConfigureClear(ClearFlag.Color | ClearFlag.Depth, Color.black);
            else
                // We have to also clear previous color so that the "background" will remain empty (black) when moving the camera.
                ConfigureClear(ClearFlag.Color, Color.clear);

            // Reduce GBuffer overdraw using the depth from opaque pass. (excluding OpenGL platforms)
            if (!isOpenGL && (renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth) && !renderingData.cameraData.renderer.cameraDepthTargetHandle.isMSAAEnabled)
            {
                renderStateBlock.depthState = new DepthState(false, CompareFunction.Equal);
                renderStateBlock.mask |= RenderStateMask.Depth;
            }
            else if (renderStateBlock.depthState.compareFunction == CompareFunction.Equal)
            {
                renderStateBlock.depthState = new DepthState(true, CompareFunction.LessEqual);
                renderStateBlock.mask |= RenderStateMask.Depth;
            }
        }

        public override void FrameCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(Shader.PropertyToID(gBuffer0.name));
            cmd.ReleaseTemporaryRT(Shader.PropertyToID(gBuffer1.name));
            cmd.ReleaseTemporaryRT(Shader.PropertyToID(gBuffer2.name));
            if (depthHandle != null)
                cmd.ReleaseTemporaryRT(Shader.PropertyToID(depthHandle.name));
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            gBuffer0 = null;
            gBuffer1 = null;
            gBuffer2 = null;
            depthHandle = null;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            SortingCriteria sortingCriteria = renderingData.cameraData.defaultOpaqueSortFlags;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, new ProfilingSampler(profilerTag)))
            {
                RendererListDesc rendererListDesc = new(new ShaderTagId("UniversalGBuffer"), renderingData.cullResults, renderingData.cameraData.camera);
                rendererListDesc.stateBlock = renderStateBlock;
                rendererListDesc.sortingCriteria = sortingCriteria;
                rendererListDesc.renderQueueRange = RenderQueueRange.opaque;
                RendererList rendererList = context.CreateRendererList(rendererListDesc);

                cmd.DrawRendererList(rendererList);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }
}
