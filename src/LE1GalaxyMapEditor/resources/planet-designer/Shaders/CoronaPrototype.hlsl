cbuffer SceneConstants : register(b0)
{
    row_major float4x4 WorldViewProjection;
    row_major float4x4 World;
    float4 CameraPosition;
    float4 RenderOptions;
};

Texture2D CoronaGradient : register(t0);
SamplerState SurfaceSampler : register(s0);

cbuffer CoronaMaterialConstants : register(b1)
{
    float4 CoronaColor;
    // x: Fringe_Bloom, y: Opacity
    float4 CoronaScalars;
};

#define FringeBloom CoronaScalars.x
#define Opacity CoronaScalars.y

struct VertexInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float4 Tangent : TANGENT;
    float2 UV : TEXCOORD0;
};

struct PixelInput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
};

PixelInput VSMain(VertexInput input)
{
    PixelInput output;
    output.Position = mul(float4(input.Position, 1), WorldViewProjection);
    output.UV = input.UV;
    return output;
}

float4 PSMain(PixelInput input) : SV_TARGET
{
    float gradient = CoronaGradient.Sample(SurfaceSampler, input.UV).r;
    float fringe = FringeBloom * floor(gradient + 0.35);
    float3 linearCorona = max((gradient * CoronaColor + fringe) * Opacity, 0);

    // The recovered material output is linear/HDR, while this prototype writes
    // directly to an ordinary display PNG. This compact transfer matches the
    // supplied no-postprocess capture without changing the material formula.
    float3 displayCorona = pow(saturate(linearCorona * 0.64), 1.0 / 1.4);

    // Approximate the small, desaturating bloom visible in the game's
    // postprocessed capture. A full-screen HDR bloom pass can replace this if
    // the editor eventually needs exact scene postprocessing.
    if (RenderOptions.y > 0.5)
    {
        float bloom = displayCorona.b * 0.1;
        displayCorona = saturate(displayCorona + bloom.xxx);
    }

    return float4(displayCorona, 0);
}
