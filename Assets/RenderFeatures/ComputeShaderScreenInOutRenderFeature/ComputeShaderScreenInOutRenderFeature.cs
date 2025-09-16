using System.Numerics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using Vector2 = UnityEngine.Vector2;

// This RendererFeature demonstrates how to integrate a Compute Shader with RenderGraph.
// In this example, the output of the Compute Shader is used to modify the CameraColor texture.
// Additionally, once the CameraColor texture is updated, it is used as input for another Compute Shader pass.

// This sample is based on this video https://www.youtube.com/watch?v=v_WkGKn601M by git-amend
// In the original sample the output image of the compute shader is applied to a RenderTexture instead of
// to the CameraColor texture.

public class ComputeShaderScreenInOutRenderFeature : ScriptableRendererFeature 
{
    class HeatmapPass : ScriptableRenderPass 
    {
        // Compute Shader programs
        ComputeShader m_HeatmapComputeShader;
        ComputeShader m_HeatmapBrightnessComputeShader;
        
        // Kernel of each compute shader
        int m_KernelHeatMapComputeShader;
        int m_KernelHeatmapBrightnessComputeShader;

        // Heatmap compute shader (uses a compute shader to simulate a group of enemies moving around)
        BufferHandle m_EnemyBuffer;
        Vector2[] m_EnemyPositions;
        const int k_EnemyCount = 64;

        // Texture Handles intended for later use by the render graph.
        TextureHandle m_heatmapTextureHandle;
        TextureHandle m_heatmapBrightnessTextureHandle;
        
        // Screen resolution
        int targetWidth = Screen.width, targetHeight = Screen.height;

        public void Setup(ComputeShader heatmapCS, ComputeShader heatmapBrightnessCS)
        {
            // Both compute shaders are defined here.
            // The first compute shader generates an output
            // that is stored in the CameraColor.
            // The second compute shader then takes this texture handler
            // as its input, processes it further,
            // and produces the final result.
            m_HeatmapComputeShader = heatmapCS;
            m_HeatmapBrightnessComputeShader = heatmapBrightnessCS;
            m_KernelHeatMapComputeShader = heatmapCS.FindKernel("CSMain");
            m_KernelHeatmapBrightnessComputeShader = heatmapBrightnessCS.FindKernel("CSMain");
        }

        // Compute Pass Data 
        // This will be used for both Compute Shaders
        class ComputePassData 
        {
            public ComputeShader compute;
            public int kernel;
            public int enemyCount;
            public Vector2[] positions;//This allows us to use the position inside the pass.
            public BufferHandle enemyHandle;
            public TextureHandle input;
            public TextureHandle output;
            public int width;
            public int height;
        }
        
        // This is the core of the RenderGraph system, where the compute passes are executed every frame.
        // The purpose of the compute pass can be summarized in three steps:
        
        // 1- Update enemy positions using Perlin noise, then upload them to a GPU buffer.
        // 2- Set up two compute passes in the render graph: the first generates a heatmap texture
        //      based on enemy positions, while the second further processes the resulting texture
        //      adding a bit of brightness with another compute shader.
        // 3- Assign the resulting texture from one compute pass to the next, and finally to the camera's color buffer for rendering.
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer context) 
        {
            // The enemy positions are initialized
            m_EnemyPositions = new Vector2[k_EnemyCount];
            
            // Update the enemy positions
            for (int i = 0; i < k_EnemyCount; i++) {
                float t = Time.time * 0.5f + i * 0.1f;
                float x = Mathf.PerlinNoise(t, i * 1.31f) * targetWidth;
                float y = Mathf.PerlinNoise(i * 0.91f, t) * targetHeight;
                m_EnemyPositions[i] = new Vector2(x, y);
            }
            
            // Retrieving the Universal Resource Data, which contains all texture resources,
            // such as the active color texture, depth texture, and more.
            var resourceData = context.Get<UniversalResourceData>();
            
            // Getting the dimensions from the camera Color
            targetWidth = resourceData.cameraColor.GetDescriptor(graph).width;
            targetHeight = resourceData.cameraColor.GetDescriptor(graph).height;
            
            // Getting the active color Texture descriptor for later use in the Texture handlers
            var cameraColorDescriptor = resourceData.activeColorTexture.GetDescriptor(graph);
            
            // Defining some attributes of the descriptor 
            cameraColorDescriptor.name = "HeatmapHandle";
            cameraColorDescriptor.enableRandomWrite = true;
            cameraColorDescriptor.msaaSamples = MSAASamples.None;
            
            // Creating a texture descriptor based on the activeColorTexture's descriptor values.
            // This texture descriptor will be used by both texture handlers:
            // heatmapTextureHandle and heatmapBrightnessTexture.
            var heatmapDesc = new TextureDesc(targetWidth, targetHeight)
            {
                name = "HeatmapHandleDescriptor",
                width = cameraColorDescriptor.width,
                height = cameraColorDescriptor.height,
                colorFormat = cameraColorDescriptor.colorFormat,
                enableRandomWrite = true // Use this to write to the texture efficiently
                                         // with a compute shader, enabling random tile
                                         // access instead of sequential tile writing.
            };
            
            // Creating the texture for the m_heatmapTextureHandle texture handle based on the camera color descriptor.
            m_heatmapTextureHandle = graph.CreateTexture(cameraColorDescriptor);
            
            // Reusing the previously created heatmapDesc, but this time changing only the name.
            heatmapDesc.name = "BrightnessHandleDescriptor";
            
            // Creating the texture for the m_heatmapBrightnessTextureHandle texture handle based on the camera color descriptor.
            m_heatmapBrightnessTextureHandle = graph.CreateTexture(cameraColorDescriptor);
            
            // Creating the buffer
            var bufferDesc = new BufferDesc()
            {
                name = "EnemyBuffer",
                stride = sizeof(float) * 2,
                count = k_EnemyCount,
                target = GraphicsBuffer.Target.Structured
            };
            
            // Now adding it to the RenderGraph
            m_EnemyBuffer = graph.CreateBuffer(bufferDesc);

            // This is the definition of the compute render pass,
            // where the data to be processed by the compute shader pass is assigned.
            using (var builder = graph.AddComputePass<ComputePassData>("ComputeHeatmapPass", out var passData))
            {
                // Assign data to the compute shader data
                passData.compute = m_HeatmapComputeShader;
                passData.kernel = m_KernelHeatMapComputeShader;
                passData.output = m_heatmapTextureHandle;
                passData.enemyHandle = m_EnemyBuffer;
                passData.enemyCount = k_EnemyCount;
                passData.positions = m_EnemyPositions;
                passData.width = targetWidth;
                passData.height = targetHeight;

                // Declare resource usage within this pass using the builder.
                builder.UseTexture(passData.output, AccessFlags.Write);
                builder.UseBuffer(passData.enemyHandle, AccessFlags.Read);

                // Set the function to execute the compute pass (using static to improve the performance)
                builder.SetRenderFunc(static(ComputePassData data, ComputeGraphContext ctx) =>
                {
                    // the SetBufferData use a command buffer to send the enemy position data
                    // from the passData.enemyHandle to the passData.positions
                    ctx.cmd.SetBufferData(data.enemyHandle, data.positions);//Use data.enemyPositions
                                                                            //to ensure it remains scoped to the render function.
                    ctx.cmd.SetComputeIntParam(data.compute, "k_EnemyCount", data.enemyCount);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, "m_EnemyPositions", data.enemyHandle);
                    ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, "heatmapTexture", data.output);
                    ctx.cmd.DispatchCompute(data.compute, data.kernel, Mathf.CeilToInt(data.width / 8f), Mathf.CeilToInt(data.height / 8f), 1);
                });
            }

            // This is the second compute render pass.
            // In this pass, the input is the current `m_heatmapTextureHandle`,
            // and the output, after being processed by the brightness compute shader,
            // will be stored in `m_heatmapBrightnessTextureHandle`.
            using (var builder = graph.AddComputePass<ComputePassData>("ComputeCameraColorFromHeatmapPass", out var passData))
            {
                builder.AllowPassCulling(false); // This option does not delete the last passes.
                
                // Assign data to the compute shader data
                passData.compute = m_HeatmapBrightnessComputeShader;
                passData.kernel = m_KernelHeatmapBrightnessComputeShader;
                passData.input = m_heatmapTextureHandle;
                passData.output = m_heatmapBrightnessTextureHandle;
                passData.width = targetWidth;
                passData.height = targetHeight;

                // Declare resource usage within this pass using the builder.
                builder.UseTexture(passData.input, AccessFlags.Read);
                builder.UseTexture(passData.output, AccessFlags.Write);

                // Set the function to execute the compute pass
                builder.SetRenderFunc(static(ComputePassData data, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, "heatmapTexture", data.input);
                    ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, "result", data.output);
                    ctx.cmd.DispatchCompute(data.compute, data.kernel, Mathf.CeilToInt(data.width / 8f), Mathf.CeilToInt(data.height / 8f), 1);
                });
            }
            
            // The resulted texture of the last compute pass is assigned to the current Camera Color
            resourceData.cameraColor = m_heatmapBrightnessTextureHandle;
        }
    }
    
    // The inspector fields of the Renderer Feature
    [SerializeField] ComputeShader HeatmapComputeShader;
    [SerializeField] ComputeShader HeatmapBrightnessComputeShader;
    // The HeatmapPass instance
    HeatmapPass pass;

    public override void Create() 
    {
        pass = new HeatmapPass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) 
    {
        if (HeatmapComputeShader == null || HeatmapBrightnessComputeShader == null)
        {
            Debug.Log("Set both shaders for the ComputeShaderRendererFeature.");
            return;
        }
        
        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.Log(
                "The ComputeShaderRendererFeature cannot be added because this system doesn't support compute shaders.");
        }

        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            pass.Setup(HeatmapComputeShader, HeatmapBrightnessComputeShader);
            renderer.EnqueuePass(pass);
        }
    }
}
