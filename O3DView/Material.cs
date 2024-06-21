using System.Numerics;
using O3DParse;
using O3DParse.Ini;
using Silk.NET.OpenGL;

namespace O3DView
{
    public class Material
    {
        public Vector4 albedo;
        public Vector3 spec;
        public Vector3 emission;
        public BlendMode blendMode;
        public DepthMode depthMode;
        public float roughness;
        public Texture2D? texture;

        public Texture2D? transMap;
        public Texture2D? nightMap;
        public Texture2D? lightMap;
        public Texture2D? envMap;
        public float envMapStrength;
        public Texture2D? bumpMap;
        public float bumpMapStrength;
        public bool isShadow;

        public RenderQueue renderQueue => blendMode == BlendMode.Opaque || blendMode == BlendMode.Cutout ? RenderQueue.Opaque : RenderQueue.Transparent;

        public Shader shader;
        private readonly GL gl;
        private Texture2D[] boundTextures = new Texture2D[6];

        public Material(GL gl, Vector4 albedo, Vector3 spec, float roughness, Texture2D? texture, Shader shader)
        {
            this.albedo = albedo;
            this.spec = spec;
            this.roughness = roughness;
            this.texture = texture;
            this.shader = shader;
            this.gl = gl;
        }

        public Material(GL gl, Shader shader)
        {
            this.shader = shader;
            this.gl = gl;
        }

        public void UpdateFromO3D(O3DMaterial o3d, string o3dPath)
        {
            albedo = o3d.diffuse;
            spec = o3d.spec;
            roughness = 1 / (o3d.specPower + 1);
            emission = o3d.emission;
            if (texture is O3DTexture o3dTex && o3dTex.FileName != o3d.textureName)
            {
                texture.Dispose();
                texture = null;
            }
            texture ??= O3DTexture.CreateOrUseCached(gl, o3d.textureName, o3dPath);
        }

        public void UpdateFromCfgMatl(MatlCommand cfgMatl, string cfgPath)
        {
            if (cfgMatl.noZCheck != null)
                depthMode &= ~DepthMode.ZTest;
            if (cfgMatl.noZWrite != null)
                depthMode &= ~DepthMode.ZWrite;
            if (cfgMatl.alpha != null)
            {
                switch (cfgMatl.alpha?.alphaMode)
                {
                    case MatlAlphaMode.Opaque:
                        blendMode = BlendMode.Opaque;
                        break;
                    case MatlAlphaMode.Cutout:
                        blendMode = BlendMode.Cutout;
                        break;
                    case MatlAlphaMode.Blend:
                        blendMode = BlendMode.AlphaBlend;
                        break;
                }
            }
            if (cfgMatl.allcolor is AllcolorCommand allcolor)
            {
                albedo = allcolor.diffuse;
                spec = allcolor.specular;
                roughness = 1 / (allcolor.specPower + 1);
                emission = allcolor.emission + allcolor.ambient;
            }
            if (cfgMatl.bumpmap is MatlBumpmapCommand bumpMapCmd)
            {
                bumpMap ??= O3DTexture.CreateOrUseCached(gl, bumpMapCmd.path, cfgPath);
                bumpMapStrength = bumpMapCmd.strength;
            }
            if (cfgMatl.transmap is MatlTransmapCommand transmapCmd)
                transMap ??= O3DTexture.CreateOrUseCached(gl, transmapCmd.path, cfgPath);
            if (cfgMatl.envmap is MatlEnvmapCommand envmapCmd)
            {
                envMap ??= O3DTexture.CreateOrUseCached(gl, envmapCmd.path, cfgPath);
                envMapStrength = envmapCmd.reflectivity;
            }
            if (cfgMatl.lightmap is MatlLightmapCommand lightmapCmd)
            {
                lightMap ??= O3DTexture.CreateOrUseCached(gl, lightmapCmd.path, cfgPath);
            }
            if (cfgMatl.nightmap is MatlNightmapCommand nightmapCmd)
            {
                nightMap ??= O3DTexture.CreateOrUseCached(gl, nightmapCmd.path, cfgPath);
            }
        }

        public static Material CreateFromO3D(O3DMaterial o3dMat, string o3dPath, Shader shader, GL gl, SceneGraph scene, 
            MatlCommand? cfgMatl = null, string? cfgPath = null, bool isShadow = false)
        {
            // TODO: Re-enable material cache. For now it's disabled since it will combine identical materials shared by different
            // meshes/submeshes which the user may not want.
            if (scene.materialCache.ContainsKey(o3dMat.GetHashCode()))
                return scene.materialCache[o3dMat.GetHashCode()];

            Material mat = new(gl, shader);
            mat.isShadow = isShadow;
            mat.UpdateFromO3D(o3dMat, o3dPath);
            if (mat.albedo.W < 1 || ((mat.texture?.IsValid ?? false) && (mat.texture.Format == PixelFormat.Rgba || mat.texture.Format == PixelFormat.Alpha)))
            {
                mat.blendMode = BlendMode.AlphaBlend;
                mat.depthMode = DepthMode.Opaque;
            }
            else
            {
                mat.blendMode = BlendMode.Opaque;
                mat.depthMode = DepthMode.Opaque;
            }

            if (cfgMatl != null && cfgPath != null)
            {
                mat.blendMode = BlendMode.Opaque;
                mat.UpdateFromCfgMatl(cfgMatl.Value, cfgPath);
            }

            if (Program.cacheMaterials)
                scene.materialCache.Add(o3dMat.GetHashCode(), mat);
            return mat;
        }

        private void BindTextures()
        {
            int bindInd = 0;
            if (texture != null && texture.IsValid)
            {
                boundTextures[bindInd] = texture;
                shader.SetUniform("uMainTex", bindInd++);
                shader.SetUniform("uUseMainTex", true);
            }
            else
                shader.SetUniform("uUseMainTex", false);

            if (transMap != null && transMap.IsValid)
            {
                boundTextures[bindInd] = transMap;
                shader.SetUniform("uTransmap", bindInd++);
                shader.SetUniform("uUseTransmap", true);
            }
            else
                shader.SetUniform("uUseTransmap", false);

            if (nightMap != null && nightMap.IsValid)
            {
                boundTextures[bindInd] = nightMap;
                shader.SetUniform("uNightmap", bindInd++);
                shader.SetUniform("uUseNightmap", true);
            }
            else
                shader.SetUniform("uUseNightmap", false);

            if (lightMap != null && lightMap.IsValid)
            {
                boundTextures[bindInd] = lightMap;
                shader.SetUniform("uLightmap", bindInd++);
                shader.SetUniform("uUseLightmap", true);
            }
            else
                shader.SetUniform("uUseLightmap", false);

            if (envMap != null && envMap.IsValid)
            {
                boundTextures[bindInd] = envMap;
                shader.SetUniform("uEnvmap", bindInd++);
                shader.SetUniform("uUseEnvmap", true);
                shader.SetUniform("uEnvmapStrength", envMapStrength);
            }
            else
                shader.SetUniform("uUseEnvmap", false);

            if (bumpMap != null && bumpMap.IsValid)
            {
                boundTextures[bindInd] = bumpMap;
                shader.SetUniform("uBumpmap", bindInd++);
                shader.SetUniform("uUseBumpmap", true);
                shader.SetUniform("uBumpmapStrength", bumpMapStrength);
            }
            else
                shader.SetUniform("uUseBumpmap", false);

            Texture2D.BindTextures(0, boundTextures.AsSpan()[..bindInd]);
        }

        public void Bind(bool needsToBindShader, ref (BlendingFactor src, BlendingFactor dst) lastBlendFunc, ref DepthMode lastDepthMode)
        {
            if (needsToBindShader)
                shader.Use();

            BindTextures();

            shader.SetUniform("uAlbedo", albedo);
            shader.SetUniform("uSpec", spec);
            shader.SetUniform("uRough", roughness);
            shader.SetUniform("uEmission", emission);
            (BlendingFactor src, BlendingFactor dst) blendFunc = blendMode switch
            {
                BlendMode.Opaque => (BlendingFactor.One, BlendingFactor.Zero),
                BlendMode.Cutout => (BlendingFactor.One, BlendingFactor.Zero),
                BlendMode.AlphaBlend => (BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha),
                BlendMode.AlphaPreMul => (BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha),
                BlendMode.Add => (BlendingFactor.One, BlendingFactor.One),
                _ => throw new NotImplementedException(),
            };
            if (blendFunc != lastBlendFunc)
            {
                gl.BlendFunc(blendFunc.src, blendFunc.dst);
                lastBlendFunc = blendFunc;
                if (blendMode == BlendMode.Cutout)
                    shader.SetUniform("uCutout", true);
                else
                    shader.SetUniform("uCutout", false);
            }

            var _depthMode = depthMode;
            if (isShadow)
                _depthMode = DepthMode.Opaque;

            if ((_depthMode & DepthMode.ZTest) != (lastDepthMode & DepthMode.ZTest))
            {
                //if ((_depthMode & DepthMode.ZTest) != 0)
                //    gl.DepthFunc(DepthFunction.Lequal);
                //else
                //    gl.DepthFunc(DepthFunction.Always);
            }
            if ((_depthMode & DepthMode.ZWrite) != (lastDepthMode & DepthMode.ZWrite))
            {
                if ((_depthMode & DepthMode.ZWrite) != 0)
                    gl.DepthMask(true);
                else
                    gl.DepthMask(false);
            }
            lastDepthMode = _depthMode;
        }

        public enum BlendMode
        {
            Opaque,
            Cutout,
            AlphaBlend,
            AlphaPreMul,
            Add
        }

        public enum RenderQueue
        {
            PreTransparent,
            Transparent,
            PreOpaque,
            Opaque,
            PostOpaque
        }

        [Flags]
        public enum DepthMode
        {
            None,
            ZTest = 1 << 0,
            ZWrite = 1 << 1,

            Opaque = ZTest | ZWrite,
        }
    }
}
