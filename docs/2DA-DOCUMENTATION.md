# LE1 Galaxy Map 2DA System: Technical Reference

The Mass Effect 1 galaxy map is driven by six related 2DA tables: **Cluster**, **System**, **Planet**, **PlotPlanet**, **Map** and **Relay**.

Together they define the hierarchy, map placement, object appearance, availability rules, landable destinations and Relay network.

This page describes the LE1 data as observed in the vanilla tables and verified through practical mod authoring. It deliberately distinguishes established behaviour from patterns whose exact game-side implementation is still uncertain.

## Confidence labels

| Label | Meaning |
|---|---|
| **Confirmed** | The relationship or effect is directly established and safe to author against. |
| **Observed** | The description matches consistent vanilla data or tested behaviour, but the complete game-side implementation is not known. |
| **Unverified** | The column is retained by the format, but no reliable runtime effect has been established. |

## The six tables at a glance

| Table | Purpose | Main relationships |
|---|---|---|
| `GalaxyMap_Cluster` | Galaxy-level regions and their visual backgrounds. | Parent of System rows; numbered Label is encoded into Relays and ActiveWorld. |
| `GalaxyMap_System` | Star systems positioned inside Clusters. | `Cluster` points to a Cluster Row ID; numbered Label contributes to ActiveWorld. |
| `GalaxyMap_Planet` | Planets, anomalies, ships, asteroid belts, Relays, depots and suns. | `System` points to a System Row ID; `Map` optionally points to a Map Row ID. |
| `GalaxyMap_PlotPlanet` | Plot-facing registration for selected Planet rows - this is what makes a tracking tag appear above the planet (Citadel, Noveria, Liara's Dig Site, Asteroid X57 etc. | Uses the same Row ID as its Planet; `Code` mirrors Planet `ActiveWorld`. |
| `GalaxyMap_Map` | Persistent level and spawn point for a landable destination. | Referenced by `Planet.Map`. |
| `GalaxyMap_Relay` | Connections drawn between Clusters. | Endpoints encode Cluster Label suffixes, not Cluster Row IDs. |

## Row ID, Label and ActiveWorld are different things

These values are related, but they are not interchangeable.

### Row ID

Every 2DA begins with an unnamed A column. This is the real **Row ID**.

- Row IDs may be sparse.
- Parent references such as `System.Cluster`, `Planet.System` and `Planet.Map` use Row IDs.
- A later-mounted `_part` table can override the same ID row, or appending rows to the 2DA (via the `2DA Merge` feature when instaling a mod)
- A Row ID does not need to match the numeric suffix of `Label`.

### Numbered Label

Clusters, Systems and Planets also carry internal numbered labels:

- `ClusterNN`
- `SystemNN`
- `PlanetNN`

The suffixes form a second identity system used by Relay and ActiveWorld.

Cluster suffixes must be unique across the galaxy. System suffixes must be unique inside their parent Cluster, and Planet suffixes must be unique inside their parent System. System and Planet suffixes must fit the range 1–99.

### ActiveWorld

`Planet.ActiveWorld` combines the three Label suffixes:

```text
ActiveWorld = (Cluster suffix × 10,000)
            + (System suffix × 100)
            + Planet suffix
```

For `Cluster01 / System01 / Planet01`:

```text
(1 × 10,000) + (1 × 100) + 1 = 10,101
```

Example: Local Cluster is `Cluster03` and Sol is `System01` in that Cluster. Earth is `Planet03` in that System, so the final `ActiveWorld` code is `30101`

Changing a numbered Label or moving an object to another parent therefore requires the descendant ActiveWorld values and linked PlotPlanet codes to be updated.

## Parent and link relationships

| Source column | Target | Link type |
|---|---|---|
| `System.Cluster` | Cluster unnamed Row ID | Many Systems to one Cluster. |
| `Planet.System` | System unnamed Row ID | Many planets/objects to one System. |
| `Planet.Map` | Map unnamed Row ID | Optional destination link; `-1` means none. |
| PlotPlanet unnamed Row ID | Planet unnamed Row ID | Same-ID link. |
| `PlotPlanet.Code` | `Planet.ActiveWorld` | Must contain the same derived code. |
| `Relay.StartCluster` | Cluster Label suffix × 10,000 | Encoded endpoint. |
| `Relay.EndCluster` | Cluster Label suffix × 10,000 | Encoded endpoint. |

## Availability-rule triplets

Several tables use independent three-column rule groups:

```text
<Scope>Conditional
<Scope>Function
<Scope>Parameter
```

The scopes are:

| Scope | Purpose |
|---|---|
| `Visible` | Controls whether the entry is visible on the relevant galaxy-map level. |
| `Usable` | Controls whether the entry can be selected or used. |
| `UsablePlanet` | Controls the Planet interaction/use button. This scope exists only on Planet rows. |

`Conditional` and `Parameter` are independent binary values. Do not assume that they must match merely because they often do in vanilla data. `Function` is a non-negative function or plot identifier.

The established **Always** triplet is:

```text
1 / 974 / 1
```

Function `974` is a plot utility that essentially means "Always True" while `975` means "Always False".
Function `975` is used by vanilla hidden anomalies and asteroid-belt anchors. All three scopes on vanilla asteroid belts use function `975`, preventing the belt anchor from being exposed as an ordinary selectable object.

## Cluster table

`GalaxyMap_Cluster` defines the objects displayed on the top-level Galaxy map.

| Column | Purpose |
|---|---|
| unnamed first column | **Confirmed:** Cluster Row ID. Referenced by `System.Cluster`. |
| `Label` | **Confirmed:** internal `ClusterNN` label. Its suffix is multiplied by 10,000 for Relay endpoints and ActiveWorld. |
| `X`, `Y` | **Confirmed:** position on the Galaxy map, inside a grid measured 0–1. |
| `Name` | **Confirmed:** TLK string reference for the displayed Cluster name. |
| `NameText` | **Confirmed:** Editor only name. |
| `Colour`, `Colour2` | **Unverified:** packed visual colours. Their exact rendered targets are not established. |
| `NebularDensity` | **Observed:** Cluster visual-density parameter. Vanilla is usually `1`, with values from approximately `0.2` to `2`. |
| `CloudTile` | **Observed:** tiling parameter for the Cluster cloud effect. Vanilla normally uses `1`. |
| `SphereIntensity` | **Observed:** intensity parameter for the Cluster sphere/effect. Vanilla normally uses `3`. |
| `SphereSize` | **Confirmed:** visual size of the Cluster marker/map sphere. Vanilla is usually approximately `4`–`4.5`. |
| `VisibleConditional`, `VisibleFunction`, `VisibleParameter` | **Confirmed:** Cluster visibility rule. |
| `UsableConditional`, `UsableFunction`, `UsableParameter` | **Confirmed:** Cluster usability rule. |
| `background` | **Confirmed:** package-qualified Cluster texture reference used by the Cluster map and by child Systems with `ShowNebula = 1`. |

Example texture reference:

```text
BIOA_GalaxyMap_T.Cluster03
```

## System table

`GalaxyMap_System` defines star systems and places them inside Clusters.

| Column | Purpose |
|---|---|
| unnamed first column | **Confirmed:** System Row ID. Referenced by `Planet.System`. |
| `Label` | **Confirmed:** internal `SystemNN` label. Its suffix contributes the hundreds segment of ActiveWorld. |
| `Cluster` | **Confirmed:** parent Cluster Row ID. This does not use the Cluster Label suffix. |
| `X`, `Y` | **Confirmed:** position on the parent Cluster map, normally 0–1. |
| `Name` | **Confirmed:** TLK string reference for the displayed System name. |
| `NameText` | **Confirmed:** Editor only name. |
| `Colour`, `Colour2` | **Unverified:** packed System visual colours. |
| `FlareTint` | **Observed:** packed visual colour related to the system flare. It matches `Colour2` on 42 of 43 vanilla Systems. |
| `Scale` | **Confirmed:** scale of the navigable System canvas. Vanilla values range from approximately `0.1` to `2`. |
| `ExitMap` | **Observed unused:** every vanilla System uses `0`; no working System-level effect has been verified. |
| `VisibleConditional`, `VisibleFunction`, `VisibleParameter` | **Confirmed:** System visibility rule. |
| `UsableConditional`, `UsableFunction`, `UsableParameter` | **Confirmed:** System usability rule. |
| `ShowNebula` | **Confirmed:** `0` uses the ordinary System background; `1` displays the parent Cluster texture. Vanilla uses `1` only for Widow. |

## Planet table

Despite its name, `GalaxyMap_Planet` stores every object placed on a System map: planets, asteroid belts, anomalies, ships, Mass Relays, fuel depots and suns. In reality, vanilla does not use most of these. It only uses planets, asteroid belts, anomalies and a unique Citadel entry.

It also contains the full material-parameter block used by ordinary 3D planets.

### Identity, hierarchy and text

| Column | Purpose |
|---|---|
| unnamed first column | **Confirmed:** Planet Row ID. Also used for a same-ID PlotPlanet relationship. |
| `Label` | **Confirmed:** internal `PlanetNN` label. Its suffix forms the final two digits of ActiveWorld. |
| `System` | **Confirmed:** parent System Row ID. |
| `X`, `Y` | **Confirmed:** position on the System map, normally 0–1. |
| `Name` | **Confirmed:** TLK string reference for the displayed object name. |
| `NameText` | **Confirmed:** Editor only name. |
| `ActiveWorld` | **Confirmed:** derived Cluster/System/Planet identity code. It must be unique. |
| `Description` | **Confirmed:** TLK string reference for the description panel. |
| `ButtonLabel` | **Confirmed:** TLK string reference for the interaction button (eg. Survey/Land). |
| `ImageIndex` | **Confirmed:** index of the preview thumbnail above the description. This index is the row ID in the `Images_GalaxyMapImages` 2DA. `-1` or blank means no image. |

### Destination and interaction

| Column | Purpose |
|---|---|
| `Map` | **Confirmed:** linked Map Row ID. `-1` means that no persistent-level destination is linked. |
| `ExitMap` | **Observed:** non-zero only for Citadel, Noveria and Feros in vanilla. Its exact docking/cockpit skybox role remains unverified. |
| `Event` | **Confirmed:** Kismet Remote Event fired when the object is used. Almost all are in `BIOA_NOR10_03_DSG.pcc`. |
| `EventCondition` | **Observed** Likely unused in favour of Remote Events. |
| `EventFunction` | **Observed** Likely unused in favour of Remote Events. |
| `EventParameter` | **Observed** Likely unused in favour of Remote Events. |
| `EventTransition` | **Observed** Likely unused in favour of Remote Events. |
| `EventTransitionParameter` | **Observed** Likely unused in favour of Remote Events. |
| `EventMessage` | **Observed** Likely unused in favour of Remote Events. |

Normal destination behaviour is driven by `Event`, the availability triplets and the optional Map link rather than the legacy event-routing block.
The `Land` event appears to be unique, as it is not linked to sequencing but rather triggers special behaviour linked to the `Map` property, which is what causes the area transition.

### System-map appearance and selection model

| Column | Purpose |
|---|---|
| `Scale` | **Confirmed:** physical size on the System map. This is the only structural distinction between ordinary moons, planets and giants. |
| `PlanetRotation` | **Observed unused:** every vanilla entry uses `0`; no functioning effect has been verified. |
| `RingColor` | **Confirmed:** packed 32-bit ring colour for `SystemLevelType = 2`. Non-ringed objects should use `-1`; one vanilla ringed planet also retains `-1`. |
| `OrbitRing` | **Confirmed:** controls the surrounding orbit geometry; see the values below. |
| `SystemLevelType` | **Confirmed:** selects the glyph/model used on the System map. |
| `PlanetLevelType` | **Confirmed:** selects the model shown after the object is selected. |

#### OrbitRing values

| Value | Meaning |
|---:|---|
| `0` | No orbit ring. |
| `1` | Ordinary orbit ring. |
| `2` | Asteroid belt. Vanilla belts also use `SystemLevelType = 0` and `PlanetLevelType = 0`. |

#### SystemLevelType values

| Value | Meaning |
|---:|---|
| `0` | Planet |
| `1` | Anomaly or ship |
| `2` | Ringed planet |
| `3` | Mass Relay |
| `4` | Fuel depot |
| `5` | Sun |

Values 3, 4 and 5 do not appear to produce a unique effect sadly, but rather just duplicates of 1.

#### PlanetLevelType values

| Value | Meaning | LE1 status |
|---:|---|---|
| `0` | None | Used by asteroid belts and non-selectable objects. |
| `1` | 3D planet | Working; uses the Planet appearance block. |
| `2` | Anomaly | Working. |
| `3` | Planet + anomaly | Known broken in LE1. |
| `4` | Citadel | Working special case. |
| `5` | Prefab | Known broken in LE1. |
| `6` | Planet + ring | Working schema value. |
| `7` | 2D image | Known broken in LE1. |

Anomalies and ships normally use `SystemLevelType = 1` with `PlanetLevelType = 2`. Citadel is the special `PlanetLevelType = 4` case.

### Planet availability

Planet rows contain all three independent rule groups:

- `VisibleConditional`, `VisibleFunction`, `VisibleParameter`
- `UsableConditional`, `UsableFunction`, `UsableParameter`
- `UsablePlanetConditional`, `UsablePlanetFunction`, `UsablePlanetParameter`

The third group controls the Planet interaction button separately from general object usability.

## Planet material and shader columns

Only `PlanetLevelType = 1` rows use the ordinary 3D planet-material workflow. Texture references are commonly package-qualified, for example:

```text
BIOA_GXM10_T.GXM_ContinentMask01
```

Colour vectors use four separate floating-point columns ending in `R`, `G`, `B` and `A`. Packed light colours use a single signed or unsigned 32-bit ARGB integer.

Mixer vectors also store `R/G/B/A`. The established controls are the red, green and blue channel weights; the fourth `A` value is retained for data fidelity, but its separate runtime purpose is not established.

### Material identity

| Column | Purpose |
|---|---|
| `Shader` | **Confirmed:** Material-instance name for this Planet row. |

### Continent and landmass

| Column(s) | Purpose |
|---|---|
| `ContinentMask01` | Primary packed landmass-mask texture. |
| `ContinentMask02` | Secondary packed land/variation-mask texture. |
| `Continent_Texture` | Detail texture mixed over the land surface. |
| `Continent_ColorR`, `Continent_ColorG`, `Continent_ColorB`, `Continent_ColorA` | Primary HDR land colour. |
| `Continent_Color_AltR`, `Continent_Color_AltG`, `Continent_Color_AltB`, `Continent_Color_AltA` | Secondary HDR land colour blended through the masks. |
| `Landmass_MixerR`, `Landmass_MixerG`, `Landmass_MixerB`, `Landmass_MixerA` | Beach transition, land threshold and silt transition controls; alpha is retained. |
| `Continent_Mask_MixerR`, `Continent_Mask_MixerG`, `Continent_Mask_MixerB`, `Continent_Mask_MixerA` | Weights channels from the primary continent mask. |
| `Continent_Mask_Mixer02R`, `Continent_Mask_Mixer02G`, `Continent_Mask_Mixer02B`, `Continent_Mask_Mixer02A` | Weights channels from the secondary continent mask. |
| `Continent_Texture_MixerR`, `Continent_Texture_MixerG`, `Continent_Texture_MixerB`, `Continent_Texture_MixerA` | Weights channels from the land detail texture. |

### Surface normals

| Column | Purpose |
|---|---|
| `Normal_Map` | Surface normal texture. |
| `Normal_Map_Tile` | UV repeat applied to the normal map. |
| `Bump_Amount` | Strength of the normal-map surface relief. |

### Ocean

| Column(s) | Purpose |
|---|---|
| `Ocean_Texture` | Detail texture mixed over the ocean surface. |
| `Ocean_ColorR`, `Ocean_ColorG`, `Ocean_ColorB`, `Ocean_ColorA` | Primary HDR ocean colour. |
| `Ocean_Color_AltR`, `Ocean_Color_AltG`, `Ocean_Color_AltB`, `Ocean_Color_AltA` | Secondary HDR ocean colour. |
| `Ocean_Texture_MixerR`, `Ocean_Texture_MixerG`, `Ocean_Texture_MixerB`, `Ocean_Texture_MixerA` | Weights channels from the ocean detail texture. |

### Beach and silt

| Column(s) | Purpose |
|---|---|
| `Beach_ColorR`, `Beach_ColorG`, `Beach_ColorB`, `Beach_ColorA` | HDR coastline-transition colour. |
| `Silt_ColorR`, `Silt_ColorG`, `Silt_ColorB`, `Silt_ColorA` | HDR shallow-water/coastal-silt colour. |

The positions and widths of these transitions are controlled by `Landmass_Mixer`.

### City emissive

| Column(s) | Purpose |
|---|---|
| `City_Emissive` | City-light mask. |
| `City_Emissive_ColorR`, `City_Emissive_ColorG`, `City_Emissive_ColorB`, `City_Emissive_ColorA` | HDR city-light colour. |
| `City_Emissive_MixerR`, `City_Emissive_MixerG`, `City_Emissive_MixerB`, `City_Emissive_MixerA` | Weights channels from the emissive mask. |
| `City_Emissive_Tile` | UV repeat applied to the city mask. |
| `Emissive_Twinkle_Multiplier` | Strength of city-light variation. Note it does not actually animate. |

### Atmosphere and horizon

| Column(s) | Purpose |
|---|---|
| `AtmosphereMaster` | Texture controlling the moving atmosphere layer. |
| `Atmosphere_ColorR`, `Atmosphere_ColorG`, `Atmosphere_ColorB`, `Atmosphere_ColorA` | HDR multiplier for the atmosphere texture. |
| `Atmosphere_MixerR`, `Atmosphere_MixerG`, `Atmosphere_MixerB`, `Atmosphere_MixerA` | Weights channels from the atmosphere texture. |
| `Atmosphere_Min` | Minimum contribution of the atmosphere layer. |
| `Atmosphere_Tile_U` | Horizontal repeat of the atmosphere texture. |
| `Atmosphere_Tile_V` | Vertical repeat of the atmosphere texture. |
| `Atmosphere_Pan_Multiplier` | Animation-speed multiplier for the atmosphere texture. |
| `Horizon_Atmosphere_ColorR`, `Horizon_Atmosphere_ColorG`, `Horizon_Atmosphere_ColorB`, `Horizon_Atmosphere_ColorA` | HDR silhouette-glow colour. |
| `Horizon_Atmosphere_Intensity` | Brightness of the glow around the planet silhouette. |
| `Horizon_Atmosphere_Falloff` | Controls how tightly the glow follows the silhouette. |

### Corona

| Column(s) | Purpose |
|---|---|
| `Corona_ColorR`, `Corona_ColorG`, `Corona_ColorB`, `Corona_ColorA` | HDR colour applied to the outer corona. |
| `Fringe_Bloom` | Additional bloom contribution at the corona fringe. |
| `Opacity` | Overall corona intensity/opacity multiplier. |

### Runtime lights

| Column | Purpose |
|---|---|
| `SunColor1` | Packed ARGB colour of the first used planet light. |
| `Brightness1` | Intensity of the first used planet light. |
| `SunColor2` | Packed ARGB colour of the second used planet light. |
| `Brightness2` | Intensity of the second used planet light. |
| `SunColor0` | **Observed** Does not appear to be used. |
| `Brightness0` | **Observed ** Does not appear to be used. |

## PlotPlanet table

Vanilla uses `GalaxyMap_PlotPlanet` for a subset of important or landable Planet rows. Not every Planet requires a PlotPlanet entry.

| Column | Purpose |
|---|---|
| unnamed first column | **Confirmed:** must match the linked Planet Row ID. |
| `Code` | **Confirmed:** must equal the linked Planet's `ActiveWorld`. |
| `Name` | **Observed** normally matches the linked Planet TLK `Name`. |
| `NameText` | **Observed** normally matches the linked Planet editor name. |
| `VisibleConditional`, `VisibleFunction`, `VisibleParameter` | PlotPlanet visibility rule; vanilla linked rows normally mirror the Planet. |
| `UsableConditional`, `UsableFunction`, `UsableParameter` | PlotPlanet usability rule; vanilla linked rows normally mirror the Planet. |

A PlotPlanet without a same-ID Planet is invalid. A mismatched `Code` breaks the ActiveWorld relationship.

## Map table

`GalaxyMap_Map` supplies the actual persistent level destination loaded for a linked Planet (eg. `BIOA_STA00` for the Citadel or `BIOA_UNC51` for Luna.)

| Column | Purpose |
|---|---|
| unnamed first column | **Confirmed:** Map Row ID referenced by `Planet.Map`. |
| `Map` | **Confirmed:** persistent-level/package name to load. |
| `StartPoint` | **Confirmed:** BioStartPoint used as the player's spawn point. |

Example:

```text
Map       = BIOA_STA00
StartPoint = start_NOR10_03
```

This warps the player to the Citadel persistent level, inside the Normandy at the galaxy map podium.

`Planet.Map = -1` means no Map link. A non-negative value must resolve to an existing Map Row ID.

Vanilla normally treats a Map destination as belonging to one Planet. Sharing a Map row between several Planets is technically possible in the raw data but should be reviewed carefully.

## Worked landable-planet relationship

The vanilla Citadel rows demonstrate the full chain:

| Table/column | Example | Meaning |
|---|---:|---|
| Cluster Row ID / Label | `1` / `Cluster01` | Cluster suffix is `1`. |
| System Row ID / Label | `1` / `System01` | Parent Cluster Row ID is `1`; System suffix is `1`. |
| Planet Row ID / Label | `1` / `Planet01` | Parent System Row ID is `1`; Planet suffix is `1`. |
| `Planet.ActiveWorld` | `10101` | Derived from the three numbered Labels. |
| `PlotPlanet` Row ID | `1` | Same as the Planet Row ID. |
| `PlotPlanet.Code` | `10101` | Same as Planet ActiveWorld. |
| `Planet.Map` | `1` | Links to Map Row ID `1`. |
| `Map.Map` | `BIOA_STA00` | Persistent level loaded for the destination. |
| `Map.StartPoint` | `start_NOR10_03` | Player spawn node. |
| `Planet.Event` | `Land` | Remote Event fired by the interaction. |

## Relay table

`GalaxyMap_Relay` describes connections between Clusters.

| Column | Purpose |
|---|---|
| unnamed first column | **Confirmed:** Relay Row ID. |
| `StartCluster` | **Confirmed:** first Cluster Label suffix multiplied by 10,000. |
| `EndCluster` | **Confirmed:** second Cluster Label suffix multiplied by 10,000. |

These columns do **not** contain Cluster Row IDs.

Example:

```text
Cluster01 → 1 × 10,000 = 10,000
Cluster07 → 7 × 10,000 = 70,000

StartCluster = 10000
EndCluster   = 70000
```

The order of the endpoints does not create direction. A Cluster cannot connect to itself, and a second row containing the same pair in reverse order is still a duplicate connection.

BASEGAME Relay row `6` contains endpoint `40000`, referring to an absent `Cluster04` which appears to have been cut from the game. As a result the line is simply undrawn until `Cluster04` is added.

## DLC `_part` tables and overrides

DLC modules use _part 2DAs to extend the BASEGAME 2DA table.

- A new Row ID adds content.
- The same table and Row ID replaces the lower-mounted version of that row.
- Higher DLC mount priority wins when several modules supply the same row.
- Use module priorities matching the DLC mount numbers and coordinate Row ID ranges between mods.

## Authoring checklist

Before packaging a galaxy-map edit, verify that:

- every unnamed Row ID is unique within its effective table unless it is an intentional override;
- `System.Cluster` and `Planet.System` resolve to existing Row IDs;
- numbered Labels use the correct prefixes and parent-scoped unique suffixes;
- every `Planet.ActiveWorld` matches its Cluster/System/Planet Label chain;
- PlotPlanet Row IDs match their Planet Row IDs;
- `PlotPlanet.Code` matches `Planet.ActiveWorld`;
- every non-negative `Planet.Map` resolves to a Map row;
- linked Map rows have both a persistent level and StartPoint;
- Relay endpoints use Cluster Label codes rather than Row IDs;
- availability triplets are complete and intentional;
- `RingColor`, `OrbitRing`, `SystemLevelType` and `PlanetLevelType` form a sensible combination;
- 3D planets use unique Shader names and valid texture references;
- known-broken `PlanetLevelType` values 3, 5 and 7 are avoided.
