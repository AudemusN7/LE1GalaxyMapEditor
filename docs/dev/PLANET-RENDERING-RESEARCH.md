# Planet Rendering Research

This file preserves the useful findings from the retired planet-material research and standalone renderer prototypes. The production implementation now lives in `LE1GalaxyMapEditor.Rendering` and the Planet Designer.

## Recovered material model

`GXM10_PlanetMaster01` uses seven texture inputs: `Normal_Map`, `City_Emissive`, `ContinentMask01`, `ContinentMask02`, `Continent_Texture`, `Ocean_Texture`, and `AtmosphereMaster`. Masks and diffuse textures use sRGB sampling; the BC5 normal map does not.

Observed texture scales are:

- continent masks ×10;
- continent colour ×15;
- ocean colour ×22;
- atmosphere × parameter tile ×30;
- city masks ×9 and × `City_Emissive_Tile` ×50;
- normal map × `Normal_Map_Tile` ×60.

Atmosphere combines direct and view-offset samples, then raises its edge mask to the tenth power. City emission is restricted to the narrow coastal/upper-land transition. The first city lookup rotates slowly in the game; the preview keeps it static because the effect is negligible at normal scale.

## Lighting and scene assumptions

The preview reproduces the recovered upper/lower SkyLight response and two unshadowed point lights from `EntryMenu`. The SkyLight uses brightness `0.25` and byte colour `R=171, G=189, B=197`; its lower hemisphere is black.

Point-light attenuation follows:

```text
max(1 - distanceSquared / radiusSquared, 0.0001)^FalloffExponent
```

Both lights use falloff exponent `2`. Their runtime colour and intensity come from `SunColor1/2` and `Brightness1/2`; the first packed slot is unused.

The extracted planet and corona meshes retain their original UVs, transforms, and tangent handedness. The star field is a screen-space backdrop rather than a 3D skybox.

## Corona and post-processing

`GXM10_CoronoMaster01` is an independent unlit additive pass using the corona gradient, `Corona_Color`, opacity, and fringe bloom. The preview uses ordinary planet-depth occlusion instead of UE3's soft intersection fade.

The production bloom and display grade are measured approximations. They do not attempt to recreate LE1's complete adaptive exposure and HDR post-processing chain.

## Remaining unknowns

- The runtime path that assigns 2DA light values was not located, although its observed result is reproduced.
- `LightEnv_Bounced*` properties do not appear in the recovered direct point-light shader and are not simulated.
- `Shader` appears to identify a runtime-created material instance; its construction path is unnecessary for previewing the recovered parameters.
