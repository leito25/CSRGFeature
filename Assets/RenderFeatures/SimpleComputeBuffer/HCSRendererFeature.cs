using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class HCSRendererFeature : ScriptableRendererFeature {
    public static HCSRendererFeature Instance { get; private set; }

    class HeatmapPass : ScriptableRenderPass {
        ComputeShader computeShader;
        int kernel;

        GraphicsBuffer enemyBuffer;
        Vector2[] enemyPositions;
        int enemyCount = 64;

        RTHandle heatmapHandle;
        int width = Screen.width, height = Screen.height;

        public RTHandle Heatmap => heatmapHandle;

        public void Setup(ComputeShader cs) {
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

        class PassData {
            public ComputeShader compute;
            public int kernel;
            public TextureHandle output;
            public Vector2 texSize;
            public BufferHandle enemyHandle;
            public int enemyCount;
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
        Instance = this;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (!SystemInfo.supportsComputeShaders || computeShader == null)
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

    public RTHandle GetHeatmapTexture() => pass?.Heatmap;
}
