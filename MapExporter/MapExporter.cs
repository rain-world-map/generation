using System.Linq;
using System.Security.Permissions;
using System.IO;
using RWCustom;
using UnityEngine;
using MonoMod.RuntimeDetour;
using System.Collections.Generic;
using BepInEx;

#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace MapExporter;

[BepInPlugin("io.github.henpemaz-dual", "Map Exporter", "1.0.0")]
sealed class MapExporter : BaseUnityPlugin
{
    // Config
    static readonly string captureSpecific = null; // Set to "White;SU" to load Outskirts as Survivor, or null to load all
    static readonly string exportFolderName = "export"; // Drops all screenshots in `Rain World/export` folder

    readonly Dictionary<string, int[]> blacklistedCams = new()
    {
        { "SU_B13", new int[]{2} }, // one indexed
        { "GW_S08", new int[]{2} }, // in vanilla only
        { "SL_C01", new int[]{4,5} }, // crescent order or will break
    };

    public void OnEnable()
    {
        On.RainWorld.Start += RainWorld_Start; // "FUCK compatibility just run my hooks" - love you too henpemaz
    }

    private void RainWorld_Start(On.RainWorld.orig_Start orig, RainWorld self)
    {
        On.Json.Serializer.SerializeValue += Serializer_SerializeValue;
        On.RainWorld.LoadSetupValues += RainWorld_LoadSetupValues;
        On.RainWorld.Update += RainWorld_Update;
        On.World.SpawnGhost += World_SpawnGhost;
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

    // no ghosts
    private void World_SpawnGhost(On.World.orig_SpawnGhost orig, World self)
    {
        return;
    }

    // setup == useful
    private RainWorldGame.SetupValues RainWorld_LoadSetupValues(On.RainWorld.orig_LoadSetupValues orig, bool distributionBuild)
    {
        var setup = orig(false);

        setup.loadAllAmbientSounds = false;
        setup.playMusic = false;
        // this broke CRS somehow smh
        //setup.loadProg = false;
        //setup.loadGame = false;

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

    #endregion fixes

    // start
    private void RainWorldGame_ctor(On.RainWorldGame.orig_ctor orig, RainWorldGame self, ProcessManager manager)
    {
        // Use safari mode, it's very sanitary
        manager.rainWorld.safariMode = true;
        manager.rainWorld.safariRainDisable = true;

        if (captureSpecific != null) {
            manager.rainWorld.safariSlugcat = new(captureSpecific.Split(';')[0]);
            manager.rainWorld.safariRegion = captureSpecific.Split(';')[1];
        }
        else {
            manager.rainWorld.safariSlugcat = SlugcatStats.Name.White;
            manager.rainWorld.safariRegion = "CC";
        }

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

        // no tutorials (cause nullrefs)
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

        Logger.LogDebug("RW Ctor done, starting capture task");
        //self.overWorld.activeWorld.activeRooms[0].abstractRoom.Abstractize();

        captureTask = CaptureTask(self);
        //captureTask.MoveNext();
    }

    private void RainWorldGame_Update(On.RainWorldGame.orig_Update orig, RainWorldGame self)
    {
        orig(self);
        captureTask.MoveNext();
    }

    string PathOfRegion(string slugcat, string region)
    {
        return Path.Combine(Custom.LegacyRootFolderDirectory(), exportFolderName, slugcat.ToLower(), region.ToLower());
    }

    string PathOfMetadata(string slugcat, string region)
    {
        return Path.Combine(PathOfRegion(slugcat, region), "metadata.json");
    }

    string PathOfScreenshot(string slugcat, string region, string room, int num)
    {
        return $"{Path.Combine(PathOfRegion(slugcat, region), room.ToLower())}_{num}.png";
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

        Cache cache = new();

        if (captureSpecific != null) {
            // Capture specific region on specific slugcat
            foreach (var step in CaptureRegion(cache, game, slugcat: new(captureSpecific.Split(';')[0]), region: captureSpecific.Split(';')[1]))
                yield return step;
        }
        else {
            // Iterate over each region on each slugcat
            foreach (string slugcat in SlugcatStats.Name.values.entries) {
                SlugcatStats.Name scug = new(slugcat);

                if (SlugcatStats.HiddenOrUnplayableSlugcat(scug)) {
                    continue;
                }

                foreach (var region in SlugcatStats.getSlugcatStoryRegions(scug).Concat(SlugcatStats.getSlugcatOptionalRegions(scug))) {
                    foreach (var step in CaptureRegion(cache, game, scug, region))
                        yield return step;
                }
            }
        }

        if (exportFolderName != null) {
            foreach (var cachedRegionMetadata in cache.metadata) {
                File.WriteAllText(PathOfMetadata("cached", cachedRegionMetadata.Key), Json.Serialize(cachedRegionMetadata.Value));
            }
        }

        Logger.LogDebug("capture task done!");
        Application.Quit();
    }

    enum CaptureMode
    {
        JustMetadata = 0, Cache = 1, SpecificSlugcat = 2
    }

    private System.Collections.IEnumerable CaptureRegion(Cache cache, RainWorldGame game, SlugcatStats.Name slugcat, string region)
    {
        // load region
        if (game.overWorld.activeWorld == null || game.overWorld.activeWorld.region.name != region) {
            Random.InitState(0);
            game.GetStorySession.saveStateNumber = slugcat;
            game.GetStorySession.saveState.saveStateNumber = slugcat;
            game.overWorld.LoadWorld(region, slugcat, false);
            Logger.LogDebug("capture task loaded " + region);
        }

        Directory.CreateDirectory(PathOfRegion(slugcat.value, region));
        Directory.CreateDirectory(PathOfRegion("Cached", region));

        List<AbstractRoom> rooms = game.world.abstractRooms.ToList();

        // Don't image rooms not available for this slugcat
        rooms.RemoveAll(r => game.world.DisabledMapRooms.Contains(r.name));

        // Don't image offscreen dens
        rooms.RemoveAll(r => r.offScreenDen);

        MapContent mapContent = new(game.world);

        foreach (var room in rooms) {
            RoomSettings cached = cache.settings.TryGetValue(room.name, out RoomSettings c) ? c : null;
            RoomSettings roomSettings = Settings(room);

            CaptureMode mode;

            if (cached != null) {
                if (Identical(cached, roomSettings)) {
                    mode = CaptureMode.JustMetadata;
                }
                else {
                    Logger.LogDebug($"{room.name} on {slugcat} is different from cached room, capturing");
                    mode = CaptureMode.SpecificSlugcat;
                }
            }
            else {
                cache.settings[room.name] = roomSettings;
                mode = CaptureMode.Cache;
            }

            foreach (var step in CaptureRoom(cache, room, mapContent, mode))
                yield return step;
        }

        if (exportFolderName != null) {
            File.WriteAllText(PathOfMetadata(slugcat.value, region), Json.Serialize(mapContent));
        }

        Logger.LogDebug("capture task done with " + region);
    }

    private System.Collections.IEnumerable CaptureRoom(Cache cache, AbstractRoom room, MapContent mapContent, CaptureMode mode)
    {
        var game = room.world.game;

        string filename(int i) => mode switch {
            CaptureMode.Cache => PathOfScreenshot("Cached", room.world.region.name, room.name, i),
            CaptureMode.SpecificSlugcat => PathOfScreenshot(game.StoryCharacter.value, room.world.region.name, room.name, i),
            _ => null,
        };

        // Don't bother if file is already captured (extremely helpful for debugging)
        if (File.Exists(filename(0))) {
            yield break;
        }

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

        mapContent.UpdateRoom(room.realizedRoom);

        if (mode == CaptureMode.Cache) {
            if (!cache.metadata.TryGetValue(room.world.name, out MapContent cachedContent)) {
                cache.metadata[room.world.name] = cachedContent = new(room.world);
            }
            cachedContent.UpdateRoom(room.realizedRoom);
        }

        // on each camera
        for (int i = 0; i < room.realizedRoom.cameraPositions.Length; i++) {
            //Logger.LogDebug("capture task camera " + i);
            //Logger.LogDebug("capture task camera has " + room.realizedRoom.cameraPositions.Length + " positions");
            // load screen
            Random.InitState(room.name.GetHashCode()); // allow for deterministic random numbers, to make rain look less garbage
            game.cameras[0].MoveCamera(i);
            game.cameras[0].virtualMicrophone.AllQuiet();
            while (game.cameras[0].www != null) yield return null;
            yield return null;
            yield return null; // one extra frame maybe
                               // fire!
            if (exportFolderName != null && mode != CaptureMode.JustMetadata) {
                ScreenCapture.CaptureScreenshot(filename(i));
            }

            // palette and colors
            mapContent.LogPalette(game.cameras[0].currentPalette);
            yield return null; // one extra frame after ??
        }
        Random.InitState(0);
        room.Abstractize();
        yield return null;
    }

    static RoomSettings Settings(AbstractRoom r)
    {
        return new RoomSettings(r.name, r.world.region, false, false, r.world.game.StoryCharacter);
    }
    bool Identical(RoomSettings one, RoomSettings two)
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
