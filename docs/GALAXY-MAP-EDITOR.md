# Galaxy Map Editor

The Galaxy Map Editor is the main visual workspace. It keeps the **HIERARCHY**, **MAP CANVAS** and **PROPERTIES** panes synchronised as you navigate.

![Main Galaxy Map Editor with the Hierarchy, map, Properties pane, breadcrumbs and module bar](docs/images/main-window.png)

## Navigate the map

| Action | Result |
|---|---|
| Select a hierarchy row | Selects the same object on the map. |
| Single-click a map object | Selects it and opens its properties. |
| Double-click a Cluster | Opens its Systems. |
| Double-click a System | Opens its planets and system objects. |
| Double-click an eligible planet | Opens it in Planet Designer. Only Planets and Ringed Planets are eligible.|
| Select a breadcrumb | Switches to the Galaxy, Cluster or System level. |

## Search the hierarchy

The hierarchy search works for names, object types, module tags, Row IDs and the object's full Cluster/System path.

Separate search terms are combined. For example, searching for a module tag and a System name shows only paths matching both terms.

Matching paths expand automatically. Clear the search to restore the complete hierarchy.

## Show the coordinate grid

Choose **Show coordinate grid** to display the 0–1 map coordinate system. Major lines are labelled at `.00`, `.25`, `.50`, `.75` and `1.00`.

This will also add a cursor widget that displays the X/Y coordinates of your cursor on the map.

The setting remains active as you move between map levels. Choose **Hide coordinate grid** to remove it. The cursor widget can also be independantly enabled by holding `Shift`.

![Coordinate grid displayed over a System map with live mouse coordinate position]](docs/images/coordinate-grid.png)

## Add content

You need an active writable module before adding content.

| Current level | Available command |
|---|---|
| Galaxy | **Add Cluster** |
| Cluster | **Add System** |
| System | **Add Planet/Object** |

The command is available from the contextual button at the top of the heirarchy, or right clicking on the appropriate hierarchy row/map canvas.

New map objects start at the centre of the map rather than at the context-menu pointer. Move them afterwards with `Shift + Drag`.

### Choose a Cluster label

**Add Cluster** asks for a global `ClusterNN` label before creating the row. Vanilla reserves `Cluster01` through `Cluster21`, so new modules must use an unused label from `Cluster22` through `Cluster99`. 

The window lists labels belonging to currently mounted modules and rejects a mounted collision.

Cluster labels are shared across the entire galaxy map and the editor cannot detect conflicts with mods that are not currently mounted, so mod authors should publish and coordinate their claimed Cluster ranges. 

Systems and Planets remain automatically numbered because their labels are scoped to their parent Cluster or System and aren't as big of a concern unless two mods edit the same cluster.

### Add a planet or system object

**Add Planet/Object** opens the **Add system object** window. Choose a template:

- **Generic planet**
- **Ringed planet**
- **Asteroid belt**
- **Hidden anomaly** - This is the blinking anomaly typically used for hidden asteroids, but can technically be used for anything
- **Anomaly / ship**

Enter the internal name, TLK ID and scale. Scale must be above 0 and may use up to two decimal places.

Landable planet templates can also create linked Map and PlotPlanet data, a Remote Event, use-button TLK text reference and persistent-level destination. Asteroid belts do not expose those landable options.

Generic and ringed planets can open in Planet Designer after creation. The planet will appear as black until an appearance has been applied to it.

![Add system object window showing the five templates and landable-planet options](docs/images/add-system-object.png)

## Move a map marker

Hold **Shift**, then left-drag the marker. Live coordinates appear while you move it.

Release the mouse or Shift to apply the movement as one undoable change. Press **Esc** before releasing to cancel it. **Undo** can also revert the change if needed.

Coordinates are kept between `0.00` and `1.00` and stored to two decimal places. Moving a BASEGAME object creates an override in a writable module.

## Edit properties

Select an object to edit it in **PROPERTIES**. Sections group common identity, placement, availability, relationship, appearance and less frequently used values. Hover over the property to see a description of what it does.

Most text fields apply when you click off them. Press **Enter** to apply a valid value and move to the next field. Invalid values remain selected, turn red, will not apply, and usually show an explanation.

Rows supplied by several modules have module-instance tabs at the top of **PROPERTIES**. These show where each module version originates and which one is currently effective.

## Clone content

Right-click an object and choose its clone command:

- **Clone Cluster…**
- **Clone System…**
- **Clone Planet / object…**

The **Clone galaxy-map content** window suggests available IDs and labels. Cloning a Cluster or System can include its children and linked rows.

A cloned eligible planet opens in Planet Designer so that you can check its appearance and assign a unique Shader name. When cloning a parent with several planets, review the child planets individually before committing.

## Move content between parents

Right-click a System and choose **Move to Cluster…**, or right-click a planet/system object and choose **Move to System…**.

The Row ID and map coordinates stay the same. The editor updates managed parent relationships and resolves numbered-label collisions in the destination.

Moving inherited content creates an override rather than changing its read-only source.

## Delete content

Right-click and choose:

- **Delete Cluster…**
- **Delete System…**
- **Delete Planet / object…**

The editor asks for confirmation. Deleting a module-owned Cluster or System also removes its module-owned descendants and managed links.

BASEGAME and read-only source rows cannot be deleted. Deleting an override reveals the lower-priority version.

## Relay connections

Select a Cluster to manage its Relays from **PROPERTIES**. It's recommended to do this in the Galaxy view so you can see the active relay connections (red lines).

| Command | Use |
|---|---|
| **Add relay connection…** | Starts destination-selection mode for a new Relay. |
| **Redirect connection from _destination_…** | Replaces the destination of an inherited or existing connection. |
| **Break connection to _destination_** | Removes a connection owned solely by the active module. |
| **Cancel relay edit** | Leaves destination-selection mode without changing anything. |

While choosing a destination, simply click on another Cluster to create the connection. Self-connections and duplicate connections are rejected.

Inherited Relays cannot be broken because the partial table format has no verified deletion marker. Redirecting one creates a same-ID override instead. 

Example: Spectre Expansion Mod adds the Arcturus Stream cluster, so it redirects the BASEGAME relay connection from  `Local Cluster <--> Exodus Cluster` to `Local Cluster <--> Arcturus Stream` and then adds a new relay connection from `Arcturus Stream <--> Exodus Cluster`,

![Main Galaxy view in Relay destination-selection mode with the instruction banner and red line](docs/images/relay-connection-a.png)

![Created new relay connection from Serpent Nebula to Pangaea Expanse](docs/images/relay-connection-b.png)

## Link a Cluster texture

Use **Link module texture…** in a Cluster's properties to select a PNG, JPEG, BMP, GIF or TIFF image.

The preview updates before Commit. The image is stored with the module inside a `resources` folder when the change is committed.

For Systems with `ShowNebula` set to `1`, the parent Cluster texture is shown at 200% scale and the ordinary sun is hidden. In BASEGAME, this is only used for the Widow system in the Serpent Nebula.

## System-map symbols

| Symbol | Object type |
|---|---|
| `●` | Planet |
| `▲` | Asteroid-belt anchor |
| `◆` | Object or anomaly |
| `◉` | Ringed planet |
| `⇄` | Relay |
| `▣` | Depot |
| `☀` | Sun |

Selected objects are orange. Other objects use the colour of the module supplying their effective row. Relay lines remain red, while asteroid particles use a neutral sandstone colour.

## See also

- [Planet Designer](PLANET-DESIGNER.md)
- [Shortcuts and Gestures](SHORTCUTS-AND-GESTURES.md)
- [Validation and Errors](VALIDATION-AND-ERRORS.md)
