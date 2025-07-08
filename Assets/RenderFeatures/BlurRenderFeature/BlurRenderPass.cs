using NUnit.Framework;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;


public class BlurRenderPass : ScriptableRenderPass
{
    // Step 6 Declare variables to interact with the shader
    private static readonly int horizontalBlurId = Shader.PropertyToID("_HorizontalBlur");
    private static readonly int verticalBlurId = Shader.PropertyToID("_VerticalBlur");
    private const string k_BlurTextureName = "_BlurTexture";
    private const string k_VerticalPassName = "VerticalBlurRenderPass";
    private const string k_HorizontalPassName = "HorizontalBlurRenderPass";


    // Step 3 custom render pass
    // Add the settings field
    // Add the material
    // Add a contructor for the custom pass
    private BlurSettings defaultSettings;
    private Material material;

    //4. RenderTextureDescriptor field
    private RenderTextureDescriptor blurTextureDescriptor;

    public BlurRenderPass(Material material, BlurSettings defaultSettings)
    {
        this.material = material;
        this.defaultSettings = defaultSettings;

        // 4. RenderTextureDescriptor init
        // setting propeties of the rendertarget
        blurTextureDescriptor = new RenderTextureDescriptor(Screen.width, Screen.height, RenderTextureFormat.Default, 0);
        //blurTextureDescriptor.dimension = TextureDimension.Cube;//ExtraCode

        
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        // 5 Variable for Storing the UniversalResourceData from frameData
        // UniversalResourceData contains all the texture references used by URP,
        // including the active color and depth textures of the camera.
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        // 7 textureHandle field to store the references to thr in/out textures
        TextureHandle srcCamColor = resourceData.activeColorTexture;
        TextureHandle dst = UniversalRenderer.CreateRenderGraphTexture(renderGraph, blurTextureDescriptor, k_BlurTextureName, false);

        // 9 adding the variable for storing the UniversalCameraData
        // setting the RenderTextureDecriptor values using that data
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        //--
        // The following line ensures that the render pass doesn't blit   //this code not blit from the backbuffer/may creates a loop or black image
        // from the back buffer.
        if (resourceData.isActiveTargetBackBuffer)//TEST
            return;
        //--

        // /Set the blur texture size to be the same as the camera target size
        // Line 37 Screen.width
        blurTextureDescriptor.width = cameraData.cameraTargetDescriptor.width;
        blurTextureDescriptor.height = cameraData.cameraTargetDescriptor.height;
        blurTextureDescriptor.depthBufferBits = 0;

        // Update the blur settings in the material
        // It seems this methos the is executed averyframe
        UpdateBlurSettings();

        //This check is to avoid an error fromo material preview in the scene
        if(!srcCamColor.IsValid() || !dst.IsValid())
            return;


        // IMPLEMENTING THE RENDER PASSES
        // The AddBlitPass method adds a vertical blur render graph pass that blits from the source texture
        // (camera color in this case) to the destination texture using the first shadder pass
        // (the shader pass is defined in the last paramenter).
        RenderGraphUtils.BlitMaterialParameters paraVertical = new(srcCamColor, dst, material, 0);
        renderGraph.AddBlitPass(paraVertical, k_VerticalPassName);

        // The AddBlitPass method adds a horizontal blur render graph pass that blits from the texture written by the vertical blur pass to the camera color texture. The method uses the second shader pass.
        RenderGraphUtils.BlitMaterialParameters paraHorizontal = new(dst, srcCamColor, material, 1);//the last parameter seems to be the pass
        renderGraph.AddBlitPass(paraHorizontal, k_HorizontalPassName);
    }

    // 8 implementing the UpdateBlurSettings gettint the data from
    //  a VolumeComponent // VolumeComponent already created on other file 
    // CustomVolumeComponent.cs
    private void UpdateBlurSettings()
    {
        if (material == null) return;

        // Use the Volume settings or the default settings if no Volume is set.
        var volumeComponent = VolumeManager.instance.stack.GetComponent<CustomVolumeComponent>();
        // set the overriden state if change or left the value by default
        float horizontalBlur = volumeComponent.horizontalBlur.overrideState ? volumeComponent.horizontalBlur.value : defaultSettings.horizontalBlur;
        float verticalBlur = volumeComponent.verticalBlur.overrideState ? volumeComponent.verticalBlur.value : defaultSettings.verticalBlur;
        material.SetFloat(horizontalBlurId, horizontalBlur);
        material.SetFloat(verticalBlurId, verticalBlur);
    }
}
