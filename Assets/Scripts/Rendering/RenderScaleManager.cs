using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Runtime control over URP's actual internal render resolution ("Render Scale") — distinct
// from the OS window/backbuffer size (Screen.SetResolution/Screen.width/height, which this
// never touches). URP renders the scene into a buffer sized at renderScale × the target
// resolution, then upscales that to fill the window — so e.g. 0.5 renders a quarter as many
// pixels per frame (half width × half height) while the window stays exactly the same size.
// This is the single biggest lever for cutting fragment/pixel-shader GPU cost (overdraw,
// per-pixel terrain shading, etc.) without changing anything about layout/UI/window size.
//
// renderScale lives on the URP pipeline ASSET currently in effect, not per-camera — see
// GraphicsSettings.currentRenderPipeline, which resolves whichever asset QualitySettings'
// current quality level points at (falling back to the project's default URP asset if that
// level doesn't override it). Changing it takes effect on the very next frame; no reload/
// restart needed, and in the Editor it's a play-mode-only change to the asset's in-memory
// values — it never gets saved back to the .asset file on disk.
public class RenderScaleManager : Singleton<RenderScaleManager>
{
    public const float MinRenderScale = 0.1f;
    public const float MaxRenderScale = 2.0f;

    void Start()
    {
        // use point filtering
        UniversalRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        asset.upscalingFilter = UpscalingFilterSelection.Point;

        DevConsole.RegisterCommand(
            "renderscale",
            $"Gets or sets URP's internal render resolution scale ({MinRenderScale}-{MaxRenderScale}, 1 = native, lower = fewer pixels rendered). Usage: renderscale [value]",
            (info) =>
            {
                if (info.positionalArgs.Length == 0)
                    return DevCommandResult.Success($"Current render scale: {GetRenderScale():0.00}");

                if (!float.TryParse(info.positionalArgs[0], out float value))
                    return DevCommandResult.Error($"'{info.positionalArgs[0]}' is not a valid number.");

                float applied = SetRenderScale(value);
                return DevCommandResult.Success($"Render scale set to {applied:0.00}.");
            });
    }

    public static float GetRenderScale()
    {
        UniversalRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        return asset != null ? asset.renderScale : 1f;
    }

    // Returns the value actually applied (clamped to Min/MaxRenderScale) so callers can
    // report what really took effect.
    public static float SetRenderScale(float value)
    {
        UniversalRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset == null)
        {
            DebugLogger.LogWarning("[RenderScaleManager] No active UniversalRenderPipelineAsset — is URP actually assigned in Graphics/Quality settings?", "rendering");
            return 1f;
        }

        float clamped = Mathf.Clamp(value, MinRenderScale, MaxRenderScale);
        asset.renderScale = clamped;
        return clamped;
    }
}
