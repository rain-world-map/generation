using System.Collections.Generic;
using System.Linq;

namespace MapExporter;

static class ReusedRooms
{
    struct RegionData
    {
        public Dictionary<string, RoomSettings> settings;
    }

    static readonly Dictionary<string, RegionData> regions = new();

    public static string SlugcatRoomsToUse(string slugcat, World world, List<AbstractRoom> validRooms)
    {
        slugcat = slugcat.ToLower();

        string baseSlugcat = SlugcatFor(slugcat, world.name);
        string key = $"{baseSlugcat}#{world.name}";

        if (baseSlugcat == slugcat) {
            regions[key] = new() {
                settings = validRooms.ToDictionary(
                    keySelector: a => a.name,
                    elementSelector: Settings,
                    comparer: System.StringComparer.InvariantCultureIgnoreCase
                    )
            };
            return null;
        }
        if (!regions.TryGetValue(key, out RegionData regionData)) {
            MapExporter.Logger.LogWarning($"NOT COPIED | Region settings are not stored for {baseSlugcat}/{world.name} coming from {slugcat}");
            return null;
        }
        if (regionData.settings.Count != validRooms.Count) {
            MapExporter.Logger.LogWarning($"NOT COPIED | Different room count for {world.name} in {baseSlugcat} and {slugcat}");
            return null;
        }
        foreach (AbstractRoom room in validRooms) {
            if (!regionData.settings.TryGetValue(room.name, out RoomSettings existingSettings)) {
                MapExporter.Logger.LogWarning($"NOT COPIED | The room {room.name} exists for {slugcat} but not {baseSlugcat}");
                return null;
            }
            if (!Identical(existingSettings, Settings(room))) {
                MapExporter.Logger.LogWarning($"NOT COPIED | The room {room.name} is different for {slugcat} and {baseSlugcat}");
                return null;
            }
        }
        MapExporter.Logger.LogDebug($"Copying rooms from {baseSlugcat} to {slugcat}/{world.name}");
        return baseSlugcat;

        // EXCLUDE HIDDEN ROOMS.
        // if number of rooms is different, null
        // if room name from one is missing from the other, null
        // if any room's settings differ, null
        // else, baseSlugcat
    }

    private static RoomSettings Settings(AbstractRoom a) => new(a.name, a.world.region, false, false, a.world.game.StoryCharacter);

    public static string SlugcatFor(string slugcat, string region)
    {
        region = region.ToLower();
        if (region is "lc" or "lm" || region is "gw" && slugcat == "spear")
            return "artificer";
        if (region is "cl" or "ug")
            return "saint";
        if (region is "rm")
            return "rivulet";
        return "white";
    }

    static bool Identical(RoomSettings one, RoomSettings two)
    {
        if (ReferenceEquals(one, two)) {
            return true;
        }
        if (one.name.StartsWith("GATE") && two.name.StartsWith("GATE")) {
            // This is a hack to fix gates. For some reason gates and gates *specifically* change constantly between slugcats.
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
