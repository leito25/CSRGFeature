using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Playables;

// This RendererFeature demonstrates how to integrate a Compute Shader with RenderGraph.
// In this example, the output of the Compute Shader is used to modify the CameraColor texture.
// Additionally, once the CameraColor texture is updated, it is used as input for another Compute Shader pass.

// This sample is based on this video https://www.youtube.com/watch?v=v_WkGKn601M by git-amend
// In the original sample the output image of the compute shader is applied to a RenderTexture instead of
// to the CameraColor texture.

public class ComputeShaderScreenInOutRenderFeature : ScriptableRendererFeature {
    class HeatmapPass : ScriptableRenderPass {
        // Compute Shader scripts
        ComputeShader computeShader01;
        ComputeShader computeShader02;
        
        // kernel of each compute shader
        int kernelComputeShader01;
        int kernelComputeShader02;

        // Heatmap compute shader (uses a compute shader to simulated a group of enemies moving around)
        GraphicsBuffer enemyBuffer;
        Vector2[] enemyPositions;
        int enemyCount = 64;

        // RT handles intended for later use by the render graph.
        RTHandle heatmapHandle01;
        RTHandle heatmapHandle02;
        
        // Screen resolution
        int width = Screen.width, height = Screen.height;

        public void Setup(ComputeShader cs1, ComputeShader cs2) {
            
            // Here are define the both Compute shader
            // The output of the first one will be used as CameraColor 
            // and the second one uses the current CameraColor as input to process
            // another one as result.
            computeShader01 = cs1;
            computeShader02 = cs2;
            kernelComputeShader01 = cs1.FindKernel("CSMain");
            kernelComputeShader02 = cs2.FindKernel("CSMain");
            
            
            if (heatmapHandle01 == null || heatmapHandle01.rt.width != width || heatmapHandle01.rt.height != height) {
                heatmapHandle01?.Release();
                var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Default, 0) {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    sRGB = false,
                    useMipMap = false
                };
                heatmapHandle01 = RTHandles.Alloc(desc, name: "_HeatmapRT01");
            }
            if (heatmapHandle02 == null || heatmapHandle02.rt.width != width || heatmapHandle02.rt.height != height)
            {
                heatmapHandle02?.Release();
                var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Default, 0)
                {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    sRGB = false,
                    useMipMap = false
                };
                heatmapHandle02 = RTHandles.Alloc(desc, name: "_HeatmapRT02");
            }

            if (enemyBuffer == null || enemyBuffer.count != enemyCount) {
                enemyBuffer?.Release();
                enemyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, sizeof(float) * 2);
                enemyPositions = new Vector2[enemyCount];
            }
        }
        
        
        // Compute Pass Data 
        // This will be used for both Compute Shaders
        class ComputePassData {
            public ComputeShader compute;
            public int kernel;
            public TextureHandle output;
            //public Vector2 texSize;
            public BufferHandle enemyHandle;
            public int enemyCount;
            public TextureHandle input;
        }
        
        // This is the core of the RenderGraph system, where compute passes are executed every frame.
        // Their purpose can be summarized in three steps:
        
        // 1- Updates enemy positions using Perlin noise, then uploads them to a GPU buffer.
        // 2- Sets up two compute passes in the render graph: the first generates a heatmap texture
        //    based on enemy positions, while the second further processes the resulting texture.
        // 3- Assigns the resulting textures from each compute pass to the camera's color buffer for rendering.
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

            // The texture handle and the buffer handle for the first compute pass
            TextureHandle texHandle = graph.ImportTexture(heatmapHandle01);
            BufferHandle enemyHandle = graph.ImportBuffer(enemyBuffer);

            // This is the definition of the compute render pass,
            // where the data to be processed by the compute shader is assigned.
            using (var builder = graph.AddComputePass<ComputePassData>("ComputePass01", out var data))
            {
                // Assign data to the compute shader data
                data.compute = computeShader01;
                data.kernel = kernelComputeShader01;
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

            // Here the texture resulted from the first compute shader
            // is assigned to the camera Color
            var resourceData = context.Get<UniversalResourceData>();
            resourceData.cameraColor = texHandle;
            
            // A new texture is created based on the active Color Texture
            var newTextureHandle = resourceData.activeColorTexture;
            
            // A new compute pass is Built
            TextureHandle texHandle2 = graph.ImportTexture(heatmapHandle02);
            
            // This is the second compute render pass, in this pass
            // the input is the current activeColorTexture and the output
            // after being computed the data, will be the newTextureHandle
            using (var builder = graph.AddComputePass<ComputePassData>("ComputePass02", out var data))
            {
                // Assign data to the compute shader data
                data.compute = computeShader02;
                data.kernel = kernelComputeShader02;
                data.output = texHandle2;
                data.input = newTextureHandle;

                // Declare resource usage
                builder.UseTexture(newTextureHandle, AccessFlags.ReadWrite);
                builder.UseTexture(texHandle2, AccessFlags.Write);

                // Set the function to execute the compute pass
                builder.SetRenderFunc((ComputePassData d, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeTextureParam(d.compute, d.kernel, "heatmapTexture", d.input);
                    ctx.cmd.SetComputeTextureParam(d.compute, d.kernel, "Result", d.output);
                    ctx.cmd.DispatchCompute(d.compute, d.kernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                });
            }
            
            // Then the resulted texture of the compute pass is assigned to the current
            // Camera Color
            resourceData.cameraColor = texHandle2;
        }


        public void Cleanup() {
            heatmapHandle01?.Release();
            heatmapHandle01 = null;

            enemyBuffer?.Release();
            enemyBuffer = null;
        }
    }

    [SerializeField] ComputeShader computeShader01;
    [SerializeField] ComputeShader computeShader02;
    HeatmapPass pass;

    public override void Create() {
        pass = new HeatmapPass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (!SystemInfo.supportsComputeShaders)
            return;

        pass.Setup(computeShader01, computeShader02);
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(pass);
        }
    }

    protected override void Dispose(bool disposing) {
        pass?.Cleanup();
    }
}
