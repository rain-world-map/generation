using System.Linq;
using System.Security.Permissions;
using System.IO;
using RWCustom;
using UnityEngine;
using MonoMod.RuntimeDetour;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using System.Text.RegularExpressions;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace MapExporter;

[BepInPlugin("io.github.henpemaz-dual", "Map Exporter", "1.0.0")]
sealed class MapExporter : BaseUnityPlugin
{
    // Config
    static readonly string[] captureSpecific = { }; // For example, "White;SU" loads Outskirts as Survivor
    static readonly bool screenshots = true;

    static readonly Dictionary<string, int[]> blacklistedCams = new()
    {
        { "SU_B13", new int[]{2} }, // one indexed
        { "GW_S08", new int[]{2} }, // in vanilla only
        { "SL_C01", new int[]{4,5} }, // crescent order or will break
    };

    public static new ManualLogSource Logger;

    public static bool NotHiddenRoom(AbstractRoom room) => !HiddenRoom(room);
    public static bool HiddenRoom(AbstractRoom room)
    {
        if (room == null) {
            return true;
        }
        if (room.world.DisabledMapRooms.Contains(room.name, System.StringComparer.InvariantCultureIgnoreCase)) {
            Logger.LogDebug($"Room {room.world.game.StoryCharacter}/{room.name} is disabled");
            return true;
        }
        if (!room.offScreenDen) {
            if (room.connections.Length == 0) {
                Logger.LogDebug($"Room {room.world.game.StoryCharacter}/{room.name} with no outward connections is ignored");
                return true;
            }
            if (room.connections.All(r => room.world.GetAbstractRoom(r) is not AbstractRoom other || !other.connections.Contains(room.index))) {
                Logger.LogDebug($"Room {room.world.game.StoryCharacter}/{room.name} with no inward connections is ignored");
                return true;
            }
        }
        return false;
    }

    public void OnEnable()
    {
        Logger = base.Logger;
        On.RainWorld.Update += RainWorld_Update1;
        On.RainWorld.Start += RainWorld_Start; // "FUCK compatibility just run my hooks" - love you too henpemaz
    }

    private void RainWorld_Update1(On.RainWorld.orig_Update orig, RainWorld self)
    {
        try {
            orig(self);
        }
        catch (System.Exception e) {
            Logger.LogError(e);
        }
    }

    private void RainWorld_Start(On.RainWorld.orig_Start orig, RainWorld self)
    {
        On.Json.Serializer.SerializeValue += Serializer_SerializeValue;
        On.RainWorld.LoadSetupValues += RainWorld_LoadSetupValues;
        On.RainWorld.Update += RainWorld_Update;
        On.World.SpawnGhost += World_SpawnGhost;
        On.GhostWorldPresence.SpawnGhost += GhostWorldPresence_SpawnGhost;
        On.GhostWorldPresence.GhostMode_AbstractRoom_Vector2 += GhostWorldPresence_GhostMode_AbstractRoom_Vector2;
        On.Ghost.Update += Ghost_Update;
        On.RainWorldGame.ctor += RainWorldGame_ctor;
        On.RainWorldGame.Update += RainWorldGame_Update;
        On.RainWorldGame.RawUpdate += RainWorldGame_RawUpdate;
        new Hook(typeof(RainWorldGame).GetProperty("TimeSpeedFac").GetGetMethod(), typeof(MapExporter).GetMethod("RainWorldGame_ZeroProperty"), this);
        new Hook(typeof(RainWorldGame).GetProperty("InitialBlackSeconds").GetGetMethod(), typeof(MapExporter).GetMethod("RainWorldGame_ZeroProperty"), this);
        new Hook(typeof(RainWorldGame).GetProperty("FadeInTime").GetGetMethod(), typeof(MapExporter).GetMethod("RainWorldGame_ZeroProperty"), this);
        On.OverWorld.WorldLoaded += OverWorld_WorldLoaded;
        On.Room.ReadyForAI += Room_ReadyForAI;
        On.Room.Loaded += Room_Loaded;
        On.Room.ScreenMovement += Room_ScreenMovement;
        On.RoomCamera.DrawUpdate += RoomCamera_DrawUpdate;
        On.VoidSpawnGraphics.DrawSprites += VoidSpawnGraphics_DrawSprites;
        On.AntiGravity.BrokenAntiGravity.ctor += BrokenAntiGravity_ctor;
        On.GateKarmaGlyph.DrawSprites += GateKarmaGlyph_DrawSprites;
        On.WorldLoader.ctor_RainWorldGame_Name_bool_string_Region_SetupValues += WorldLoader_ctor_RainWorldGame_Name_bool_string_Region_SetupValues;

        orig(self);
    }

    private void Serializer_SerializeValue(On.Json.Serializer.orig_SerializeValue orig, Json.Serializer self, object value)
    {
        if (value is IJsonObject obj) {
            orig(self, obj.ToJson());
        }
        else {
            orig(self, value);
        }
    }

    // Consistent RNG ?
    private void RainWorld_Update(On.RainWorld.orig_Update orig, RainWorld self)
    {
        Random.InitState(0);
        orig(self);
    }

    #region fixes
    // shortcut consistency
    private void RoomCamera_DrawUpdate(On.RoomCamera.orig_DrawUpdate orig, RoomCamera self, float timeStacker, float timeSpeed)
    {
        if (self.room != null && self.room.shortcutsBlinking != null) {
            self.room.shortcutsBlinking = new float[self.room.shortcuts.Length, 4];
            for (int i = 0; i < self.room.shortcutsBlinking.GetLength(0); i++) {
                self.room.shortcutsBlinking[i, 3] = -1;
            }
        }
        orig(self, timeStacker, timeSpeed);
    }
    // no shake
    private void Room_ScreenMovement(On.Room.orig_ScreenMovement orig, Room self, Vector2? pos, Vector2 bump, float shake)
    {
        return;
    }
    // update faster
    private void RainWorldGame_RawUpdate(On.RainWorldGame.orig_RawUpdate orig, RainWorldGame self, float dt)
    {
        self.myTimeStacker += 2f;
        orig(self, dt);
    }
    //  no grav swithcing
    private void BrokenAntiGravity_ctor(On.AntiGravity.BrokenAntiGravity.orig_ctor orig, AntiGravity.BrokenAntiGravity self, int cycleMin, int cycleMax, RainWorldGame game)
    {
        orig(self, cycleMin, cycleMax, game);
        self.on = false;
        self.from = self.on ? 1f : 0f;
        self.to = self.on ? 1f : 0f;
        self.lights = self.to;
        self.counter = 40000;
    }
    // Make gate glyphs more visible
    private void GateKarmaGlyph_DrawSprites(On.GateKarmaGlyph.orig_DrawSprites orig, GateKarmaGlyph self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        orig(self, sLeaser, rCam, timeStacker, camPos);

        sLeaser.sprites[1].shader = FShader.defaultShader;
        sLeaser.sprites[1].color = Color.white;

        if (self.requirement == MoreSlugcats.MoreSlugcatsEnums.GateRequirement.RoboLock) {
            for (int i = 2; i < 11; i++) {
                sLeaser.sprites[i].shader = FShader.defaultShader;
                sLeaser.sprites[i].color = Color.white;
            }
        }
    }
    // zeroes some annoying fades
    public delegate float orig_PropertyToZero(RainWorldGame self);
    public float RainWorldGame_ZeroProperty(orig_PropertyToZero _, RainWorldGame _1)
    {
        return 0f;
    }
    // spawn ghost always
    private void World_SpawnGhost(On.World.orig_SpawnGhost orig, World self)
    {
        self.game.rainWorld.safariMode = false;
        orig(self);
        self.game.rainWorld.safariMode = true;
    }
    // spawn ghosts always, to show them on the map
    private bool GhostWorldPresence_SpawnGhost(On.GhostWorldPresence.orig_SpawnGhost orig, GhostWorldPresence.GhostID ghostID, int karma, int karmaCap, int ghostPreviouslyEncountered, bool playingAsRed)
    {
        return true;
    }
    // don't let them affect nearby rooms
    private float GhostWorldPresence_GhostMode_AbstractRoom_Vector2(On.GhostWorldPresence.orig_GhostMode_AbstractRoom_Vector2 orig, GhostWorldPresence self, AbstractRoom testRoom, Vector2 worldPos)
    {
        if (self.ghostRoom.name != testRoom.name) {
            return 0f;
        }
        return orig(self, testRoom, worldPos);
    }
    // don't let them hurl us back to the karma screen
    private void Ghost_Update(On.Ghost.orig_Update orig, Ghost self, bool eu)
    {
        orig(self, eu);
        self.fadeOut = self.lastFadeOut = 0f;
    }
    // setup == useful
    private RainWorldGame.SetupValues RainWorld_LoadSetupValues(On.RainWorld.orig_LoadSetupValues orig, bool distributionBuild)
    {
        var setup = orig(false);

        setup.loadAllAmbientSounds = false;
        setup.playMusic = false;

        setup.cycleTimeMax = 10000;
        setup.cycleTimeMin = 10000;

        setup.gravityFlickerCycleMin = 10000;
        setup.gravityFlickerCycleMax = 10000;

        setup.startScreen = false;
        setup.cycleStartUp = false;

        setup.player1 = false;
        setup.worldCreaturesSpawn = false;
        setup.singlePlayerChar = 0;

        return setup;
    }

    // fuck you in particular
    private void VoidSpawnGraphics_DrawSprites(On.VoidSpawnGraphics.orig_DrawSprites orig, VoidSpawnGraphics self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam, float timeStacker, Vector2 camPos)
    {
        //youre code bad
        for (int i = 0; i < sLeaser.sprites.Length; i++) {
            sLeaser.sprites[i].isVisible = false;
        }
    }

    // effects blacklist
    private void Room_Loaded(On.Room.orig_Loaded orig, Room self)
    {
        for (int i = self.roomSettings.effects.Count - 1; i >= 0; i--) {
            if (self.roomSettings.effects[i].type == RoomSettings.RoomEffect.Type.VoidSea) self.roomSettings.effects.RemoveAt(i); // breaks with no player
            else if (self.roomSettings.effects[i].type.ToString() == "CGCameraZoom") self.roomSettings.effects.RemoveAt(i); // bad for screenies
            else if (((int)self.roomSettings.effects[i].type) >= 27 && ((int)self.roomSettings.effects[i].type) <= 36) self.roomSettings.effects.RemoveAt(i); // insects bad for screenies
        }
        foreach (var item in self.roomSettings.placedObjects) {
            if (item.type == PlacedObject.Type.InsectGroup) item.active = false;
            if (item.type == PlacedObject.Type.FlyLure
                || item.type == PlacedObject.Type.JellyFish) self.waitToEnterAfterFullyLoaded = Mathf.Max(self.waitToEnterAfterFullyLoaded, 20);

        }
        orig(self);
    }

    // no orcacles
    private void Room_ReadyForAI(On.Room.orig_ReadyForAI orig, Room self)
    {
        string oldname = self.abstractRoom.name;
        if (self.abstractRoom.name.EndsWith("_AI")) self.abstractRoom.name = "XXX"; // oracle breaks w no player
        orig(self);
        self.abstractRoom.name = oldname;
    }

    // no gate switching
    private void OverWorld_WorldLoaded(On.OverWorld.orig_WorldLoaded orig, OverWorld self)
    {
        return; // orig assumes a gate
    }

    private void WorldLoader_ctor_RainWorldGame_Name_bool_string_Region_SetupValues(On.WorldLoader.orig_ctor_RainWorldGame_Name_bool_string_Region_SetupValues orig, WorldLoader self, RainWorldGame game, SlugcatStats.Name playerCharacter, bool singleRoomWorld, string worldName, Region region, RainWorldGame.SetupValues setupValues)
    {
        orig(self, game, playerCharacter, singleRoomWorld, worldName, region, setupValues);

        for (int i = self.lines.Count - 1; i > 0; i--) {
            string[] split1 = Regex.Split(self.lines[i], " : ");
            if (split1.Length != 3 || split1[1] != "EXCLUSIVEROOM") {
                continue;
            }
            string[] split2 = Regex.Split(self.lines[i - 1], " : ");
            if (split2.Length != 3 || split2[1] != "EXCLUSIVEROOM") {
                continue;
            }
            // If rooms match on both EXCLUSIVEROOM entries, but not characters, merge the characters.
            if (split1[0] != split2[0] && split1[2] == split2[2]) {
                string newLine = $"{split1[0]},{split2[0]} : EXCLUSIVEROOM : {split1[2]}";

                self.lines[i - 1] = newLine;
                self.lines.RemoveAt(i);
            }
        }
    }

    #endregion fixes

    // start
    private void RainWorldGame_ctor(On.RainWorldGame.orig_ctor orig, RainWorldGame self, ProcessManager manager)
    {
        // Use safari mode, it's very sanitary
        manager.rainWorld.safariMode = true;
        manager.rainWorld.safariRainDisable = true;
        manager.rainWorld.safariSlugcat = SlugcatStats.Name.White;
        manager.rainWorld.safariRegion = "SU";

        orig(self, manager);

        // No safari overseers
        if (self.cameras[0].followAbstractCreature != null) {
            self.cameras[0].followAbstractCreature.Room.RemoveEntity(self.cameras[0].followAbstractCreature);
            self.cameras[0].followAbstractCreature.realizedObject?.Destroy();
            self.cameras[0].followAbstractCreature = null;
        }
        self.roomRealizer.followCreature = null;
        self.roomRealizer = null;

        // misc wtf fixes
        self.GetStorySession.saveState.theGlow = false;
        self.rainWorld.setup.playerGlowing = false;

        // no tutorials
        self.GetStorySession.saveState.deathPersistentSaveData.KarmaFlowerMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.ScavMerchantMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.ScavTollMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.ArtificerTutorialMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.DangleFruitInWaterMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.GoExploreMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.KarmicBurstMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.SaintEnlightMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.SMTutorialMessage = true;
        self.GetStorySession.saveState.deathPersistentSaveData.TongueTutorialMessage = true;

        // allow Saint ghosts
        self.GetStorySession.saveState.cycleNumber = 1;

        Logger.LogDebug("Starting capture task");

        captureTask = CaptureTask(self);
    }

    private void RainWorldGame_Update(On.RainWorldGame.orig_Update orig, RainWorldGame self)
    {
        orig(self);
        captureTask.MoveNext();
    }

    static string PathOfRegion(string slugcat, string region)
    {
        return Directory.CreateDirectory(Path.Combine(Custom.LegacyRootFolderDirectory(), "export", slugcat.ToLower(), region.ToLower())).FullName;
    }

    static string PathOfSlugcatData()
    {
        return Path.Combine(Path.Combine(Custom.LegacyRootFolderDirectory(), "export", "slugcats.json"));
    }

    static string PathOfMetadata(string slugcat, string region)
    {
        return Path.Combine(PathOfRegion(slugcat, region), "metadata.json");
    }

    static string PathOfScreenshot(string slugcat, string region, string room, int num)
    {
        return $"{Path.Combine(PathOfRegion(slugcat, region), room.ToLower())}_{num}.png";
    }

    private static int ScugPriority(string slugcat)
    {
        return slugcat switch {
            "white" => 10,      // do White first, they have the most generic regions
            "artificer" => 9,   // do Artificer next, they have Metropolis, Waterfront Facility, and past-GW
            "saint" => 8,       // do Saint next for Undergrowth and Silent Construct
            "rivulet" => 7,     // do Rivulet for The Rot
            _ => 0              // everyone else has a mix of duplicate rooms
        };
    }

    // Runs half-synchronously to the game loop, bless iters
    System.Collections.IEnumerator captureTask;
    private System.Collections.IEnumerator CaptureTask(RainWorldGame game)
    {
        // Task start
        Logger.LogDebug("capture task start");
        Random.InitState(0);

        // 1st camera transition is a bit whack ? give it a sec to load
        //while (game.cameras[0].www != null) yield return null;
        while (game.cameras[0].room == null || !game.cameras[0].room.ReadyForPlayer) yield return null;
        for (int i = 0; i < 40; i++) yield return null;
        // ok game loaded I suppose
        game.cameras[0].room.abstractRoom.Abstractize();

        SlugcatFile slugcatsJson = new();

        if (captureSpecific?.Length > 0) {
            foreach (var capture in captureSpecific) {
                SlugcatStats.Name slugcat = new(capture.Split(';')[0]);

                game.GetStorySession.saveStateNumber = slugcat;
                game.GetStorySession.saveState.saveStateNumber = slugcat;

                slugcatsJson.AddCurrentSlugcat(game);

                foreach (var step in CaptureRegion(game, region: capture.Split(';')[1]))
                    yield return step;
            }
        }
        else {
            // Iterate over each region on each slugcat
            foreach (string slugcatName in SlugcatStats.Name.values.entries.OrderByDescending(ScugPriority)) {
                SlugcatStats.Name slugcat = new(slugcatName);

                if (SlugcatStats.HiddenOrUnplayableSlugcat(slugcat)) {
                    continue;
                }

                game.GetStorySession.saveStateNumber = slugcat;
                game.GetStorySession.saveState.saveStateNumber = slugcat;

                slugcatsJson.AddCurrentSlugcat(game);

                foreach (var region in SlugcatStats.getSlugcatStoryRegions(slugcat).Concat(SlugcatStats.getSlugcatOptionalRegions(slugcat))) {
                    foreach (var step in CaptureRegion(game, region))
                        yield return step;
                }
            }
        }

        File.WriteAllText(PathOfSlugcatData(), Json.Serialize(slugcatsJson));

        Logger.LogDebug("capture task done!");
        Application.Quit();
    }

    private System.Collections.IEnumerable CaptureRegion(RainWorldGame game, string region)
    {
        SlugcatStats.Name slugcat = game.StoryCharacter;

        // load region
        Random.InitState(0);
        game.overWorld.LoadWorld(region, slugcat, false);
        Logger.LogDebug($"Loaded {slugcat}/{region}");

        Directory.CreateDirectory(PathOfRegion(slugcat.value, region));

        RegionInfo mapContent = new(game.world);

        List<AbstractRoom> rooms = game.world.abstractRooms.ToList();

        // Don't image rooms not available for this slugcat
        rooms.RemoveAll(HiddenRoom);

        // Don't image offscreen dens
        rooms.RemoveAll(r => r.offScreenDen);

        if (ReusedRooms.SlugcatRoomsToUse(slugcat.value, game.world, rooms) is string copyRooms) {
            mapContent.copyRooms = copyRooms;
        }
        else {
            foreach (var room in rooms) {
                foreach (var step in CaptureRoom(room, mapContent))
                    yield return step;
            }
        }

        File.WriteAllText(PathOfMetadata(slugcat.value, region), Json.Serialize(mapContent));

        Logger.LogDebug("capture task done with " + region);
    }

    private System.Collections.IEnumerable CaptureRoom(AbstractRoom room, RegionInfo regionContent)
    {
        RainWorldGame game = room.world.game;

        // load room
        game.overWorld.activeWorld.loadingRooms.Clear();
        Random.InitState(0);
        game.overWorld.activeWorld.ActivateRoom(room);
        // load room until it is loaded
        if (game.overWorld.activeWorld.loadingRooms.Count > 0 && game.overWorld.activeWorld.loadingRooms[0].room == room.realizedRoom) {
            RoomPreparer loading = game.overWorld.activeWorld.loadingRooms[0];
            while (!loading.done) {
                loading.Update();
            }
        }
        while (!(room.realizedRoom.loadingProgress >= 3 && room.realizedRoom.waitToEnterAfterFullyLoaded < 1)) {
            room.realizedRoom.Update();
        }

        if (blacklistedCams.TryGetValue(room.name, out int[] cams)) {
            var newpos = room.realizedRoom.cameraPositions.ToList();
            for (int i = cams.Length - 1; i >= 0; i--) {
                newpos.RemoveAt(cams[i] - 1);
            }
            room.realizedRoom.cameraPositions = newpos.ToArray();
        }

        yield return null;
        Random.InitState(0);
        // go to room
        game.cameras[0].MoveCamera(room.realizedRoom, 0);
        game.cameras[0].virtualMicrophone.AllQuiet();
        // get to room
        while (game.cameras[0].loadingRoom != null) yield return null;
        Random.InitState(0);

        regionContent.UpdateRoom(room.realizedRoom);

        for (int i = 0; i < room.realizedRoom.cameraPositions.Length; i++) {
            // load screen
            Random.InitState(room.name.GetHashCode()); // allow for deterministic random numbers, to make rain look less garbage
            game.cameras[0].MoveCamera(i);
            game.cameras[0].virtualMicrophone.AllQuiet();
            while (game.cameras[0].www != null) yield return null;
            yield return null;
            yield return null; // one extra frame maybe
                               // fire!

            if (screenshots) {
                string filename = PathOfScreenshot(game.StoryCharacter.value, room.world.name, room.name, i);

                if (!File.Exists(filename)) {
                    ScreenCapture.CaptureScreenshot(filename);
                }
            }

            // palette and colors
            regionContent.LogPalette(game.cameras[0].currentPalette);

            yield return null; // one extra frame after ??
        }
        Random.InitState(0);
        room.Abstractize();
        yield return null;
    }
}
