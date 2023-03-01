using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.Serialization;

namespace MapExporter;

sealed class SlugcatFile : IJsonObject
{
    private readonly Dictionary<string, object> scugs = new();

    public void AddCurrentSlugcat(RainWorldGame game)
    {
        scugs[game.StoryCharacter.value.ToLower()] = new Slugcat(game);
    }

    public Dictionary<string, object> ToJson() => scugs;
}

sealed class Slugcat : IJsonObject
{
    private readonly string startingRegion;
    private readonly string startingRoom;
    private readonly Dictionary<string, string> regions;

    public Slugcat(RainWorldGame game)
    {
        SaveState ss = (SaveState)FormatterServices.GetUninitializedObject(typeof(SaveState));
        ss.progression = game.rainWorld.progression;
        ss.progression.rainWorld.safariMode = false;
        ss.saveStateNumber = game.StoryCharacter;
        ss.setDenPosition();
        ss.progression.rainWorld.safariMode = true;

        startingRoom = ss.denPosition;

        if (startingRoom.StartsWith("GATE_")) {
            startingRegion = startingRoom == "GATE_OE_SU" ? "SU" : throw new Exception($"Unknown starting region from room {startingRoom} for slugcat {game.StoryCharacter.value}");
        }
        else {
            startingRegion = startingRoom.Split('_')[0];
        }

        regions = new Dictionary<string, string>();
        foreach (var reg in SlugcatStats.getSlugcatStoryRegions(game.StoryCharacter).Concat(SlugcatStats.getSlugcatOptionalRegions(game.StoryCharacter))) {
            regions[reg] = Region.GetRegionFullName(reg, game.StoryCharacter);
        }
    }

    public Dictionary<string, object> ToJson()
    {
        return new Dictionary<string, object>()
        {
            { "startingRegion", startingRegion },
            { "startingRoom", startingRoom },
            { "regions", regions }
        };
    }
}
