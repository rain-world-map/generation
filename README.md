# Rain-World-Interactive-Map
A Rain World interactive map using leaflet and GeoJSON data exported from the game files.

[(Link to the map page)](https://henpemaz.github.io/Rain-World-Interactive-Map/index.html)

This project consists of three parts:
- A c# mod, MapExporter, that jumps through the game generating screenshots and exporting metadata about rooms and regions and maps.
- A python script, generateGeoJSON, that stitches up the screenshots into a map, producing a tileset/basemap and converting the metadata into GeoJSON features.
- The front-end app in plain html css and javascript using Leaflet for the map, all static files so it can be hosted in a github site.

To generate assets for the game:
1. Install MapExporter,  run the game, and let the mod do its thing. The game will close when MapExporter is finished.
2. After the game closes, copy the contents of the `exports` folder into a new folder called `py-input`, which should be next to `generateGeoJSON.py`.
3. Run `generateGeoJSON.py`.

The currently tracked things from the game are:
- room placement from the dev-map
- room names
- room connections
- room geometry
- spawns and lineages

The immediate to-do list for this project is:
- place icons for the most common room tags like "shelter", "scavoutpost" and "swarmroom"
- in-room shortcuts
- icons for pearls and unlock tokens

If you wish to contribute, hmu on Discord!
