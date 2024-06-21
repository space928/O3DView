using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace O3DView
{
    public static class DDSReader
    {
        private const uint DDS_MAGIC = 0x20534444;  // "DDS "

        public static DDSTexture ReadFromStream(Stream stream, bool leaveOpen = true)
        {
            using BinaryReader reader = new(stream, Encoding.Default, leaveOpen);

            DDSFile dds = new();
            // Check magic
            uint magic = reader.ReadUInt32();
            if (magic != DDS_MAGIC)
                throw new DDSException($"File magic 0x{magic:X8} does not magic DDS magic number!");

            // Read header
            var headerSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref dds.header, 1));
            stream.ReadExactly(headerSpan);
            if (dds.header.size != Unsafe.SizeOf<DDSHeader>()
                || dds.header.ddspf.size != Unsafe.SizeOf<DDSPixelFormat>())
                throw new DDSException("Unexpected DDS header size!");

            // Read extended header
            if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_FOURCC) != 0
                && dds.header.ddspf.fourCC == DDSFourCC.DX10)
            {
                var extHeaderSpan = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref dds.dx10Header, 1));
                stream.ReadExactly(extHeaderSpan);
            }

            if ((dds.header.caps & DDSCaps1.DDSCAPS_TEXTURE) == 0)
                throw new DDSException("DDS file doesn't have DDSCAPS_TEXTURE!");

            // Determine texture type
            DDSTexture tex = new();
            tex.width = dds.header.width;
            tex.height = dds.header.height;
            if ((dds.header.caps2 & DDSCaps2.DDSCAPS2_CUBEMAP) != 0)
            {
                if (dds.dx10Header.arraySize > 1)
                    tex.textureType = TextureType.TextureCubeArray;
                else
                    tex.textureType = TextureType.TextureCube;
            }
            else if ((dds.header.caps2 & DDSCaps2.DDSCAPS2_VOLUME) != 0)
            {
                tex.textureType = TextureType.Texture3D;
            }
            else
            {
                if (dds.dx10Header.arraySize > 1)
                    tex.textureType = TextureType.Texture2DArray;
                else
                    tex.textureType = TextureType.Texture2D;
            }

            // Determine number of elements
            switch (tex.textureType)
            {
                case TextureType.Texture2D:
                    tex.elements = 1;
                    break;
                case TextureType.TextureCube:
                    tex.elements = 6;
                    break;
                case TextureType.Texture3D:
                    tex.elements = dds.header.depth;
                    break;
                case TextureType.Texture2DArray:
                    tex.elements = dds.dx10Header.arraySize;
                    break;
                case TextureType.TextureCubeArray:
                    tex.elements = 6 * dds.dx10Header.arraySize;
                    break;
            }

            // Determine number of mip maps
            if ((dds.header.caps & DDSCaps1.DDS_SURFACE_FLAGS_MIPMAP) != 0)
                tex.mipmaps = dds.header.mipMapCount;
            else
                tex.mipmaps = 1;

            // Determine the corresponding OpenGL format
            tex.compression = TextureCompressionType.None; //IsCompressed(dds);
            DetermineOGLFormat(ref tex, dds);

            // Read texture data
            tex.data = new DDSTexture.Element[tex.elements];
            for (int e = 0; e < tex.elements; e++)
            {
                ref var el = ref tex.data[e];
                el.mipMaps = new DDSTexture.MipMap[tex.mipmaps];

                uint width = tex.width;
                uint height = tex.height;

                for (int m = 0; m < tex.mipmaps; m++)
                {
                    ref var mip = ref el.mipMaps[m];
                    mip.width = width;
                    mip.height = height;
                    ulong size = ComputeDDSBytes(dds, width, height);

                    mip.data = new byte[size];
                    stream.ReadExactly(mip.data.AsSpan());

                    width /= 2;
                    height /= 2;
                    width = Math.Max(1, width);
                    height = Math.Max(1, height);
                }
            }

            return tex;
        }

        // https://github.com/microsoft/DirectXTex/blob/main/DDSTextureLoader/DDSTextureLoader11.cpp#L652
        private static void DetermineOGLFormat(ref DDSTexture tex, DDSFile dds)
        {
            if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_FOURCC) != 0)
            {
                switch (dds.header.ddspf.fourCC)
                {
                    case DDSFourCC.DX10:
                        ConvertDXGIFormat(ref tex, dds.dx10Header.dxgiFormat);
                        return;
                    case DDSFourCC.DXT1:
                        ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM);
                        return;
                    case DDSFourCC.DXT2:
                    case DDSFourCC.DXT3:
                        ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM);
                        return;
                    case DDSFourCC.DXT4:
                    case DDSFourCC.DXT5:
                        ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM);
                        return;
                    default:
                        throw new DDSException($"Can't convert FourCC format 0x{dds.header.ddspf.fourCC:X8} to OpenGL!");
                }
            }

            if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_RGB) != 0)
            {
                // RGB/RGBA
                switch (dds.header.ddspf.rgbBitCount)
                {
                    case 32:
                        switch ((dds.header.ddspf.rBitMask,
                            dds.header.ddspf.gBitMask,
                            dds.header.ddspf.bBitMask,
                            dds.header.ddspf.aBitMask))
                        {
                            case (0x000000ff, 0x0000ff00, 0x00ff0000, 0xff000000):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM);
                                return;
                            case (0x00ff0000, 0x0000ff00, 0x000000ff, 0xff000000):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM);
                                return;
                            case (0x00ff0000, 0x0000ff00, 0x000000ff, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_UNORM);
                                return;
                            case (0x000000ff, 0x0000ff00, 0x00ff0000, 0):
                                // D3DFMT_X8B8G8R8
                                tex.format = Silk.NET.OpenGL.PixelFormat.AbgrExt;
                                tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8;
                                tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                                return;
                            case (0x3ff00000, 0x000ffc00, 0x000003ff, 0xc0000000):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM);
                                return;
                            case (0x000003ff, 0x000ffc00, 0x3ff00000, 0xc0000000):
                                // D3DFMT_A2R10G10B10
                                throw new DDSException($"Can't convert DDS format D3DFMT_A2R10G10B10 to OpenGL!");
                            case (0x0000ffff, 0xffff0000, 0, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM);
                                return;
                            case (0xffffffff, 0, 0, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT);
                                return;
                            default:
                                throw new DDSException($"Can't convert DDS format 32-R{dds.header.ddspf.rBitMask:x}G{dds.header.ddspf.gBitMask:x}B{dds.header.ddspf.bBitMask:x}A{dds.header.ddspf.aBitMask:x} to OpenGL!");
                        }
                    case 24:
                        // D3DFMT_R8G8B8
                        tex.format = Silk.NET.OpenGL.PixelFormat.Rgb;
                        tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgb8;
                        tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                        return;
                    case 16:
                        switch ((dds.header.ddspf.rBitMask,
                            dds.header.ddspf.gBitMask,
                            dds.header.ddspf.bBitMask,
                            dds.header.ddspf.aBitMask))
                        {
                            case (0x7c00, 0x03e0, 0x001f, 0x8000):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_B5G5R5A1_UNORM);
                                return;
                            case (0xf800, 0x07e0, 0x001f, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_B5G6R5_UNORM);
                                return;
                            case (0x7c00, 0x03e0, 0x001f, 0):
                                // D3DFMT_X1R5G5B5
                                throw new DDSException($"Can't convert DDS format D3DFMT_X1R5G5B5 to OpenGL!");
                            case (0x0f00, 0x00f0, 0x000f, 0xf000):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_B4G4R4A4_UNORM);
                                return;
                            case (0x00ff, 0, 0, 0xff00):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM);
                                return;
                            case (0xffff, 0, 0, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R16_UNORM);
                                return;
                            case (0x0f00, 0x00f0, 0x000f, 0):
                                // D3DFMT_X4R4G4B4
                                tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                                tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba4;
                                tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort4444;
                                return;
                            default:
                                throw new DDSException($"Can't convert DDS format 16-R{dds.header.ddspf.rBitMask:x}G{dds.header.ddspf.gBitMask:x}B{dds.header.ddspf.bBitMask:x}A{dds.header.ddspf.aBitMask:x} to OpenGL!");
                        }
                    case 8:
                        switch ((dds.header.ddspf.rBitMask,
                            dds.header.ddspf.gBitMask,
                            dds.header.ddspf.bBitMask,
                            dds.header.ddspf.aBitMask))
                        {
                            case (0xff, 0, 0, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R8_UNORM);
                                return;
                            default:
                                throw new DDSException($"Can't convert DDS format 8-R{dds.header.ddspf.rBitMask:x}G{dds.header.ddspf.gBitMask:x}B{dds.header.ddspf.bBitMask:x}A{dds.header.ddspf.aBitMask:x} to OpenGL!");
                        }
                }
            }
            else if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_ALPHAPIXELS) != 0
                || (dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_ALPHA) != 0)
            {
                // A-only
                if (dds.header.ddspf.rgbBitCount == 8)
                {
                    ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_A8_UNORM);
                    return;
                }
            }
            else if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_LUMINANCE) != 0)
            {
                // L-only
                switch (dds.header.ddspf.rgbBitCount)
                {
                    case 16:
                        switch ((dds.header.ddspf.rBitMask,
                            dds.header.ddspf.gBitMask,
                            dds.header.ddspf.bBitMask,
                            dds.header.ddspf.aBitMask))
                        {
                            case (0xffff, 0, 0, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R16_UNORM);
                                return;
                            case (0x00ff, 0, 0, 0xff00):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM);
                                return;
                            default:
                                throw new DDSException($"Can't convert DDS format 16-R{dds.header.ddspf.rBitMask:x}G{dds.header.ddspf.gBitMask:x}B{dds.header.ddspf.bBitMask:x}A{dds.header.ddspf.aBitMask:x} to OpenGL!");
                        }
                    case 8:
                        switch ((dds.header.ddspf.rBitMask,
                            dds.header.ddspf.gBitMask,
                            dds.header.ddspf.bBitMask,
                            dds.header.ddspf.aBitMask))
                        {
                            case (0xff, 0, 0, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R8_UNORM);
                                return;
                            case (0x0f, 0, 0, 0xf0):
                                // D3DFMT_A4L4
                                throw new DDSException($"Can't convert DDS format D3DFMT_A4L4 to OpenGL!");
                            case (0x00ff, 0, 0, 0xff00):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM);
                                return;
                            default:
                                throw new DDSException($"Can't convert DDS format 8-R{dds.header.ddspf.rBitMask:x}G{dds.header.ddspf.gBitMask:x}B{dds.header.ddspf.bBitMask:x}A{dds.header.ddspf.aBitMask:x} to OpenGL!");
                        }
                }
            }
            else if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_BUMPDUDV) != 0)
            {
                // Bump-only
                switch (dds.header.ddspf.rgbBitCount)
                {
                    case 32:
                        switch ((dds.header.ddspf.rBitMask,
                            dds.header.ddspf.gBitMask,
                            dds.header.ddspf.bBitMask,
                            dds.header.ddspf.aBitMask))
                        {
                            case (0x000000ff, 0x0000ff00, 0x00ff0000, 0xff000000):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM);
                                return;
                            case (0x0000ffff, 0xffff0000, 0, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM);
                                return;
                            case (0x3ff00000, 0x000ffc00, 0x000003ff, 0xc0000000):
                                // D3DFMT_A2W10V10U10
                                throw new DDSException($"Can't convert DDS format D3DFMT_A2W10V10U10 to OpenGL!");
                            default:
                                throw new DDSException($"Can't convert DDS format 32-R{dds.header.ddspf.rBitMask:x}G{dds.header.ddspf.gBitMask:x}B{dds.header.ddspf.bBitMask:x}A{dds.header.ddspf.aBitMask:x} to OpenGL!");
                        }
                    case 16:
                        switch ((dds.header.ddspf.rBitMask,
                            dds.header.ddspf.gBitMask,
                            dds.header.ddspf.bBitMask,
                            dds.header.ddspf.aBitMask))
                        {
                            case (0x00ff, 0xff00, 0, 0):
                                ConvertDXGIFormat(ref tex, DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM);
                                return;
                            default:
                                throw new DDSException($"Can't convert DDS format 16-R{dds.header.ddspf.rBitMask:x}G{dds.header.ddspf.gBitMask:x}B{dds.header.ddspf.bBitMask:x}A{dds.header.ddspf.aBitMask:x} to OpenGL!");
                        }
                }
            }
            else if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_YUV) != 0)
            {
                // YUV
                throw new DDSException($"Can't convert DDS format YUV-{dds.header.ddspf.rgbBitCount}-R{dds.header.ddspf.rBitMask:x}G{dds.header.ddspf.gBitMask:x}B{dds.header.ddspf.bBitMask:x}A{dds.header.ddspf.aBitMask:x} to OpenGL!");
            }
            else
            {
                throw new DDSException($"Can't convert DDS format flags 0x{dds.header.ddspf.flags:X8} to OpenGL!");
            }
        }

        private static void ConvertDXGIFormat(ref DDSTexture tex, DXGI_FORMAT fmt)
        {
            // Please note that many of these are just wild guesses, I'm not too familiar with the less common d3d and ogl texture formats...
            switch (fmt)
            {
                case DXGI_FORMAT.DXGI_FORMAT_UNKNOWN:
                    goto NotImplemented;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba32ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba32f;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Float;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba32ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba32i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Int;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgb;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgb32ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgb;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgb32f;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Float;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgb;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgb32ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32B32_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgb;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgb32i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Int;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba16;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba16f;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.HalfFloat;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba16;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba16ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba16SNorm;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Short;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba16i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Short;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG32ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG32f;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Float;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG32ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G32_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG32i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Int;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32G8X24_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT_S8X24_UINT:
                case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_X32_TYPELESS_G8X24_UINT:
                case DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UNORM:
                case DXGI_FORMAT.DXGI_FORMAT_R10G10B10A2_UINT:
                case DXGI_FORMAT.DXGI_FORMAT_R11G11B10_FLOAT:
                    goto NotImplemented;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
                    // Technically wrong
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8SNorm;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Byte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Byte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG16;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG16f;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.HalfFloat;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG16;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG16ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG16SNorm;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Short;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16G16_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG16;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R32ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.DepthComponent;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.DepthComponent32f;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Float;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R32f;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Float;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R32ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R32_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R32i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Int;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R24G8_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_D24_UNORM_S8_UINT:
                case DXGI_FORMAT.DXGI_FORMAT_R24_UNORM_X8_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_X24_TYPELESS_G8_UINT:
                    goto NotImplemented;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG8ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG8SNorm;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Int;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8G8_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.RG;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.RG8i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Int;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R16;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R16f;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.HalfFloat;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_D16_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.DepthComponent;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.DepthComponent16;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R16;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R16ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16_SNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R16SNorm;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Short;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R16_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R16i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Short;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8_TYPELESS:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8_UINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R8ui;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8_SNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R8SNorm;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Byte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R8_SINT:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Red;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.R8i;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Byte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_A8_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Alpha;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Alpha8Ext;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R1_UNORM:
                case DXGI_FORMAT.DXGI_FORMAT_R9G9B9E5_SHAREDEXP:
                case DXGI_FORMAT.DXGI_FORMAT_R8G8_B8G8_UNORM:
                case DXGI_FORMAT.DXGI_FORMAT_G8R8_G8B8_UNORM:
                    goto NotImplemented;
                case DXGI_FORMAT.DXGI_FORMAT_BC1_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedRgbaS3TCDxt1Ext;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    tex.compression = TextureCompressionType.BC1;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedSrgbAlphaS3TCDxt1Ext;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    tex.compression = TextureCompressionType.BC1;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC2_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedRgbaS3TCDxt3Ext;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    tex.compression = TextureCompressionType.BC2;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM_SRGB:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedSrgbAlphaS3TCDxt3Ext;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    tex.compression = TextureCompressionType.BC2;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC3_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedRgbaS3TCDxt5Ext;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    tex.compression = TextureCompressionType.BC3;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedSrgbAlphaS3TCDxt5Ext;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    tex.compression = TextureCompressionType.BC3;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC4_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM:
                case DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM:
                case DXGI_FORMAT.DXGI_FORMAT_BC5_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM:
                case DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM:
                    goto NotImplemented;
                case DXGI_FORMAT.DXGI_FORMAT_B5G6R5_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Bgr;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgb565;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort565Rev;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_B5G5R5A1_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Bgra;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgb5A1;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort5551;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM:
                case DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Bgra;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt8888Rev;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_R10G10B10_XR_BIAS_A2_UNORM:
                    goto NotImplemented;
                case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
                case DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_B8G8R8X8_UNORM_SRGB:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Bgra;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba8;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedInt8888Rev;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC6H_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedRgbBptcUnsignedFloat;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Float;
                    tex.compression = TextureCompressionType.BC6H;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedRgbBptcSignedFloat;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.Float;
                    tex.compression = TextureCompressionType.BC6H;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC7_TYPELESS:
                case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedRgbaBptcUnorm;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    tex.compression = TextureCompressionType.BC7;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Rgba;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.CompressedSrgbAlphaBptcUnorm;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedByte;
                    tex.compression = TextureCompressionType.BC7;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_AYUV:
                case DXGI_FORMAT.DXGI_FORMAT_Y410:
                case DXGI_FORMAT.DXGI_FORMAT_Y416:
                case DXGI_FORMAT.DXGI_FORMAT_NV12:
                case DXGI_FORMAT.DXGI_FORMAT_P010:
                case DXGI_FORMAT.DXGI_FORMAT_P016:
                case DXGI_FORMAT.DXGI_FORMAT_420_OPAQUE:
                case DXGI_FORMAT.DXGI_FORMAT_YUY2:
                case DXGI_FORMAT.DXGI_FORMAT_Y210:
                case DXGI_FORMAT.DXGI_FORMAT_Y216:
                case DXGI_FORMAT.DXGI_FORMAT_NV11:
                case DXGI_FORMAT.DXGI_FORMAT_AI44:
                case DXGI_FORMAT.DXGI_FORMAT_IA44:
                case DXGI_FORMAT.DXGI_FORMAT_P8:
                case DXGI_FORMAT.DXGI_FORMAT_A8P8:
                    goto NotImplemented;
                case DXGI_FORMAT.DXGI_FORMAT_B4G4R4A4_UNORM:
                    tex.format = Silk.NET.OpenGL.PixelFormat.Bgra;
                    tex.internalFormat = Silk.NET.OpenGL.InternalFormat.Rgba4;
                    tex.pixelType = Silk.NET.OpenGL.PixelType.UnsignedShort4444Rev;
                    break;
                case DXGI_FORMAT.DXGI_FORMAT_P208:
                case DXGI_FORMAT.DXGI_FORMAT_V208:
                case DXGI_FORMAT.DXGI_FORMAT_V408:
                case DXGI_FORMAT.DXGI_FORMAT_SAMPLER_FEEDBACK_MIN_MIP_OPAQUE:
                case DXGI_FORMAT.DXGI_FORMAT_SAMPLER_FEEDBACK_MIP_REGION_USED_OPAQUE:
                case DXGI_FORMAT.DXGI_FORMAT_FORCE_UINT:
                    goto NotImplemented;
            }

            return;

        NotImplemented:
            throw new DDSException($"Can't convert DXGI format '{fmt}' to OpenGL!");
        }

        // https://github.com/microsoft/DirectXTex/blob/main/DDSTextureLoader/DDSTextureLoader11.cpp#L493
        private static ulong ComputeDDSBytes(DDSFile dds, uint width, uint height)
        {
            bool blockCompression = false;
            bool packed = false;
            bool planar = false;
            uint bpe = 0;
            if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_FOURCC) != 0)
            {
                switch (dds.header.ddspf.fourCC)
                {
                    case DDSFourCC.DXT1:
                        blockCompression = true;
                        bpe = 8;
                        break;
                    case DDSFourCC.DXT2:
                    case DDSFourCC.DXT3:
                    case DDSFourCC.DXT4:
                    case DDSFourCC.DXT5:
                        blockCompression = true;
                        bpe = 16;
                        break;
                    case DDSFourCC.G8R8_G8B8:
                    case DDSFourCC.R8G8_B8G8:
                    case DDSFourCC.UYVY:
                    case DDSFourCC.YUY2:
                        packed = true;
                        bpe = 4;
                        break;
                    case DDSFourCC.DX10:
                        switch (dds.dx10Header.dxgiFormat)
                        {
                            case DXGI_FORMAT.DXGI_FORMAT_BC1_TYPELESS:
                            case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB:
                            case DXGI_FORMAT.DXGI_FORMAT_BC4_TYPELESS:
                            case DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM:
                                blockCompression = true;
                                bpe = 8;
                                break;
                            case DXGI_FORMAT.DXGI_FORMAT_BC2_TYPELESS:
                            case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM_SRGB:
                            case DXGI_FORMAT.DXGI_FORMAT_BC3_TYPELESS:
                            case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB:
                            case DXGI_FORMAT.DXGI_FORMAT_BC5_TYPELESS:
                            case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_BC6H_TYPELESS:
                            case DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16:
                            case DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16:
                            case DXGI_FORMAT.DXGI_FORMAT_BC7_TYPELESS:
                            case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB:
                                blockCompression = true;
                                bpe = 16;
                                break;
                            case DXGI_FORMAT.DXGI_FORMAT_R8G8_B8G8_UNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_G8R8_G8B8_UNORM:
                            case DXGI_FORMAT.DXGI_FORMAT_YUY2:
                                packed = true;
                                bpe = 4;
                                break;
                            case DXGI_FORMAT.DXGI_FORMAT_Y210:
                            case DXGI_FORMAT.DXGI_FORMAT_Y216:
                                packed = true;
                                bpe = 8;
                                break;
                            case DXGI_FORMAT.DXGI_FORMAT_NV12:
                            case DXGI_FORMAT.DXGI_FORMAT_420_OPAQUE:
                                if ((height % 2) != 0)
                                    throw new DDSException("DDS YUV format must have a height which is a multiple of 2!");
                                planar = true;
                                bpe = 2;
                                break;
                            case DXGI_FORMAT.DXGI_FORMAT_P010:
                            case DXGI_FORMAT.DXGI_FORMAT_P016:
                                if ((height % 2) != 0)
                                    throw new DDSException("DDS YUV format must have a height which is a multiple of 2!");
                                planar = true;
                                bpe = 4;
                                break;

                        }
                        break;
                }
            }

            if (blockCompression)
            {
                ulong blocksWide = width > 0 ? Math.Max(1, (width + 3) / 4) : 0;
                ulong blocksHigh = height > 0 ? Math.Max(1, (height + 3) / 4) : 0;
                return blocksWide * blocksHigh * bpe;
            }
            else if (packed)
            {
                ulong rowBytes = ((width + 1u) >> 1) * bpe;
                return rowBytes * height;
            } 
            else if (planar)
            {
                ulong rowBytes = ((width + 1u) >> 1) * bpe;
                return (rowBytes * height) + ((rowBytes * height + 1u) >> 1);
            } else
            {
                return (width * height * dds.header.ddspf.rgbBitCount + 7) / 8;
            }
        }

        /// <summary>
        /// Returns how many bits per pixel a given <see cref="D3DFORMAT"/> uses. Note that compressed formats may use less than 1 byte per pixel.
        /// </summary>
        /// <param name="fmt">the format to query</param>
        /// <returns>the number of bits per pixel for that format.</returns>
        private static int BitsPerPixel(D3DFORMAT fmt)
        {
            switch (fmt)
            {
                // 4 bpp
                case D3DFORMAT.D3DFMT_DXT1:
                case D3DFORMAT.D3DFMT_DXT2:
                case D3DFORMAT.D3DFMT_DXT3:
                case D3DFORMAT.D3DFMT_DXT4:
                case D3DFORMAT.D3DFMT_DXT5:
                    return 4;
                // 8 bpp
                case D3DFORMAT.D3DFMT_R3G3B2:
                case D3DFORMAT.D3DFMT_A8:
                case D3DFORMAT.D3DFMT_P8:
                case D3DFORMAT.D3DFMT_L8:
                    return 8;
                // 16 bpp
                case D3DFORMAT.D3DFMT_R5G6B5:
                case D3DFORMAT.D3DFMT_X1R5G5B5:
                case D3DFORMAT.D3DFMT_A1R5G5B5:
                case D3DFORMAT.D3DFMT_A4R4G4B4:
                case D3DFORMAT.D3DFMT_A8R3G3B2:
                case D3DFORMAT.D3DFMT_X4R4G4B4:
                case D3DFORMAT.D3DFMT_A8P8:
                case D3DFORMAT.D3DFMT_A8L8:
                case D3DFORMAT.D3DFMT_A4L4:
                case D3DFORMAT.D3DFMT_V8U8:
                case D3DFORMAT.D3DFMT_L6V5U5:
                case D3DFORMAT.D3DFMT_R8G8_B8G8:
                case D3DFORMAT.D3DFMT_YUY2:
                case D3DFORMAT.D3DFMT_G8R8_G8B8:
                case D3DFORMAT.D3DFMT_D16_LOCKABLE:
                case D3DFORMAT.D3DFMT_D15S1:
                case D3DFORMAT.D3DFMT_D16:
                case D3DFORMAT.D3DFMT_L16:
                case D3DFORMAT.D3DFMT_INDEX16:
                case D3DFORMAT.D3DFMT_R16F:
                case D3DFORMAT.D3DFMT_CxV8U8:
                    return 16;
                // 24 bpp
                case D3DFORMAT.D3DFMT_R8G8B8:
                    return 24;
                // 32 bpp
                case D3DFORMAT.D3DFMT_A8R8G8B8:
                case D3DFORMAT.D3DFMT_X8R8G8B8:
                case D3DFORMAT.D3DFMT_A2B10G10R10:
                case D3DFORMAT.D3DFMT_A8B8G8R8:
                case D3DFORMAT.D3DFMT_X8B8G8R8:
                case D3DFORMAT.D3DFMT_G16R16:
                case D3DFORMAT.D3DFMT_A2R10G10B10:
                case D3DFORMAT.D3DFMT_X8L8V8U8:
                case D3DFORMAT.D3DFMT_Q8W8V8U8:
                case D3DFORMAT.D3DFMT_V16U16:
                case D3DFORMAT.D3DFMT_A2W10V10U10:
                case D3DFORMAT.D3DFMT_UYVY:
                case D3DFORMAT.D3DFMT_D32:
                case D3DFORMAT.D3DFMT_D24S8:
                case D3DFORMAT.D3DFMT_D24X8:
                case D3DFORMAT.D3DFMT_D24X4S4:
                case D3DFORMAT.D3DFMT_D32F_LOCKABLE:
                case D3DFORMAT.D3DFMT_D24FS8:
                case D3DFORMAT.D3DFMT_INDEX32:
                case D3DFORMAT.D3DFMT_MULTI2_ARGB8: // Maybe?
                case D3DFORMAT.D3DFMT_G16R16F:
                case D3DFORMAT.D3DFMT_R32F:
                    return 32;
                // 64 bpp
                case D3DFORMAT.D3DFMT_A16B16G16R16:
                case D3DFORMAT.D3DFMT_Q16W16V16U16:
                case D3DFORMAT.D3DFMT_A16B16G16R16F:
                case D3DFORMAT.D3DFMT_G32R32F:
                    return 64;
                // 128 bpp
                case D3DFORMAT.D3DFMT_A32B32G32R32F:
                    return 128;
                // 0 bpp
                case D3DFORMAT.D3DFMT_UNKNOWN:
                case D3DFORMAT.D3DFMT_VERTEXDATA:
                    return 0;
                default:
                    return 0;
            }
        }

        private static bool IsCompressed(DDSFile dds)
        {
            if ((dds.header.ddspf.flags & DDSPixelFormatFlags.DDPF_FOURCC) == 0)
                return false;

            if (dds.header.ddspf.fourCC == DDSFourCC.DX10)
            {
                switch (dds.dx10Header.dxgiFormat)
                {
                    case DXGI_FORMAT.DXGI_FORMAT_BC1_TYPELESS:
                    case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM:
                    case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB:
                    case DXGI_FORMAT.DXGI_FORMAT_BC2_TYPELESS:
                    case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM:
                    case DXGI_FORMAT.DXGI_FORMAT_BC2_UNORM_SRGB:
                    case DXGI_FORMAT.DXGI_FORMAT_BC3_TYPELESS:
                    case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM:
                    case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB:
                    case DXGI_FORMAT.DXGI_FORMAT_BC4_TYPELESS:
                    case DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM:
                    case DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM:
                    case DXGI_FORMAT.DXGI_FORMAT_BC5_TYPELESS:
                    case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM:
                    case DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM:
                    case DXGI_FORMAT.DXGI_FORMAT_BC6H_TYPELESS:
                    case DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16:
                    case DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16:
                    case DXGI_FORMAT.DXGI_FORMAT_BC7_TYPELESS:
                    case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM:
                    case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB:
                        return true;
                    default:
                        return false;
                }
            }

            switch (dds.header.ddspf.fourCC)
            {
                case DDSFourCC.DXT1:
                case DDSFourCC.DXT2:
                case DDSFourCC.DXT3:
                case DDSFourCC.DXT4:
                case DDSFourCC.DXT5:
                    return true;
                default:
                    return false;
            }
        }
    }

    public class DDSException : Exception
    {
        public DDSException() { }
        public DDSException(string message) : base(message) { }
    }
}
