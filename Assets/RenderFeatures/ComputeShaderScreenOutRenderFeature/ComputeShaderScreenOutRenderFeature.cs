using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

// This RendererFeature shows how a compute shader can be used together with RenderGraph and how use the output 
// in the cameraColor

// This sample is based on this video https://www.youtube.com/watch?v=v_WkGKn601M by git-amend
// In the original sample the output image of the compute shader is applied to a RenderTexture
// In this particular case, the is only a little chance in the line 91

public class ComputeShaderScreenOutRenderFeature : ScriptableRendererFeature {
    class HeatmapPass : ScriptableRenderPass {
        // Compute Shader
        ComputeShader computeShader;
        int kernel;

        // Compute Shader data
        GraphicsBuffer enemyBuffer;
        Vector2[] enemyPositions;
        int enemyCount = 64;
        
        // RT handles intended for later use by the render graph.
        RTHandle heatmapHandle;
        
        // Screen resolution
        int width = Screen.width, height = Screen.height;

        public void Setup(ComputeShader cs) {
            // Here are define the compute shader
            computeShader = cs;
            kernel = cs.FindKernel("CSMain");

            if (heatmapHandle == null || heatmapHandle.rt.width != width || heatmapHandle.rt.height != height) {
                heatmapHandle?.Release();
                var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Default, 0) {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    sRGB = false,
                    useMipMap = false
                };
                heatmapHandle = RTHandles.Alloc(desc, name: "_HeatmapRT");
            }

            if (enemyBuffer == null || enemyBuffer.count != enemyCount) {
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
        
        // This is the core of the RenderGraph system, where compute pass is executed every frame.
        // Their purpose can be summarized in three steps:
        
        // 1- Updates enemy positions using Perlin noise, then uploads them to a GPU buffer.
        // 2- Sets up a compute pass in the render graph that generates a heatmap texture
        //    based on enemy positions.
        // 3- Assigns the resulting texture to the camera's color buffer for rendering.
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer context) {
            
            // Populated the enemy positions
            for (int i = 0; i < enemyCount; i++) {
                float t = Time.time * 0.5f + i * 0.1f;
                float x = Mathf.PerlinNoise(t, i * 1.31f) * width;
                float y = Mathf.PerlinNoise(i * 0.91f, t) * height;
                enemyPositions[i] = new Vector2(x, y);
            }
            
            // Set the enemy buffer data
            enemyBuffer.SetData(enemyPositions);

            // The texture handle and the buffer handle for the compute pass
            TextureHandle texHandle = graph.ImportTexture(heatmapHandle);
            BufferHandle enemyHandle = graph.ImportBuffer(enemyBuffer);

            // This is the definition of the compute render pass,
            // where the data to be processed by the compute shader is assigned.
            using (var builder = graph.AddComputePass<ComputePassData>("ComputePass01", out var data))
            {
                // Assign data to the compute shader data
                data.compute = computeShader;
                data.kernel = kernel;
                data.output = texHandle;
                data.enemyHandle = enemyHandle;
                data.enemyCount = enemyCount;

                // Declare resource usage
                builder.UseTexture(texHandle, AccessFlags.Write);
                builder.UseBuffer(enemyHandle, AccessFlags.Read);

                // Set the function to execute the compute pass
                builder.SetRenderFunc((ComputePassData d, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeIntParam(d.compute, "enemyCount", d.enemyCount);
                    ctx.cmd.SetComputeBufferParam(d.compute, d.kernel, "enemyPositions", d.enemyHandle);
                    ctx.cmd.SetComputeTextureParam(d.compute, d.kernel, "heatmapTexture", d.output);
                    ctx.cmd.DispatchCompute(d.compute, d.kernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                });
            }
            
            
            // Here we get the ResourceData
            // and assign to the cameraColor the texHandle
            var resourceData = context.Get<UniversalResourceData>();
            resourceData.cameraColor = texHandle;
        }


        public void Cleanup() {
            heatmapHandle?.Release();
            heatmapHandle = null;

            enemyBuffer?.Release();
            enemyBuffer = null;
        }
    }

    [SerializeField] ComputeShader computeShader;
    HeatmapPass pass;

    public override void Create() {
        pass = new HeatmapPass {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (!SystemInfo.supportsComputeShaders)
            return;

        pass.Setup(computeShader);
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(pass);
        }
    }

    protected override void Dispose(bool disposing) {
        pass?.Cleanup();
    }
}
