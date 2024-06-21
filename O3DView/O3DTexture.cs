using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace O3DView
{
    internal class O3DTexture : Texture2D
    {
        public string FileName => filename;

        private readonly string filename;
        private readonly string basePath;
        private string? resolvedPath;

        private static readonly Dictionary<string, O3DTexture> textureCache = [];

        private O3DTexture(GL gl, string fileName, string basePath) : base(gl)
        {
            this.filename = fileName;
            this.basePath = Path.GetFullPath(basePath);
            isValid = false;
            Load();
        }

        public static O3DTexture? CreateOrUseCached(GL gl, string fileName, string basePath)
        {
            basePath = Path.GetFullPath(basePath);
            string? resolved = ResolveO3DTexPath(basePath, fileName);
            if (resolved == null)
                return null;

            if(textureCache.TryGetValue(resolved, out O3DTexture? value))
                return value;

            var tex = new O3DTexture(gl, fileName, basePath);
            textureCache.Add(resolved, tex);
            return tex;
        }

        public static O3DTexture? GetCached(string fileName, string basePath)
        {
            string? resolved = ResolveO3DTexPath(basePath, fileName);
            if (resolved == null)
                return null;

            if (textureCache.TryGetValue(resolved, out O3DTexture? value))
                return value;
            return null;
        }

        private static string? ResolveO3DTexPath(string basePath, string filename)
        {
            string? dir = Path.GetDirectoryName(basePath);
            string ddsName = Path.ChangeExtension(filename, ".dds");
            while(!string.IsNullOrEmpty(dir))
            {
                string testPath = Path.Combine(dir, ddsName);
                if(File.Exists(testPath))
                    return testPath;

                testPath = Path.Combine(dir, "texture", ddsName);
                if (File.Exists(testPath))
                    return testPath;

                testPath = Path.Combine(dir, filename);
                if (File.Exists(testPath))
                    return testPath;

                testPath = Path.Combine(dir, "texture", filename);
                if (File.Exists(testPath))
                    return testPath;

                dir = Directory.GetParent(dir)?.FullName;
            }
            return null;
        }

        public void Load()
        {
            resolvedPath = ResolveO3DTexPath(basePath, filename);
            if (resolvedPath == null)
                return;

            switch(Path.GetExtension(resolvedPath))
            {
                case ".dds":
                    {
                        using var f = File.OpenRead(resolvedPath);
                        var img = DDSReader.ReadFromStream(f);
                        if (img.textureType != TextureType.Texture2D)
                            throw new NotImplementedException("Can't load non-2D texture!");
                        Bind();
                        if(img.compression != TextureCompressionType.None)
                        {
                            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                            for (int m = 0; m < img.mipmaps; m++)
                            {
                                var mip = img.data[0].mipMaps[m];
                                gl.CompressedTexImage2D<byte>(TextureTarget.Texture2D, m, img.internalFormat, mip.width, mip.height, 0, (uint)mip.data.Length, mip.data.AsSpan());
                            }
                        } else
                        {
                            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                            for (int m = 0; m < img.mipmaps; m++)
                            {
                                var mip = img.data[0].mipMaps[m];
                                gl.TexImage2D<byte>(TextureTarget.Texture2D, m, img.internalFormat, mip.width, mip.height, 0, img.format, img.pixelType, mip.data.AsSpan());
                            }
                        }
                        //if(img.mipmaps <= 1)
                            gl.GenerateMipmap(TextureTarget.Texture2D);
                        gl.TextureParameter(handle, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                        gl.TextureParameter(handle, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                        gl.TextureParameter(handle, TextureParameterName.TextureMaxAnisotropy, 8);
                        Unbind();
                        break;
                    }
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".bmp":
                    {
                        using var f = File.OpenRead(resolvedPath);
                        var img = ImageResult.FromStream(f);
                        Bind();
                        (pixelFormat, internalFormat, pixelType) = img.Comp.ToOpenGLFormat();
                        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
                        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, internalFormat, (uint)img.Width, (uint)img.Height, 0, pixelFormat, pixelType, img.Data.AsSpan());
                        gl.GenerateMipmap(TextureTarget.Texture2D);
                        gl.TextureParameter(handle, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
                        gl.TextureParameter(handle, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                        gl.TextureParameter(handle, TextureParameterName.TextureMaxAnisotropy, 8);
                        Unbind();
                        break;
                    }
            }

            isValid = true;
        }
    }
}
