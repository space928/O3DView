using System;
using System.Runtime.InteropServices;

namespace O3DView;

public struct DDSFile
{
    public DDSHeader header;
    public DDSHeaderDXT10 dx10Header;
}

public struct DDSTexture
{
    public TextureType textureType;
    public uint width;
    public uint height;
    public uint mipmaps;
    public uint elements;
    public Silk.NET.OpenGL.PixelFormat format;
    public Silk.NET.OpenGL.InternalFormat internalFormat;
    public Silk.NET.OpenGL.PixelType pixelType;
    public TextureCompressionType compression;

    public Element[] data;

    public struct Element
    {
        public MipMap[] mipMaps;
    }

    public struct MipMap
    {
        public uint width; 
        public uint height;
        public byte[] data;
    }
}

public enum TextureType
{
    //Texture1D,
    Texture2D,
    Texture2DArray,
    Texture3D,
    TextureCube,
    TextureCubeArray,
}

[StructLayout(LayoutKind.Explicit, Size = 0x7c)]
public struct DDSHeader
{
    [FieldOffset(0x0)] public uint size;
    [FieldOffset(0x4)] public DDSFlags flags;
    [FieldOffset(0x8)] public uint height;
    [FieldOffset(0xc)] public uint width;
    [FieldOffset(0x10)] public uint pitchOrLinearSize;
    [FieldOffset(0x14)] public uint depth;
    [FieldOffset(0x18)] public uint mipMapCount;
    //[FieldOffset(0x1c)]public uint[11] reserved1;
    [FieldOffset(0x48)] public DDSPixelFormat ddspf;
    [FieldOffset(0x68)] public DDSCaps1 caps;
    [FieldOffset(0x6c)] public DDSCaps2 caps2;
    //[FieldOffset(0x70)]public uint caps3;
    //[FieldOffset(0x74)]public uint caps4;
    //[FieldOffset(0x78)]public uint reserved2;
}

public struct DDSHeaderDXT10 {
    public DXGI_FORMAT dxgiFormat;
    public D3D10_RESOURCE_DIMENSION resourceDimension;
    public uint miscFlag;
    public uint arraySize;
    public uint miscFlags2;
}

[StructLayout(LayoutKind.Sequential, Size = 0x20)]
public struct DDSPixelFormat
{
    public uint size;
    public DDSPixelFormatFlags flags;
    public DDSFourCC fourCC;
    public uint rgbBitCount;
    public uint rBitMask;
    public uint gBitMask;
    public uint bBitMask;
    public uint aBitMask;
}

public enum TextureCompressionType
{
    None,
    BC1,
    BC2,
    BC3,
    BC4,
    BC5,
    BC6H,
    BC7,

    DXT1 = BC1,
    DXT2 = BC2,
    DXT3 = BC2,
    DXT4 = BC3,
    DXT5 = BC3,
}

[Flags]
public enum DDSFlags : uint
{
    /// <summary>
    /// Required in every .dds file.
    /// </summary>
    DDSD_CAPS = 0x1,
    /// <summary>
    /// Required in every .dds file.
    /// </summary>
    DDSD_HEIGHT = 0x2,
    /// <summary>
    /// Required in every .dds file.
    /// </summary>
    DDSD_WIDTH = 0x4,
    /// <summary>
    /// Required when pitch is provided for an uncompressed texture.
    /// </summary>
    DDSD_PITCH = 0x8,
    /// <summary>
    /// Required in every .dds file.
    /// </summary>
    DDSD_PIXELFORMAT = 0x1000,
    /// <summary>
    /// Required in a mipmapped texture.
    /// </summary>
    DDSD_MIPMAPCOUNT = 0x20000,
    /// <summary>
    /// Required when pitch is provided for a compressed texture.
    /// </summary>
    DDSD_LINEARSIZE = 0x80000,
    /// <summary>
    /// Required in a depth texture.
    /// </summary>
    DDSD_DEPTH = 0x800000
}

[Flags]
public enum DDSCaps1 : uint
{
    /// <summary>
    /// Optional; must be used on any file that contains more than one surface 
    /// (a mipmap, a cubic environment map, or mipmapped volume texture).
    /// </summary>
    DDSCAPS_COMPLEX = 0x8,
    /// <summary>
    /// Optional; should be used for a mipmap.
    /// </summary>
    DDSCAPS_MIPMAP = 0x400000,
    /// <summary>
    /// Required
    /// </summary>
    DDSCAPS_TEXTURE = 0x1000,

    DDS_SURFACE_FLAGS_MIPMAP = DDSCAPS_COMPLEX | DDSCAPS_MIPMAP,
    DDS_SURFACE_FLAGS_TEXTURE = DDSCAPS_TEXTURE,
    DDS_SURFACE_FLAGS_CUBEMAP = DDSCAPS_COMPLEX
}

[Flags]
public enum DDSCaps2 : uint
{
    /// <summary>
    /// Required for a cube map.
    /// </summary>
    DDSCAPS2_CUBEMAP = 0x200,
    /// <summary>
    /// Required when these surfaces are stored in a cube map.
    /// </summary>
    DDSCAPS2_CUBEMAP_POSITIVEX = 0x400,
    /// <summary>
    /// Required when these surfaces are stored in a cube map.
    /// </summary>
    DDSCAPS2_CUBEMAP_NEGATIVEX = 0x800,
    /// <summary>
    /// Required when these surfaces are stored in a cube map.
    /// </summary>
    DDSCAPS2_CUBEMAP_POSITIVEY = 0x1000,
    /// <summary>
    /// Required when these surfaces are stored in a cube map.
    /// </summary>
    DDSCAPS2_CUBEMAP_NEGATIVEY = 0x2000,
    /// <summary>
    /// Required when these surfaces are stored in a cube map.
    /// </summary>
    DDSCAPS2_CUBEMAP_POSITIVEZ = 0x4000,
    /// <summary>
    /// Required when these surfaces are stored in a cube map.
    /// </summary>
    DDSCAPS2_CUBEMAP_NEGATIVEZ = 0x8000,
    /// <summary>
    /// Required for a volume texture.
    /// </summary>
    DDSCAPS2_VOLUME = 0x200000,

    DDS_CUBEMAP_POSITIVEX = DDSCAPS2_CUBEMAP | DDSCAPS2_CUBEMAP_POSITIVEX,
    DDS_CUBEMAP_POSITIVEY = DDSCAPS2_CUBEMAP | DDSCAPS2_CUBEMAP_POSITIVEY,
    DDS_CUBEMAP_POSITIVEZ = DDSCAPS2_CUBEMAP | DDSCAPS2_CUBEMAP_POSITIVEZ,
    DDS_CUBEMAP_NEGATIVEX = DDSCAPS2_CUBEMAP | DDSCAPS2_CUBEMAP_NEGATIVEX,
    DDS_CUBEMAP_NEGATIVEY = DDSCAPS2_CUBEMAP | DDSCAPS2_CUBEMAP_NEGATIVEY,
    DDS_CUBEMAP_NEGATIVEZ = DDSCAPS2_CUBEMAP | DDSCAPS2_CUBEMAP_NEGATIVEZ,
    DDS_CUBEMAP_ALLFACES = DDS_CUBEMAP_POSITIVEX
        | DDS_CUBEMAP_POSITIVEY
        | DDS_CUBEMAP_POSITIVEZ
        | DDS_CUBEMAP_NEGATIVEX
        | DDS_CUBEMAP_NEGATIVEY
        | DDS_CUBEMAP_NEGATIVEZ,
    DDS_FLAGS_VOLUME = DDSCAPS2_VOLUME
}

[Flags]
public enum DDSPixelFormatFlags : uint
{
    /// <summary>
    /// Texture contains alpha data; dwRGBAlphaBitMask contains valid data.
    /// </summary>
    DDPF_ALPHAPIXELS = 0x1,
    /// <summary>
    /// Used in some older DDS files for alpha channel only uncompressed data 
    /// (dwRGBBitCount contains the alpha channel bitcount; dwABitMask contains 
    /// valid data)
    /// </summary>
    DDPF_ALPHA = 0x2,
    /// <summary>
    /// Texture contains compressed RGB data; dwFourCC contains valid data.
    /// </summary>
    DDPF_FOURCC = 0x4,
    /// <summary>
    /// Texture contains uncompressed RGB data; dwRGBBitCount and the RGB masks 
    /// (dwRBitMask, dwGBitMask, dwBBitMask) contain valid data.
    /// </summary>
    DDPF_RGB = 0x40,
    /// <summary>
    /// Used in some older DDS files for YUV uncompressed data (dwRGBBitCount 
    /// contains the YUV bit count; dwRBitMask contains the Y mask, dwGBitMask 
    /// contains the U mask, dwBBitMask contains the V mask)
    /// </summary>
    DDPF_YUV = 0x200,
    /// <summary>
    /// Used in some older DDS files for single channel color uncompressed data 
    /// (dwRGBBitCount contains the luminance channel bit count; dwRBitMask 
    /// contains the channel mask). Can be combined with DDPF_ALPHAPIXELS for 
    /// a two channel DDS file.
    /// </summary>
    DDPF_LUMINANCE = 0x20000,
    DDPF_BUMPDUDV = 0x80000
}

public enum DDSFourCC : uint
{
    UYVY = ((byte)'U') | ((byte)'Y' << 8) | ((byte)'V' << 16) | ((byte)'Y' << 24),//MAKEFOURCC('U', 'Y', 'V', 'Y'),
    R8G8_B8G8 = ((byte)'R') | ((byte)'G' << 8) | ((byte)'B' << 16) | ((byte)'G' << 24),
    YUY2 = ((byte)'Y') | ((byte)'U' << 8) | ((byte)'Y' << 16) | ((byte)'2' << 24),
    G8R8_G8B8 = ((byte)'G') | ((byte)'R' << 8) | ((byte)'G' << 16) | ((byte)'B' << 24),
    DXT1 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'1' << 24),
    DXT2 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'2' << 24),
    DXT3 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'3' << 24),
    DXT4 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'4' << 24),
    DXT5 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'5' << 24),

    DX10 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'1' << 16) | ((byte)'0' << 24),
}

public enum D3DFORMAT : uint
{
    D3DFMT_UNKNOWN = 0,

    D3DFMT_R8G8B8 = 20,
    D3DFMT_A8R8G8B8 = 21,
    D3DFMT_X8R8G8B8 = 22,
    D3DFMT_R5G6B5 = 23,
    D3DFMT_X1R5G5B5 = 24,
    D3DFMT_A1R5G5B5 = 25,
    D3DFMT_A4R4G4B4 = 26,
    D3DFMT_R3G3B2 = 27,
    D3DFMT_A8 = 28,
    D3DFMT_A8R3G3B2 = 29,
    D3DFMT_X4R4G4B4 = 30,
    D3DFMT_A2B10G10R10 = 31,
    D3DFMT_A8B8G8R8 = 32,
    D3DFMT_X8B8G8R8 = 33,
    D3DFMT_G16R16 = 34,
    D3DFMT_A2R10G10B10 = 35,
    D3DFMT_A16B16G16R16 = 36,

    D3DFMT_A8P8 = 40,
    D3DFMT_P8 = 41,

    D3DFMT_L8 = 50,
    D3DFMT_A8L8 = 51,
    D3DFMT_A4L4 = 52,

    D3DFMT_V8U8 = 60,
    D3DFMT_L6V5U5 = 61,
    D3DFMT_X8L8V8U8 = 62,
    D3DFMT_Q8W8V8U8 = 63,
    D3DFMT_V16U16 = 64,
    D3DFMT_A2W10V10U10 = 67,

    D3DFMT_UYVY = ((byte)'U') | ((byte)'Y' << 8) | ((byte)'V' << 16) | ((byte)'Y' << 24),//MAKEFOURCC('U', 'Y', 'V', 'Y'),
    D3DFMT_R8G8_B8G8 = ((byte)'R') | ((byte)'G' << 8) | ((byte)'B' << 16) | ((byte)'G' << 24),
    D3DFMT_YUY2 = ((byte)'Y') | ((byte)'U' << 8) | ((byte)'Y' << 16) | ((byte)'2' << 24),
    D3DFMT_G8R8_G8B8 = ((byte)'G') | ((byte)'R' << 8) | ((byte)'G' << 16) | ((byte)'B' << 24),
    D3DFMT_DXT1 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'1' << 24),
    D3DFMT_DXT2 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'2' << 24),
    D3DFMT_DXT3 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'3' << 24),
    D3DFMT_DXT4 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'4' << 24),
    D3DFMT_DXT5 = ((byte)'D') | ((byte)'X' << 8) | ((byte)'T' << 16) | ((byte)'5' << 24),

    D3DFMT_D16_LOCKABLE = 70,
    D3DFMT_D32 = 71,
    D3DFMT_D15S1 = 73,
    D3DFMT_D24S8 = 75,
    D3DFMT_D24X8 = 77,
    D3DFMT_D24X4S4 = 79,
    D3DFMT_D16 = 80,

    D3DFMT_D32F_LOCKABLE = 82,
    D3DFMT_D24FS8 = 83,

    D3DFMT_L16 = 81,

    D3DFMT_VERTEXDATA = 100,
    D3DFMT_INDEX16 = 101,
    D3DFMT_INDEX32 = 102,

    D3DFMT_Q16W16V16U16 = 110,

    D3DFMT_MULTI2_ARGB8 = ((byte)'M') | ((byte)'E' << 8) | ((byte)'T' << 16) | ((byte)'1' << 24),

    // Floating point surface formats

    // s10e5 formats (16-bits per channel)
    D3DFMT_R16F = 111,
    D3DFMT_G16R16F = 112,
    D3DFMT_A16B16G16R16F = 113,

    // IEEE s23e8 formats (32-bits per channel)
    D3DFMT_R32F = 114,
    D3DFMT_G32R32F = 115,
    D3DFMT_A32B32G32R32F = 116,

    D3DFMT_CxV8U8 = 117,

    D3DFMT_FORCE_DWORD = 0x7fffffff
}

public enum DXGI_FORMAT : uint
{
    DXGI_FORMAT_UNKNOWN = 0,
    DXGI_FORMAT_R32G32B32A32_TYPELESS = 1,
    DXGI_FORMAT_R32G32B32A32_FLOAT = 2,
    DXGI_FORMAT_R32G32B32A32_UINT = 3,
    DXGI_FORMAT_R32G32B32A32_SINT = 4,
    DXGI_FORMAT_R32G32B32_TYPELESS = 5,
    DXGI_FORMAT_R32G32B32_FLOAT = 6,
    DXGI_FORMAT_R32G32B32_UINT = 7,
    DXGI_FORMAT_R32G32B32_SINT = 8,
    DXGI_FORMAT_R16G16B16A16_TYPELESS = 9,
    DXGI_FORMAT_R16G16B16A16_FLOAT = 10,
    DXGI_FORMAT_R16G16B16A16_UNORM = 11,
    DXGI_FORMAT_R16G16B16A16_UINT = 12,
    DXGI_FORMAT_R16G16B16A16_SNORM = 13,
    DXGI_FORMAT_R16G16B16A16_SINT = 14,
    DXGI_FORMAT_R32G32_TYPELESS = 15,
    DXGI_FORMAT_R32G32_FLOAT = 16,
    DXGI_FORMAT_R32G32_UINT = 17,
    DXGI_FORMAT_R32G32_SINT = 18,
    DXGI_FORMAT_R32G8X24_TYPELESS = 19,
    DXGI_FORMAT_D32_FLOAT_S8X24_UINT = 20,
    DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS = 21,
    DXGI_FORMAT_X32_TYPELESS_G8X24_UINT = 22,
    DXGI_FORMAT_R10G10B10A2_TYPELESS = 23,
    DXGI_FORMAT_R10G10B10A2_UNORM = 24,
    DXGI_FORMAT_R10G10B10A2_UINT = 25,
    DXGI_FORMAT_R11G11B10_FLOAT = 26,
    DXGI_FORMAT_R8G8B8A8_TYPELESS = 27,
    DXGI_FORMAT_R8G8B8A8_UNORM = 28,
    DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29,
    DXGI_FORMAT_R8G8B8A8_UINT = 30,
    DXGI_FORMAT_R8G8B8A8_SNORM = 31,
    DXGI_FORMAT_R8G8B8A8_SINT = 32,
    DXGI_FORMAT_R16G16_TYPELESS = 33,
    DXGI_FORMAT_R16G16_FLOAT = 34,
    DXGI_FORMAT_R16G16_UNORM = 35,
    DXGI_FORMAT_R16G16_UINT = 36,
    DXGI_FORMAT_R16G16_SNORM = 37,
    DXGI_FORMAT_R16G16_SINT = 38,
    DXGI_FORMAT_R32_TYPELESS = 39,
    DXGI_FORMAT_D32_FLOAT = 40,
    DXGI_FORMAT_R32_FLOAT = 41,
    DXGI_FORMAT_R32_UINT = 42,
    DXGI_FORMAT_R32_SINT = 43,
    DXGI_FORMAT_R24G8_TYPELESS = 44,
    DXGI_FORMAT_D24_UNORM_S8_UINT = 45,
    DXGI_FORMAT_R24_UNORM_X8_TYPELESS = 46,
    DXGI_FORMAT_X24_TYPELESS_G8_UINT = 47,
    DXGI_FORMAT_R8G8_TYPELESS = 48,
    DXGI_FORMAT_R8G8_UNORM = 49,
    DXGI_FORMAT_R8G8_UINT = 50,
    DXGI_FORMAT_R8G8_SNORM = 51,
    DXGI_FORMAT_R8G8_SINT = 52,
    DXGI_FORMAT_R16_TYPELESS = 53,
    DXGI_FORMAT_R16_FLOAT = 54,
    DXGI_FORMAT_D16_UNORM = 55,
    DXGI_FORMAT_R16_UNORM = 56,
    DXGI_FORMAT_R16_UINT = 57,
    DXGI_FORMAT_R16_SNORM = 58,
    DXGI_FORMAT_R16_SINT = 59,
    DXGI_FORMAT_R8_TYPELESS = 60,
    DXGI_FORMAT_R8_UNORM = 61,
    DXGI_FORMAT_R8_UINT = 62,
    DXGI_FORMAT_R8_SNORM = 63,
    DXGI_FORMAT_R8_SINT = 64,
    DXGI_FORMAT_A8_UNORM = 65,
    DXGI_FORMAT_R1_UNORM = 66,
    DXGI_FORMAT_R9G9B9E5_SHAREDEXP = 67,
    DXGI_FORMAT_R8G8_B8G8_UNORM = 68,
    DXGI_FORMAT_G8R8_G8B8_UNORM = 69,
    DXGI_FORMAT_BC1_TYPELESS = 70,
    DXGI_FORMAT_BC1_UNORM = 71,
    DXGI_FORMAT_BC1_UNORM_SRGB = 72,
    DXGI_FORMAT_BC2_TYPELESS = 73,
    DXGI_FORMAT_BC2_UNORM = 74,
    DXGI_FORMAT_BC2_UNORM_SRGB = 75,
    DXGI_FORMAT_BC3_TYPELESS = 76,
    DXGI_FORMAT_BC3_UNORM = 77,
    DXGI_FORMAT_BC3_UNORM_SRGB = 78,
    DXGI_FORMAT_BC4_TYPELESS = 79,
    DXGI_FORMAT_BC4_UNORM = 80,
    DXGI_FORMAT_BC4_SNORM = 81,
    DXGI_FORMAT_BC5_TYPELESS = 82,
    DXGI_FORMAT_BC5_UNORM = 83,
    DXGI_FORMAT_BC5_SNORM = 84,
    DXGI_FORMAT_B5G6R5_UNORM = 85,
    DXGI_FORMAT_B5G5R5A1_UNORM = 86,
    DXGI_FORMAT_B8G8R8A8_UNORM = 87,
    DXGI_FORMAT_B8G8R8X8_UNORM = 88,
    DXGI_FORMAT_R10G10B10_XR_BIAS_A2_UNORM = 89,
    DXGI_FORMAT_B8G8R8A8_TYPELESS = 90,
    DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91,
    DXGI_FORMAT_B8G8R8X8_TYPELESS = 92,
    DXGI_FORMAT_B8G8R8X8_UNORM_SRGB = 93,
    DXGI_FORMAT_BC6H_TYPELESS = 94,
    DXGI_FORMAT_BC6H_UF16 = 95,
    DXGI_FORMAT_BC6H_SF16 = 96,
    DXGI_FORMAT_BC7_TYPELESS = 97,
    DXGI_FORMAT_BC7_UNORM = 98,
    DXGI_FORMAT_BC7_UNORM_SRGB = 99,
    DXGI_FORMAT_AYUV = 100,
    DXGI_FORMAT_Y410 = 101,
    DXGI_FORMAT_Y416 = 102,
    DXGI_FORMAT_NV12 = 103,
    DXGI_FORMAT_P010 = 104,
    DXGI_FORMAT_P016 = 105,
    DXGI_FORMAT_420_OPAQUE = 106,
    DXGI_FORMAT_YUY2 = 107,
    DXGI_FORMAT_Y210 = 108,
    DXGI_FORMAT_Y216 = 109,
    DXGI_FORMAT_NV11 = 110,
    DXGI_FORMAT_AI44 = 111,
    DXGI_FORMAT_IA44 = 112,
    DXGI_FORMAT_P8 = 113,
    DXGI_FORMAT_A8P8 = 114,
    DXGI_FORMAT_B4G4R4A4_UNORM = 115,
    DXGI_FORMAT_P208 = 130,
    DXGI_FORMAT_V208 = 131,
    DXGI_FORMAT_V408 = 132,
    DXGI_FORMAT_SAMPLER_FEEDBACK_MIN_MIP_OPAQUE,
    DXGI_FORMAT_SAMPLER_FEEDBACK_MIP_REGION_USED_OPAQUE,
    DXGI_FORMAT_FORCE_UINT = 0xffffffff
}

public enum D3D10_RESOURCE_DIMENSION : uint
{
    D3D10_RESOURCE_DIMENSION_UNKNOWN = 0,
    D3D10_RESOURCE_DIMENSION_BUFFER = 1,
    D3D10_RESOURCE_DIMENSION_TEXTURE1D = 2,
    D3D10_RESOURCE_DIMENSION_TEXTURE2D = 3,
    D3D10_RESOURCE_DIMENSION_TEXTURE3D = 4
}
