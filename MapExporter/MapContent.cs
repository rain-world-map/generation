using System.Collections.Generic;
using UnityEngine;
using System.Text.RegularExpressions;
using System;
using System.Linq;
using System.IO;
using RWCustom;

namespace MapExporter;

sealed class MapContent : IJsonObject
{
    readonly Dictionary<string, RoomEntry> rooms;
    readonly List<ConnectionEntry> connections;
    readonly string name;
    readonly string acronym;
    readonly List<Color> fgcolors;
    readonly List<Color> bgcolors;
    readonly List<Color> sccolors;
    readonly HashSet<string> worldSpawns;

    public MapContent(World world)
    {
        acronym = world.name;
        name = NameOfRegion(world);

        rooms = new Dictionary<string, RoomEntry>() { ["offscreen"] = new("offscreen") };
        connections = new List<ConnectionEntry>();

        fgcolors = new List<Color>();
        bgcolors = new List<Color>();
        sccolors = new List<Color>();

        worldSpawns = new HashSet<string>();

        DevInterface.DevUI fakeDevUi = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DevInterface.DevUI)) as DevInterface.DevUI;
        DevInterface.MapPage fakeMapPage = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(DevInterface.MapPage)) as DevInterface.MapPage;
        fakeMapPage.owner = fakeDevUi;
        fakeDevUi.game = world.game;
        fakeMapPage.filePath = string.Concat(new object[]
        {
                Custom.RootFolderDirectory(),
                "World",
                Path.DirectorySeparatorChar,
                "Regions",
                Path.DirectorySeparatorChar,
                world.name,
                Path.DirectorySeparatorChar,
                "map_",
                world.name,
                ".txt"
        });

        LoadMapConfig(fakeMapPage);

        fakeDevUi.game = null;
        fakeMapPage.owner = null;

        LoadSpawns(world);
    }

    private void LoadSpawns(World world)
    {
        string acronym = world.region.name;
        string path = AssetManager.ResolveFilePath($"world/{acronym}/world_{acronym}.txt");
        if (File.Exists(path)) {
            AssimilateCreatures(File.ReadAllLines(path));
        }
        else {
            Console.WriteLine($"!? PATH DOES NOT EXIST: {path}");
        }
    }

    private void AssimilateCreatures(IEnumerable<string> raw)
    {
        bool insideofcreatures = false;
        foreach (var item in raw)
        {
            if (item == "CREATURES") insideofcreatures = true;
            else if (item == "END CREATURES") insideofcreatures = false;
            else if (insideofcreatures)
            {
                if (string.IsNullOrEmpty(item) || item.StartsWith("//")) continue;
                worldSpawns.Add(item);
            }
        }
    }

    static float[] Vec2arr(Vector2 vec) => new float[] { vec.x, vec.y };
    static float[] Vec2arr(Vector3 vec) => new float[] { vec.x, vec.y, vec.z };
    static int[] Intvec2arr(IntVector2 vec) => new int[] { vec.x, vec.y};

    public void UpdateRoom(Room room)
    {
        rooms[room.abstractRoom.name] = new(room.abstractRoom.name);
        rooms[room.abstractRoom.name].UpdateEntry(room);
    }

    private string NameOfRegion(World world)
    {
        return Region.GetRegionFullName(world.region.name, world.game.StoryCharacter);
    }

    public Dictionary<string, object> ToJson()
    {
        return new Dictionary<string, object>()
        {
            { "name", name },
            { "acronym", acronym },
            { "rooms", rooms },
            { "connections", connections },
            { "fgcolors" , (from s in fgcolors select  Vec2arr((Vector3)(Vector4)s)).ToList()},
            { "bgcolors" , (from s in bgcolors select  Vec2arr((Vector3)(Vector4)s)).ToList()},
            { "sccolors" , (from s in sccolors select  Vec2arr((Vector3)(Vector4)s)).ToList()},
            { "spawns", worldSpawns.ToArray()},
        };
    }

    internal void LogPalette(RoomPalette currentPalette)
    {
        // get sky color and fg color (px 00 and 07)
        Color fg = currentPalette.texture.GetPixel(0, 0);
        Color bg = currentPalette.texture.GetPixel(0, 7);
        Color sc = currentPalette.shortCutSymbol;
        fgcolors.Add(fg);
        bgcolors.Add(bg);
        sccolors.Add(sc);

    }

    sealed class RoomEntry : IJsonObject
    {
        public string roomName;

        public RoomEntry(string roomName)
        {
            this.roomName = roomName;
        }

        // from map txt
        public Vector2 devPos;
        public Vector2 canPos;
        public int canLayer;
        public int subregion;
        public bool everParsed = false;
        public void ParseEntry(string line)
        {
            //Debug.Log(line);
            string[] arr = Regex.Split(line, ": ");
            if (roomName != arr[0]) throw new Exception();
            string[] arr2 = Regex.Split(arr[1], ",");
            canPos.x = float.Parse(arr2[0]);
            canPos.y = float.Parse(arr2[1]);
            devPos.x = float.Parse(arr2[2]);
            devPos.y = float.Parse(arr2[3]);
            canLayer = int.Parse(arr2[4]);
            subregion = int.Parse(arr2[5]);
            everParsed = true;
        }

        // from room
        public Vector2[] cameras; // TODO: can this cause issues if it's not the same as the cache?
        private int[] size;
        private int[,][] tiles;
        private IntVector2[] nodes;

        public void UpdateEntry(Room room)
        {
            cameras = room.cameraPositions;

            size = new int[] { room.Width, room.Height };

            tiles = new int[room.Width, room.Height][];
            for (int k = 0; k < room.Width; k++)
            {
                for (int l = 0; l < room.Height; l++)
                {
                    // Dont like either available formats ?
                    // Invent a new format
                    tiles[k, l] = new int[] { (int)room.Tiles[k, l].Terrain, (room.Tiles[k, l].verticalBeam ? 2:0) + (room.Tiles[k, l].horizontalBeam ? 1:0), (int)room.Tiles[k, l].shortCut};
                    //terain, vb+hb, sc
                }
            }
            nodes = room.exitAndDenIndex;
        }

        // wish there was a better way to do this
        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>()
            {
                { "roomName", roomName },
                { "canPos", Vec2arr(canPos) },
                { "canLayer", canLayer },
                { "devPos", Vec2arr(devPos) },
                { "subregion", subregion },
                { "cameras", cameras != null ? (from c in cameras select Vec2arr(c)).ToArray() : null},
                { "nodes", nodes != null ? (from n in nodes select Intvec2arr(n)).ToArray() : null},
                { "size", size},
                { "tiles", tiles},
            };
        }
    }

    sealed class ConnectionEntry : IJsonObject
    {
        public string roomA;
        public string roomB;
        public IntVector2 posA;
        public IntVector2 posB;
        public int dirA;
        public int dirB;

        public ConnectionEntry(string line)
        {
            string[] stuff = Regex.Split(Regex.Split(line, ": ")[1], ",");
            roomA = stuff[0];
            roomB = stuff[1];
            posA = new IntVector2(int.Parse(stuff[2]), int.Parse(stuff[3]));
            posB = new IntVector2(int.Parse(stuff[4]), int.Parse(stuff[5]));
            dirA = int.Parse(stuff[6]);
            dirB = int.Parse(stuff[7]);
        }

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>()
            {
                { "roomA", roomA },
                { "roomB", roomB },
                { "posA", Intvec2arr(posA) },
                { "posB", Intvec2arr(posB) },
                { "dirA", dirA },
                { "dirB", dirB },
            };
        }
    }

    public void LoadMapConfig(DevInterface.MapPage fakeMapPage)
    {
        if (!File.Exists(fakeMapPage.filePath)) return;
        Debug.Log("reading map file: " + fakeMapPage.filePath);
        string[] contents = File.ReadAllLines(fakeMapPage.filePath);
        foreach (var s in contents)
        {
            string sname = Regex.Split(s, ": ")[0];
            if (rooms.TryGetValue(sname, out RoomEntry room)) room.ParseEntry(s);
            if (sname == "Connection") connections.Add(new ConnectionEntry(s));
        }
    }
}
