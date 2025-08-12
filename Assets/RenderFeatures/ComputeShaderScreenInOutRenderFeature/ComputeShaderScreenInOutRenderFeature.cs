using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

// This RendererFeature demonstrates how to integrate a Compute Shader with RenderGraph.
// In this example, the output of the Compute Shader is used to modify the CameraColor texture.
// Additionally, once the CameraColor texture is updated, it is used as input for another Compute Shader pass.

// This sample is based on this video https://www.youtube.com/watch?v=v_WkGKn601M by git-amend
// In the original sample the output image of the compute shader is applied to a RenderTexture instead of
// to the CameraColor texture.

public class ComputeShaderScreenInOutRenderFeature : ScriptableRendererFeature {
    class HeatmapPass : ScriptableRenderPass {
        
        // Compute Shader programs
        ComputeShader HeatmapComputeShader;
        ComputeShader HeatmapBrightnessComputeShader;
        
        // kernel of each compute shader
        int kernelHeatMapComputeShader;
        int kernelHeatmapBrightnessComputeShader;

        // Heatmap compute shader (uses a compute shader to simulated a group of enemies moving around)
        GraphicsBuffer enemyBuffer;
        Vector2[] enemyPositions;
        const int enemyCount = 64;

        // RT handles intended for later use by the render graph.
        RTHandle heatmapTextureHandle;
        RTHandle heatmapBrightnessTextureHandle;
        
        // Screen resolution
        int width = Screen.width, height = Screen.height;

        public void Setup(ComputeShader cs1, ComputeShader cs2) {
            
            // Both compute shaders are defined here.
            // The first compute shader generates an output
            // that is stored in the CameraColor.
            // The second compute shader then takes this CameraColor
            // as its input, processes it further,
            // and produces the final result.
            HeatmapComputeShader = cs1;
            HeatmapBrightnessComputeShader = cs2;
            kernelHeatMapComputeShader = cs1.FindKernel("CSMain");
            kernelHeatmapBrightnessComputeShader = cs2.FindKernel("CSMain");
            
            
            if (heatmapTextureHandle == null || heatmapTextureHandle.rt.width != width || heatmapTextureHandle.rt.height != height) {
                heatmapTextureHandle?.Release();
                var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Default, 0) {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    sRGB = false,
                    useMipMap = false
                };
                heatmapTextureHandle = RTHandles.Alloc(desc, name: "_HeatmapRT01");
            }
            if (heatmapBrightnessTextureHandle == null || heatmapBrightnessTextureHandle.rt.width != width || heatmapBrightnessTextureHandle.rt.height != height)
            {
                heatmapBrightnessTextureHandle?.Release();
                var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Default, 0)
                {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    sRGB = false,
                    useMipMap = false
                };
                heatmapBrightnessTextureHandle = RTHandles.Alloc(desc, name: "_HeatmapRT02");
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
            public int enemyCount;
            public BufferHandle enemyHandle;
            public TextureHandle input;
            public TextureHandle output;
        }
        
        // This is the core of the RenderGraph system, where compute passes are executed every frame.
        // Their purpose can be summarized in three steps:
        
        // 1- Updates enemy positions using Perlin noise, then uploads them to a GPU buffer.
        // 2- Sets up two compute passes in the render graph: the first generates a heatmap texture
        //      based on enemy positions, while the second further processes the resulting texture
        //      adding a bit of brightness with another compute shader.
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
            TextureHandle texHandle = graph.ImportTexture(heatmapTextureHandle);
            BufferHandle enemyHandle = graph.ImportBuffer(enemyBuffer);

            // This is the definition of the compute render pass,
            // where the data to be processed by the compute shader is assigned.
            using (var builder = graph.AddComputePass<ComputePassData>("ComputeHeatmapPass", out var data))
            {
                // Assign data to the compute shader data
                data.compute = HeatmapComputeShader;
                data.kernel = kernelHeatMapComputeShader;
                data.output = texHandle;
                data.enemyHandle = enemyHandle;
                data.enemyCount = enemyCount;

                // Declare resource usage
                builder.UseTexture(texHandle, AccessFlags.Write);
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

            // Here the texture resulted from the first compute shader
            // is assigned to the camera Color
            var resourceData = context.Get<UniversalResourceData>();
            resourceData.cameraColor = texHandle;

            // A new compute pass will be build and this is the resulted Texture handle
            TextureHandle resultedTextureHandle = graph.ImportTexture(heatmapBrightnessTextureHandle);
            
            // This is the second compute render pass, in this pass
            // the input is the current activeColorTexture and the output
            // after being computed the data, will be the resulted texture handle
            using (var builder = graph.AddComputePass<ComputePassData>("ComputeCameraColorFromHeatmapPass", out var data))
            {
                // Assign data to the compute shader data
                data.compute = HeatmapBrightnessComputeShader;
                data.kernel = kernelHeatmapBrightnessComputeShader;
                data.input = resourceData.activeColorTexture; // here the activeColorTexture is added directly to the dataPass
                data.output = resultedTextureHandle;

                // Declare the resource usage: the current activeColorTexture is directly utilized in the builder.
                builder.UseTexture(resourceData.activeColorTexture, AccessFlags.Read);
                builder.UseTexture(resultedTextureHandle, AccessFlags.Write);

                // Set the function to execute the compute pass
                builder.SetRenderFunc((ComputePassData data, ComputeGraphContext ctx) =>
                {
                    ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, "heatmapTexture", data.input);
                    ctx.cmd.SetComputeTextureParam(data.compute, data.kernel, "Result", data.output);
                    ctx.cmd.DispatchCompute(data.compute, data.kernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
                });
            }
            
            // The resulted texture of the compute pass is assigned to the current Camera Color
            resourceData.cameraColor = resultedTextureHandle;
        }


        public void Cleanup() {
            heatmapTextureHandle?.Release();
            heatmapTextureHandle = null;
            heatmapBrightnessTextureHandle?.Release();
            heatmapBrightnessTextureHandle = null;

            enemyBuffer?.Release();
            enemyBuffer = null;
        }
    }

    [SerializeField] ComputeShader HeatmapComputeShader;
    [SerializeField] ComputeShader HeatmapBrightnessComputeShader;
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

        pass.Setup(HeatmapComputeShader, HeatmapBrightnessComputeShader);
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(pass);
        }
    }

    protected override void Dispose(bool disposing) {
        pass?.Cleanup();
    }
}
