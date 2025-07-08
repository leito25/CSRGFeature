using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 1 step custom render pass
// The blur settings
// The are two passes on the blur custom shader
// one pass do the horiz blur and the other one the vertical blur
[Serializable]
public class BlurSettings
{
    [Range(0, 0.4f)] public float horizontalBlur;
    [Range(0, 0.4f)] public float verticalBlur;
}

public class BlurRendererFeature : ScriptableRendererFeature
{
    // 2 step Custom RenderPass
    // Fields for getting the proper resources
    [SerializeField] private BlurSettings settings;
    [SerializeField] private Shader shader;
    private Material material;
    private BlurRenderPass blurRenderPass;





    //When the Renderer Feature loads the first time.
    //When you enable or disable the Renderer Feature.
    //When you change a property in the inspector of the Renderer Feature.
    public override void Create()
    {
        // IMPLEMENTING THE RENDER PASS INTO THE RENDER FEATURE
        // 1 Add the render pass in the create method of the custom render feature
        if (shader == null)
        {
            return;
        }

        material = new Material(shader);//material based on the shader
        blurRenderPass = new BlurRenderPass(material, settings);

        blurRenderPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
    }

    
    //Unity calls this method every frame, once for each camera.
    //This method lets you inject ScriptableRenderPass instances into the scriptable Renderer
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // IMPLEMENTING THE RENDER PASS INTO THE RENDER FEATURE
        // 2 Enqueue the render pass using the Enqueue pass method
        if(blurRenderPass == null)
        {  return; }
        if(renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(blurRenderPass);
        }
    }

    // IMPLEMENTING THE RENDER PASS INTO THE RENDER FEATURE
    // 3 Implementing the dispose method
    protected override void Dispose(bool disposing)
    {
        if (Application.isPlaying)
        {
            Destroy(material);
        }
        else
        {
            DestroyImmediate(material);
        }
    }
}