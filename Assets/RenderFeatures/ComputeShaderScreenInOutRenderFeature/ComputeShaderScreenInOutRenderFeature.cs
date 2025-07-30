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
    public static ComputeShaderScreenInOutRenderFeature Instance { get; private set; }

    class HeatmapPass : ScriptableRenderPass {
        ComputeShader computeShader;
        int kernel;
        int kernelP2;

        GraphicsBuffer enemyBuffer;
        Vector2[] enemyPositions;
        int enemyCount = 64;

        RTHandle heatmapHandle;
        int width = Screen.width, height = Screen.height;

        public RTHandle Heatmap => heatmapHandle;

        ComputeShader computeShaderP2;

        Vector4 rectF4;
        RTHandle heatmapHandleP2;

        public void Setup(ComputeShader cs, ComputeShader cs2) {
            // Here are define the both Compute shader
            // The output of the first one will be used as CameraColor 
            // and the second one uses the current CameraColor as input to process
            // another one as result.
            computeShader = cs;
            computeShaderP2 = cs2;
            kernel = cs.FindKernel("CSMain");
            kernelP2 = cs2.FindKernel("CSMain");

            int halfRes = Screen.width >> 1;
            int quarterRes = Screen.width >> 2;

            Vector4 rect = new Vector4(quarterRes, quarterRes, halfRes, halfRes);

            rectF4 = new Vector4(0, 1, 1, 1);

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
            if (heatmapHandleP2 == null || heatmapHandleP2.rt.width != width || heatmapHandleP2.rt.height != height)
            {
                heatmapHandleP2?.Release();
                var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.Default, 0)
                {
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    sRGB = false,
                    useMipMap = false
                };
                heatmapHandleP2 = RTHandles.Alloc(desc, name: "_HeatmapRTP2");
            }

            if (enemyBuffer == null || enemyBuffer.count != enemyCount) {
                enemyBuffer?.Release();
                enemyBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, enemyCount, sizeof(float) * 2);
                enemyPositions = new Vector2[enemyCount];
            }
        }

        class PassData {
            public ComputeShader compute;
            public int kernel;
            public TextureHandle output;
            public Vector2 texSize;
            public BufferHandle enemyHandle;
            public int enemyCount;
        }

        class PassDataP2
        {
            public ComputeShader compute;
            public int kernel;
            public TextureHandle output;
            public Vector2 texSize;
            public BufferHandle enemyHandle;
            public int enemyCount;
            public Vector4 myrect;
            public TextureHandle inputt;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer context) {
            for (int i = 0; i < enemyCount; i++) {
                float t = Time.time * 0.5f + i * 0.1f;
                float x = Mathf.PerlinNoise(t, i * 1.31f) * width;
                float y = Mathf.PerlinNoise(i * 0.91f, t) * height;
                enemyPositions[i] = new Vector2(x, y);
            }
            
            enemyBuffer.SetData(enemyPositions);

            TextureHandle texHandle = graph.ImportTexture(heatmapHandle);
            BufferHandle enemyHandle = graph.ImportBuffer(enemyBuffer);

            /**/
            using IComputeRenderGraphBuilder builder = graph.AddComputePass("HeatmapPass", out PassData data);
            data.compute = computeShader;
            data.kernel = kernel;
            data.output = texHandle;
            data.enemyHandle = enemyHandle;
            data.enemyCount = enemyCount;

            builder.UseTexture(texHandle, AccessFlags.Write);
            builder.UseBuffer(enemyHandle, AccessFlags.Read);

            builder.SetRenderFunc((PassData d, ComputeGraphContext ctx) => {
                ctx.cmd.SetComputeIntParam(d.compute, "enemyCount", d.enemyCount);
                ctx.cmd.SetComputeBufferParam(d.compute, d.kernel, "enemyPositions", d.enemyHandle);
                ctx.cmd.SetComputeTextureParam(d.compute, d.kernel, "heatmapTexture", d.output);
                ctx.cmd.DispatchCompute(d.compute, d.kernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
            });
            
            // Here the texture resulted from the first compute shader
            // is assigned to the camera Color
            var resourceData = context.Get<UniversalResourceData>();
            resourceData.cameraColor = texHandle;
            
            // A new texture is created based on the active Color Texture
            var newTextureHandle = resourceData.activeColorTexture;
            
            // A NEW COMPUTE PASS is Built
            TextureHandle texHandle2 = graph.ImportTexture(heatmapHandleP2);
            BufferHandle enemyHandle2 = graph.ImportBuffer(enemyBuffer);
            
            builder.Dispose();

            using IComputeRenderGraphBuilder builderP2 = graph.AddComputePass("HeatmapPassP2", out PassDataP2 dataP2);
            dataP2.compute = computeShaderP2;
            dataP2.kernel = kernelP2;
            dataP2.output = texHandle2;
            dataP2.enemyHandle = enemyHandle2;
            dataP2.inputt = newTextureHandle;

            builderP2.UseTexture(newTextureHandle, AccessFlags.Write);
            builderP2.UseTexture(texHandle2, AccessFlags.Write);
            builderP2.UseBuffer(enemyHandle2, AccessFlags.Read);

            builderP2.SetRenderFunc((PassDataP2 d, ComputeGraphContext ctx) => {
                ctx.cmd.SetComputeTextureParam(d.compute, d.kernel, "heatmapTexture", d.inputt);
                
                ctx.cmd.SetComputeTextureParam(d.compute, d.kernel, "Result", d.output);
                ctx.cmd.SetComputeVectorParam(d.compute, d.kernel, d.myrect);
                ctx.cmd.DispatchCompute(d.compute, d.kernel, Mathf.CeilToInt(width / 8f), Mathf.CeilToInt(height / 8f), 1);
            });
            
            // Then the resulted texture is assigned to the current
            // Camera Color
            resourceData.cameraColor = texHandle2;
        }


        public void Cleanup() {
            heatmapHandle?.Release();
            heatmapHandle = null;

            enemyBuffer?.Release();
            enemyBuffer = null;
        }
    }

    [SerializeField] ComputeShader computeShader;
    [SerializeField] ComputeShader computeShaderP2;
    HeatmapPass pass;

    public override void Create() {
        pass = new HeatmapPass
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
        Instance = this;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (!SystemInfo.supportsComputeShaders || computeShader == null)
            return;

        pass.Setup(computeShader, computeShaderP2);
        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(pass);
        }
    }

    protected override void Dispose(bool disposing) {
        pass?.Cleanup();
    }

    public RTHandle GetHeatmapTexture() => pass?.Heatmap;
}
