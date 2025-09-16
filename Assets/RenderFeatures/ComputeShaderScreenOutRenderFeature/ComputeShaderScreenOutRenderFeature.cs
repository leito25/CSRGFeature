using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

// This RendererFeature demonstrates how to use a compute shader with RenderGraph 
// and how to apply the compute shader's output to the cameraColor.

// This sample is based on this video https://www.youtube.com/watch?v=v_WkGKn601M by git-amend
// In the original sample the output image of the compute shader is applied to a RenderTexture

public class ComputeShaderScreenOutRenderFeature : ScriptableRendererFeature 
{
    class HeatmapPass : ScriptableRenderPass 
    {
        // Compute Shader
        ComputeShader m_HeatmapComputeShader;
        int m_Kernel;

        // Compute Shader data
        BufferHandle m_EnemyBuffer;
        Vector2[] m_EnemyPositions;
        const int k_EnemyCount = 64;
        
        // Texture Handles intended for later use by the render graph.
        TextureHandle m_heatmapTextureHandle;
        
        // Screen resolution
        int targetWidth = Screen.width, targetHeight = Screen.height;

        public void Setup(ComputeShader heatmapCS) 
        {
            // Compute Shader definition
            m_HeatmapComputeShader = heatmapCS;
            m_Kernel = heatmapCS.FindKernel("CSMain");
        }
        
        // Compute Pass Data 
        class ComputePassData {
            public ComputeShader compute;
            public int kernel;
            public TextureHandle output;
            public BufferHandle enemyHandle;
            public int enemyCount;
            public Vector2[] positions;//This allows us to use the position inside the pass.
            public int width;
            public int height;
        }
        
        // This is the core of the RenderGraph system, where the compute pass is executed every frame.
        // The purpose of the compute pass can be summarized in three steps:
        
        // 1- Update enemy positions using Perlin noise, then upload them to a GPU buffer.
        // 2- Set up a compute pass in the render graph that generates a heatmap texture
        //    based on enemy positions.
        // 3- Assign the resulting texture to the camera's color buffer for rendering.
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
            
            // Getting the activecolorTexture descriptor for later use in the Texture handlers
            var cameraColorDescriptor = resourceData.activeColorTexture.GetDescriptor(graph);

            // Defining some attributes of the descriptor 
            cameraColorDescriptor.name = "HeatmapHandle";
            cameraColorDescriptor.enableRandomWrite = true;
            cameraColorDescriptor.msaaSamples = MSAASamples.None;
            
            // Creating a texture descriptor based on the activeColorTexture's descriptor values.
            // This texture descriptor will be used by both texture handlers:
            // // heatmapTextureHandle and heatmapBrightnessTexture.
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
            // where the data to be processed by the compute shader is assigned.
            using (var builder = graph.AddComputePass<ComputePassData>("ComputeHeatmapPass", out var passData))
            {
                // Assign data to the compute shader data
                passData.compute = m_HeatmapComputeShader;
                passData.kernel = m_Kernel;
                passData.output = m_heatmapTextureHandle;
                passData.enemyHandle = m_EnemyBuffer;
                passData.enemyCount = k_EnemyCount;
                passData.positions = m_EnemyPositions;
                passData.width = targetWidth;
                passData.height = targetHeight;

                // Declare resource usage within this pass using the builder.
                builder.UseTexture(passData.output, AccessFlags.Write);
                builder.UseBuffer(passData.enemyHandle, AccessFlags.Read);

                // Set the function to execute the compute pass
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
            
            // The resulted texture of the compute pass is assigned to the current Camera Color
            resourceData.cameraColor = m_heatmapTextureHandle;
        }
    }

    [SerializeField] ComputeShader HeatmapComputeShader;
    HeatmapPass pass;

    public override void Create() 
    {
        pass = new HeatmapPass 
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) 
    {
        if (HeatmapComputeShader == null)
        {
            Debug.Log("Set the ComputeShader shader for the ComputeShaderRendererFeature.");
            return;
        }
        
        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.Log(
                "The ComputeShaderRendererFeature cannot be added because this system doesn't support compute shaders.");
        }
        
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            pass.Setup(HeatmapComputeShader);
            renderer.EnqueuePass(pass);
        }
    }
}
