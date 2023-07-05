using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

#if UNITY_2023_1_OR_NEWER
[Serializable, VolumeComponentMenu("Lighting/Screen Space Reflection (URP)"), SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
#else
[Serializable, VolumeComponentMenuForRenderPipeline("Lighting/Screen Space Reflection (URP)", typeof(UniversalRenderPipeline))]
#endif
public class ScreenSpaceReflection : VolumeComponent, IPostProcessComponent
{
    [InspectorName("State (Opaque)"), Tooltip("When set to Enabled, URP processes SSR on opaque objects for Cameras in the influence of this effect's Volume.")]
    public SSRStateParameter state = new(value: State.Disabled, overrideState: true);
    [Tooltip("Specifies the algorithm to use for screen space reflection.")]
    public SSRAlgorithmParameter algorithm = new(value: Algorithm.Approximation, overrideState: false);
    [InspectorName("Minimum Smoothness"), Tooltip("SSR ignores a pixel if its smoothness value is lower than this value.")]
    public ClampedFloatParameter minSmoothness = new(value: 0.4f, min: 0.0f, max: 1.0f, overrideState: false);
    [InspectorName("Smoothness Fade Start"), Tooltip("Use the slider to set the smoothness value at which SSR reflections begin to fade out.")]
    public ClampedFloatParameter fadeSmoothness = new(value: 0.6f, min: 0.0f, max: 1.0f, overrideState: false);
    [InspectorName("Screen Edge Fade Distance"), Tooltip("Fades out screen space reflection when it is near the screen boundaries.")]
    public ClampedFloatParameter edgeFade = new(value: 0.1f, min: 0.0f, max: 1.0f, overrideState: true);
    [Tooltip("The thickness mode of screen space reflection.")]
    public SSRThicknessParameter thicknessMode = new(value: ThicknessMode.Constant, overrideState: false);
    [InspectorName("Object Thickness"), Tooltip("The thickness of all scene objects. This is also the fallback thickness for automatic thickness mode.")]
    public ClampedFloatParameter thickness = new(value: 0.25f, min: 0.0f, max: 1.0f, overrideState: true);
    [Tooltip("The quality of ray marching. The custom mode provides the best quality.")]
    public SSRQualityParameter quality = new(value: Quality.Low, overrideState: false);
    [InspectorName("Max Ray Steps"), Tooltip("The maximum ray steps for custom quality mode.")]
    public ClampedIntParameter maxStep = new(value: 16, min: 4, max: 128, overrideState: false);
    [InspectorName("Accumulation Factor"), Tooltip("The speed of accumulation convergence for PBR Accumulation mode. Does not work properly with distortion post-processing effects due to URP limitations.")]
    public ClampedFloatParameter accumFactor = new(value: 0.75f, min: 0.0f, max: 1.0f, overrideState: false);

    public bool IsActive()
    {
        return state.value == State.Enabled && SystemInfo.supportedRenderTargetCount >= 3;
    }

    // This is unused since 2023.1
    public bool IsTileCompatible() => false;

    public enum State
    {
        [Tooltip("Disable URP screen space reflection.")]
        Disabled = 0,
        [Tooltip("Enable URP screen space reflection.")]
        Enabled = 1
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="State"/> value.
    /// </summary>
    [Serializable]
    public sealed class SSRStateParameter : VolumeParameter<State>
    {
        /// <summary>
        /// Creates a new <see cref="SSRStateParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public SSRStateParameter(State value, bool overrideState = false) : base(value, overrideState) { }
    }

    public enum Algorithm
    {
        [Tooltip("Cast rays in deterministic directions to compute reflections.")]
        Approximation = 0,

        [InspectorName("PBR Accumulation"), Tooltip("Cast rays in stochastic directions and accumulate multiple frames to compute rough reflections.")]
        PBRAccumulation = 1
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="Algorithm"/> value.
    /// </summary>
    [Serializable]
    public sealed class SSRAlgorithmParameter : VolumeParameter<Algorithm>
    {
        /// <summary>
        /// Creates a new <see cref="SSRAlgorithmParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public SSRAlgorithmParameter(Algorithm value, bool overrideState = false) : base(value, overrideState) { }
    }

    public enum ThicknessMode
    {
        [Tooltip("Apply constant thickness to every scene object.")]
        Constant = 0,

        [InspectorName("Automatic"), Tooltip("Automatic mode renders the back-faces of scene objects to compute thickness.")]
        ComputeBackface = 1
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="ThicknessMode"/> value.
    /// </summary>
    [Serializable]
    public sealed class SSRThicknessParameter : VolumeParameter<ThicknessMode>
    {
        /// <summary>
        /// Creates a new <see cref="SSRThicknessParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public SSRThicknessParameter(ThicknessMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    public enum Quality
    {
        [Tooltip("Low quality mode with 16 ray steps.")]
        Low = 0,
        [Tooltip("Medium quality mode with 32 ray steps.")]
        Medium = 1,
        [Tooltip("High quality mode with 64 ray steps.")]
        High = 2,
        [Tooltip("Custom quality mode with 16 ray steps by default.")]
        Custom = 3
    }

    /// <summary>
    /// A <see cref="VolumeParameter"/> that holds a <see cref="Quality"/> value.
    /// </summary>
    [Serializable]
    public sealed class SSRQualityParameter : VolumeParameter<Quality>
    {
        /// <summary>
        /// Creates a new <see cref="SSRQualityParameter"/> instance.
        /// </summary>
        /// <param name="value">The initial value to store in the parameter.</param>
        /// <param name="overrideState">The initial override state for the parameter.</param>
        public SSRQualityParameter(Quality value, bool overrideState = false) : base(value, overrideState) { }
    }
}

