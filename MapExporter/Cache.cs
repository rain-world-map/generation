using System.Collections.Generic;
using System.Linq;

namespace MapExporter;

enum CaptureMode
{
    CacheHit, CacheMiss, CacheAdd
}

sealed class Cache
{
    public readonly Dictionary<string, MapContent> metadata = new();   // "{region name}" -> map content
    public readonly Dictionary<string, RoomSettings> roomSettings = new(); // "{region name}#{room name}" -> settings
    public readonly Dictionary<string, FloatRect> roomBoundingBoxes = new(); // "{region name}#{room name}" -> pos and size

    private static string GetKey(Room room)
    {
        return $"{room.world.name}#{room.abstractRoom.name}";
    }

    public void TrackBoundingBox(MapContent regionContent, Room room)
    {
        string key = GetKey(room);
        if (!roomBoundingBoxes.ContainsKey(key))
            roomBoundingBoxes[key] = regionContent.GetBoundingBox(room);
    }

    public CaptureMode CacheResult(Room room)
    {
        string key = GetKey(room);

        if (roomSettings.TryGetValue(key, out RoomSettings existingSettings)) {
            if (Identical(existingSettings, room.roomSettings)) {
                MapExporter.Logger.LogDebug($"CACHE HIT  | {room.game.StoryCharacter}/{room.abstractRoom.name}");
                return CaptureMode.CacheHit;
            }
            MapExporter.Logger.LogDebug($"CACHE MISS | {room.game.StoryCharacter}/{room.abstractRoom.name} | Different from cached room");
            return CaptureMode.CacheMiss;
        }
        FloatRect roomBox = roomBoundingBoxes[key];
        if (roomBoundingBoxes.Any(kvp => kvp.Key != key && Intersects(kvp.Value, roomBox))) {
            MapExporter.Logger.LogDebug($"CACHE MISS | {room.game.StoryCharacter}/{room.abstractRoom.name} | Cameras overlap with another cached room");
            return CaptureMode.CacheMiss;
        }
        MapExporter.Logger.LogDebug($"CACHE ADD  | {room.game.StoryCharacter}/{room.abstractRoom.name}");
        roomSettings[key] = room.roomSettings;
        return CaptureMode.CacheAdd;
    }

    static bool Identical(RoomSettings one, RoomSettings two)
    {
        if (ReferenceEquals(one, two)) {
            return true;
        }
        if (one == null || two == null) {
            return false;
        }
        bool p1 = one.isAncestor == two.isAncestor && one.isTemplate == two.isTemplate && one.clds == two.clds && one.swAmp == two.swAmp && one.swLength == two.swLength &&
            one.wAmp == two.wAmp && one.wetTerrain == two.wetTerrain && one.eColA == two.eColA && one.eColB == two.eColB && one.grm == two.grm && one.pal == two.pal &&
            one.wtrRflctAlpha == two.wtrRflctAlpha;
        if (!p1) {
            return false;
        }
        bool fadePalettesMatch = one.fadePalette == null && two.fadePalette == null ||
            one.fadePalette != null && two.fadePalette != null && one.fadePalette.palette == two.fadePalette.palette && one.fadePalette.fades.SequenceEqual(two.fadePalette.fades);
        if (!fadePalettesMatch) {
            return false;
        }
        bool effectsMatch = one.effects.Select(e => e.ToString()).SequenceEqual(two.effects.Select(e => e.ToString()));
        if (!effectsMatch) {
            return false;
        }
        bool placedObjectsMatch = one.placedObjects.Select(p => p.ToString()).SequenceEqual(two.placedObjects.Select(p => p.ToString()));
        if (!placedObjectsMatch) {
            return false;
        }
        return Identical(one.parent, two.parent);
    }
}
