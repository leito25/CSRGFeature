using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

// This RendererFeature demonstrates how to use a compute shader with RenderGraph 
// and how to apply the compute shader's output to the cameraColor.

// This sample is based on this video https://www.youtube.com/watch?v=v_WkGKn601M by git-amend
// In the original sample the output image of the compute shader is applied to a RenderTexture
// In this particular case, there is only a little change in the "InComputePass".

public class ComputeShaderScreenOutRenderFeature : ScriptableRendererFeature 
{
    class HeatmapPass : ScriptableRenderPass 
    {
        // Compute Shader
        ComputeShader m_HeatmapComputeShader;
        int m_Kernel;

        // Compute Shader data
        GraphicsBuffer enemyBuffer;
        Vector2[] enemyPositions;
        const int enemyCount = 64;
        
        // RT handles intended for later use by the render graph.
        RTHandle heatmapTextureHandle;
        
        // Screen resolution
        int width = Screen.width, height = Screen.height;

        public void Setup(ComputeShader heatmapCS) 
        {
            m_HeatmapComputeShader = heatmapCS;
            m_Kernel = heatmapCS.FindKernel("CSMain");

            if (heatmapTextureHandle == null || heatmapTextureHandle.rt.width != width || heatmapTextureHandle.rt.height != height) 
            {
                heatmapTextureHandle?.Release();
                var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Default, 0) {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    sRGB = false,
                    useMipMap = false
                };
                heatmapTextureHandle = RTHandles.Alloc(desc, name: "_HeatmapRT");
            }

            if (enemyBuffer == null || enemyBuffer.count != enemyCount) 
            {
                enemyBuffer?.Release();
                enemyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, sizeof(float) * 2);
                enemyPositions = new Vector2[enemyCount];
            }
        }
        
        // Compute Pass Data 
        class ComputePassData {
            public ComputeShader compute;
            public int kernel;
            public TextureHandle output;
            public BufferHandle enemyHandle;
            public int enemyCount;
        }
        
        // This is the core of the RenderGraph system, where the compute pass is executed every frame.
        // The purpose of the compute pass can be summarized in three steps:
        
        // 1- Update enemy positions using Perlin noise, then upload them to a GPU buffer.
        // 2- Set up a compute pass in the render graph that generates a heatmap texture
        //    based on enemy positions.
        // 3- Assign the resulting texture to the camera's color buffer for rendering.
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer context) 
        {
            // Update the enemy positions
            for (int i = 0; i < enemyCount; i++) {
                float t = Time.time * 0.5f + i * 0.1f;
                float x = Mathf.PerlinNoise(t, i * 1.31f) * width;
                float y = Mathf.PerlinNoise(i * 0.91f, t) * height;
                enemyPositions[i] = new Vector2(x, y);
            }
            
            // Set the enemy buffer data
            enemyBuffer.SetData(enemyPositions);

            // The texture handle and the buffer handle for the compute pass
            TextureHandle heatmapHandle = graph.ImportTexture(heatmapTextureHandle);
            BufferHandle enemyHandle = graph.ImportBuffer(enemyBuffer);

            // This is the definition of the compute render pass,
            // where the data to be processed by the compute shader is assigned.
            using (var builder = graph.AddComputePass<ComputePassData>("ComputeHeatmapPass", out var passData))
            {
                // Assign data to the compute shader data
                passData.compute = m_HeatmapComputeShader;
                passData.kernel = m_Kernel;
                passData.output = heatmapHandle;
                passData.enemyHandle = enemyHandle;
                passData.enemyCount = enemyCount;

                // Declare resource usage
                builder.UseTexture(heatmapHandle, AccessFlags.Write);
                builder.UseBuffer(enemyHandle, AccessFlags.Read);

                // Set the function to execute the compute pass
                builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeIntParam(data.compute, "enemyCount", data.enemyCount);
                    ctx.cmd.SetComputeBufferParam(data.compute, data.kernel, "enemyPositions", data.enemyHandle);
                    ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, "heatmapTexture", data.output);
                    ctx.cmd.DispatchCompute(data.compute, data.kernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                });
            }
            // Here we get the ResourceData
            // and assign to the cameraColor the heatmapHandle
            var resourceData = context.Get<UniversalResourceData>();
            resourceData.cameraColor = heatmapHandle;
        }

        public void Cleanup() 
        {
            heatmapTextureHandle?.Release();
            heatmapTextureHandle = null;

            enemyBuffer?.Release();
            enemyBuffer = null;
        }
    }

    [SerializeField] ComputeShader HeatmapComputeShader;
    HeatmapPass pass;

    public override void Create() 
    {
        pass = new HeatmapPass {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) 
    {
        if (!SystemInfo.supportsComputeShaders)
            return;
        
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            pass.Setup(HeatmapComputeShader);
            renderer.EnqueuePass(pass);
        }
    }

    protected override void Dispose(bool disposing) 
    {
        pass?.Cleanup();
    }
}
