using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Vector2 = UnityEngine.Vector2;

public class ParticleRenderGraphFeature : ScriptableRendererFeature
{
    class ParticlePass : ScriptableRenderPass
    {
        // Compute Shader
        ComputeShader m_ParticleComputeShader;
        int m_KernelID;

        // Particle data
        struct Particle
        {
            public Vector3 position;
            public Vector3 velocity;
            public float life;
        }

        const int SIZE_PARTICLE = 7 * sizeof(float);
        const int k_ParticleCount = 1000000; // Adjust as needed

        BufferHandle m_ParticleBuffer;
        GraphicsBuffer m_PersistentParticleBuffer;
        Particle[] m_ParticleArray;

        // Material for rendering
        Material m_ParticleMaterial;

        // Mouse position (for simplicity, can be set from inspector or input)
        Vector2 m_MousePosition;

        public void Setup(ComputeShader computeShader, Material material, Vector2 mousePos)
        {
            m_ParticleComputeShader = computeShader;
            m_ParticleMaterial = material;
            m_MousePosition = mousePos;
            m_KernelID = computeShader.FindKernel("CSParticle");

            // Initialize particles if not already
            if (m_PersistentParticleBuffer == null)
            {
                m_ParticleArray = new Particle[k_ParticleCount];
                for (int i = 0; i < k_ParticleCount; i++)
                {
                    float x = Random.value * 2 - 1.0f;
                    float y = Random.value * 2 - 1.0f;
                    float z = Random.value * 2 - 1.0f;
                    Vector3 xyz = new Vector3(x, y, z);
                    xyz.Normalize();
                    xyz *= Random.value * 5;

                    m_ParticleArray[i].position = xyz;
                    m_ParticleArray[i].velocity = Vector3.zero;
                    m_ParticleArray[i].life = Random.value * 5.0f + 1.0f;
                }

                m_PersistentParticleBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, k_ParticleCount, SIZE_PARTICLE);
                m_PersistentParticleBuffer.SetData(m_ParticleArray);
            }


        }

        // Compute Pass Data
        class ComputePassData
        {
            public ComputeShader computeShader;
            public int kernel;
            public BufferHandle particleBuffer;
            public Vector2 mousePosition;
            public float deltaTime;
            public int particleCount;
        }

        // Render Pass Data
        class RenderPassData
        {
            public Material material;
            public BufferHandle particleBuffer;
            public int particleCount;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer context)
        {
            var resourceData = context.Get<UniversalResourceData>();

            // Import persistent buffer
            m_ParticleBuffer = graph.ImportBuffer(m_PersistentParticleBuffer);

            // Compute pass to update particles
            using (var builder = graph.AddComputePass<ComputePassData>("UpdateParticles", out var computePassData))
            {
                computePassData.computeShader = m_ParticleComputeShader;
                computePassData.kernel = m_KernelID;
                computePassData.particleBuffer = m_ParticleBuffer;
                computePassData.mousePosition = m_MousePosition;
                computePassData.deltaTime = Time.deltaTime;
                computePassData.particleCount = k_ParticleCount;

                builder.UseBuffer(computePassData.particleBuffer, AccessFlags.ReadWrite);

                builder.SetRenderFunc(static (ComputePassData data, ComputeGraphContext ctx) =>
                {
                    // Set compute parameters
                    ctx.cmd.SetComputeFloatParam(data.computeShader, "deltaTime", data.deltaTime);
                    ctx.cmd.SetComputeVectorParam(data.computeShader, "mousePosition", data.mousePosition);
                    ctx.cmd.SetComputeBufferParam(data.computeShader, data.kernel, "particleBuffer", data.particleBuffer);

                    // Dispatch
                    uint threadsX;
                    data.computeShader.GetKernelThreadGroupSizes(data.kernel, out threadsX, out _, out _);
                    int groupSizeX = Mathf.CeilToInt((float)data.particleCount / (float)threadsX);
                    ctx.cmd.DispatchCompute(data.computeShader, data.kernel, groupSizeX, 1, 1);
                });
            }

            // Render pass to draw particles
            using (var builder = graph.AddRasterRenderPass<RenderPassData>("RenderParticles", out var renderPassData))
            {
                renderPassData.material = m_ParticleMaterial;
                renderPassData.particleBuffer = m_ParticleBuffer;
                renderPassData.particleCount = k_ParticleCount;

                // Set render attachments
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

                builder.SetRenderFunc(static (RenderPassData data, RasterGraphContext ctx) =>
                {
                    // Set buffer on material
                    data.material.SetBuffer("particleBuffer", data.particleBuffer);

                    // Draw procedural
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Points, data.particleCount, 1);
                });
            }
        }
    }

    // Inspector fields
    [SerializeField] ComputeShader ParticleComputeShader;
    [SerializeField] Material ParticleMaterial;
    [SerializeField] Vector2 MousePosition; // For simplicity, set manually or get from input

    ParticlePass particlePass;

    public override void Create()
    {
        particlePass = new ParticlePass
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques // Or appropriate event
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (ParticleComputeShader == null || ParticleMaterial == null)
        {
            Debug.Log("Set both compute shader and material for the ParticleRenderGraphFeature.");
            return;
        }

        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.Log("The ParticleRenderGraphFeature cannot be added because this system doesn't support compute shaders.");
            return;
        }

        if (renderingData.cameraData.cameraType == CameraType.Game)
        {
            particlePass.Setup(ParticleComputeShader, ParticleMaterial, MousePosition);
            renderer.EnqueuePass(particlePass);
        }
    }
}