using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using O3DParse;
using System.Numerics;
using System.Buffers;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Diagnostics;
using O3DParse.Ini;
using static O3DView.Material;

namespace O3DView
{
    internal class Renderer
    {
        private readonly GL gl;
        private readonly IWindow window;
        private readonly Shader? mainShader;
        private readonly ErrorCallback onErrorMessageCB;
        private readonly Program program;
        private readonly BufferObject<Light.ShaderData> lightBuffer;

        private int lastRenderedVerts = 0;
        private int lastRenderedTris = 0;
        private int lastDrawCalls = 0;

        private const int SHADER_LIGHTS_SSBO_BINDING = 2;

        public delegate void ErrorCallback(string caption, string message);

        public SceneGraph scene;

        public int RenderedVerts => lastRenderedVerts;
        public int RenderedTris => lastRenderedTris;
        public int DrawCalls => lastDrawCalls;

        public Renderer(GL gl, IWindow window, ErrorCallback onErrorMessageCB, Program program)
        {
            this.gl = gl;
            this.onErrorMessageCB = onErrorMessageCB;
            this.program = program;
            scene = new SceneGraph();
            var sun = new Light
            {
                colour = new(.9f, .9f, .9f),
                ambient = new(.1f, .1f, .1f),
                mode = LightMode.Directional,
                intensity = 1
            };
            //sun.transform.EulerRot = new();
            scene.lights.Add(sun);
            this.window = window;

            try
            {
                mainShader = new(gl, "main_vert.glsl", "main_frag.glsl");

                gl.Enable(EnableCap.DepthTest);
                gl.DepthFunc(DepthFunction.Lequal);
                gl.Enable(EnableCap.CullFace);
                gl.CullFace(TriangleFace.Back);
                gl.Enable(EnableCap.Multisample);
                gl.Enable(EnableCap.Blend);
                gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
                gl.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
            } catch (Exception ex)
            {
                onErrorMessageCB("Error while initialising the renderer!", ex.ToString());
            }

            Span<Light.ShaderData> lightData = stackalloc Light.ShaderData[scene.lights.Count];
            lightData = Light.CopyLightData(scene.lights, lightData);
            lightBuffer = new BufferObject<Light.ShaderData>(gl, lightData, BufferTargetARB.ShaderStorageBuffer);
            lightBuffer.Bind();
            gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, SHADER_LIGHTS_SSBO_BINDING, lightBuffer.handle);
            lightBuffer.Unbind();
        }

        public void Render(double delta)
        {
            gl.ClearColor(Color.FromArgb(255, (int)(.45f * 255), (int)(.55f * 255), (int)(.60f * 255)));
            gl.Clear((uint)ClearBufferMask.ColorBufferBit);
            var size = window.FramebufferSize;
            var proj = Matrix4x4.CreatePerspectiveFieldOfView((60 * (MathF.PI / 180)), (float)size.X / size.Y, 0.1f, 1000.0f);
            scene.camera.OnRender(delta);
            var view = scene.camera.View;

            int renderedTris = 0, renderedVerts = 0, drawCalls = 0;

            if (program.wireframe)
            {
                gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                gl.Disable(EnableCap.CullFace);
            }
            else
            {
                gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                gl.Enable(EnableCap.CullFace);
            }

            Span<Light.ShaderData> lightData = stackalloc Light.ShaderData[scene.lights.Count];
            lightData = Light.CopyLightData(scene.lights, lightData);
            lightBuffer.Update(lightData, shrink: true);

            //gl.Clear(ClearBufferMask.DepthBufferBit);
            //RenderMeshes(RenderQueue.PreTransparent, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);
            //RenderMeshes(RenderQueue.Transparent, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);
            //RenderMeshes(RenderQueue.PreOpaque, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);
            RenderMeshes(RenderQueue.Opaque, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);
            //RenderMeshes(RenderQueue.PostOpaque, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);

            //RenderMeshes(RenderQueue.Transparent, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);

            lastRenderedVerts = renderedVerts;
            lastRenderedTris = renderedTris;
            lastDrawCalls = drawCalls;
        }

        private void RenderMeshes(RenderQueue renderQueue, Matrix4x4 proj, Matrix4x4 view, ref int renderedTris, ref int renderedVerts, ref int drawCalls)
        {
            Material? lastMat = null;
            Shader? lastShader = null;
            (BlendingFactor src, BlendingFactor dst) lastBlend = (BlendingFactor.One, BlendingFactor.Zero);
            var lastDepthMode = DepthMode.Opaque;
            gl.DepthFunc(DepthFunction.Lequal);
            gl.DepthMask(true);
            gl.BlendFunc(lastBlend.src, lastBlend.dst);
            gl.Clear(ClearBufferMask.DepthBufferBit);

            foreach (Mesh m in scene.meshes)
            {
                if (!m.visible)
                    continue;

                if (renderQueue == RenderQueue.Opaque)
                {
                    renderedVerts += m.VertCount;
                    renderedTris += m.TriCount;
                }

                var model = m.transform.Matrix;
                foreach (var sm in m.submeshes)
                {
                    //if (renderQueue != sm.mat.renderQueue)
                    //    continue;

                    // Only update shader parameters if needed
                    if (lastMat != sm.mat)
                    {
                        bool shaderChanged = lastShader != sm.mat.shader;
                        sm.mat.Bind(shaderChanged, ref lastBlend, ref lastDepthMode);
                        lastMat = sm.mat;
                        lastShader = sm.mat.shader;

                        if (shaderChanged)
                        {
                            // Whenever the shader is changed we should need to reset the view and projection matrices
                            // The current light (forward rendering) also needs to be set whenever it's changed
                            lastShader.SetUniform("uView", view);
                            lastShader.SetUniform("uProj", proj);
                            lastShader.SetUniform("uTime", (float)window.Time);
                            lastShader.SetUniform("uViewDir", scene.camera.direction);
                            lastShader.SetUniform("uViewPos", scene.camera.transform.Pos);
                            lastShader.SetUniform("uFullbright", program.fullbright);
                            lastShader.SetUniform("uWireframe", program.wireframe);
                            lastShader.SetUniform("uLightmapStrength", program.lightmapStrength);
                            lastShader.SetUniform("uNightmapStrength", program.nightmapStrength);
                        }
                    }

                    sm.mat.shader.SetUniform("uModel", model);
                    bool selected = program.selectionHighlight && m == program.SelectedObject;
                    sm.mat.shader.SetUniform("uSelected", selected);
                    if (selected)
                    {
                        DepthMode selectedDepthMode = DepthMode.ZWrite;
                        gl.DepthMask(true);
                        gl.DepthFunc(DepthFunction.Always);
                        lastDepthMode = selectedDepthMode;
                    }

                    sm.vao.Bind();
                    gl.DrawElements(PrimitiveType.Triangles, sm.vertCount, DrawElementsType.UnsignedInt, (ReadOnlySpan<uint>)null);
                    drawCalls++;
                }
            }
            // TODO: We could progressively sort the list of renderers to reduce the number of shader/material changes?
            //       a bit like dynamic batching?
        }

        public Mesh CreateMeshFromO3D(O3DFile o3d, string path, MeshCommand? meshCommand, string? cfgPath)
        {
            if (mainShader == null)
                throw new Exception("Main shader was not initialised, object cannot be created!");
            return new O3DMesh(o3d, path, gl, mainShader, scene, meshCommand, cfgPath);
        }

        private void DBG_LogBoundResources()
        {
            Debug.WriteLine("### Currently bound OpenGL Resources ###");
            int handle;

            gl.GetInteger(GetPName.ActiveTexture, out handle);
            Debug.WriteLine($"\tActiveTexture = {handle}");

            gl.GetInteger(GetPName.BlendEquation, out handle);
            Debug.WriteLine($"\tBlendEquation = {handle}");

            gl.GetInteger(GetPName.ElementArrayBufferBinding, out handle);
            Debug.WriteLine($"\tElementArrayBufferBinding = {handle}");

            gl.GetInteger(GetPName.ArrayBufferBinding, out handle);
            Debug.WriteLine($"\tArrayBufferBinding = {handle}");

            gl.GetInteger(GetPName.VertexArrayBinding, out handle);
            Debug.WriteLine($"\tVertexArrayBinding = {handle}");

            gl.GetInteger(GetPName.UniformBufferBinding, out handle);
            Debug.WriteLine($"\tUniformBufferBinding = {handle}");

            gl.GetInteger(GetPName.ProgramPipelineBinding, out handle);
            Debug.WriteLine($"\tProgramPipelineBinding = {handle}");
        }
    }
}
