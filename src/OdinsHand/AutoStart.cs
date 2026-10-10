using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>
    /// Loads a character and world straight from the main menu, so a build can verify
    /// itself without a person clicking through the UI.
    ///
    /// This is the piece that turns "launch the game and drive it by hand" into "launch
    /// the game and read the report". Without it the harness still needs someone to press
    /// Start, which is most of the cost of testing a change.
    ///
    /// DEVELOPMENT ONLY, off unless configured, and in an assembly that is never shipped.
    /// It picks a named save deliberately rather than "the first one" so it cannot wander
    /// into somebody's real character.
    /// </summary>
    public static class AutoStart
    {
        private static readonly System.Reflection.MethodInfo SetSelectedProfile =
            AccessTools.Method(typeof(FejdStartup), "SetSelectedProfile", new[] { typeof(string) });

        private static readonly AccessTools.FieldRef<FejdStartup, World> WorldRef =
            AccessTools.FieldRefAccess<FejdStartup, World>("m_world");

        private static readonly System.Reflection.MethodInfo SetServerPassword =
            AccessTools.PropertySetter(typeof(FejdStartup), "ServerPassword");

        private static readonly AccessTools.FieldRef<FejdStartup, int> ProfileIndexRef =
            AccessTools.FieldRefAccess<FejdStartup, int>("m_profileIndex");

        private static readonly AccessTools.FieldRef<FejdStartup, List<PlayerProfile>> ProfilesRef =
            AccessTools.FieldRefAccess<FejdStartup, List<PlayerProfile>>("m_profiles");

        // ZNet.m_connectionStatus is public in the publicized assembly and inaccessible at
        // runtime, like every other publicized member. Reading it directly cost the WHOLE
        // method: the JIT rejects MoveNext when it is compiled, so not one line of the
        // coroutine ran - including a log statement placed at the very top specifically to
        // prove whether it was running. Silence, with the cause three screens further down.
        private static readonly AccessTools.FieldRef<ZNet.ConnectionStatus> ConnectionStatusRef =
            AccessTools.StaticFieldRefAccess<ZNet.ConnectionStatus>(
                AccessTools.Field(typeof(ZNet), "m_connectionStatus"));

        private static readonly AccessTools.FieldRef<FejdStartup, GameObject> PlayerInstanceRef =
            AccessTools.FieldRefAccess<FejdStartup, GameObject>("m_playerInstance");

        private static bool _attempted;

        /// <summary>
        /// Creates a local test character, the same way the new-character screen does.
        ///
        /// Local rather than cloud on purpose: this is a disposable test character and has
        /// no business consuming somebody's Steam cloud quota or syncing between machines.
        /// </summary>
        private static bool TryCreateCharacter(FejdStartup startup, string characterName, Action<string> log)
        {
            try
            {
                var filename = characterName.ToLowerInvariant();

                if (PlayerProfile.HaveProfile(filename))
                {
                    log($"[autostart] a profile file for '{filename}' exists but did not list; not overwriting it.");
                    return false;
                }

                var profile = new PlayerProfile(filename, FileHelpers.FileSource.Local);
                profile.SetName(characterName);

                // The menu keeps a preview character standing on the title screen. Saving
                // from it is what gives the new character a body and starting gear; without
                // it the profile exists but has nothing in it.
                var preview = PlayerInstanceRef(startup);
                var player = preview != null ? preview.GetComponent<Player>() : null;

                if (player != null)
                {
                    player.GiveDefaultItems();
                    profile.SavePlayerData(player);
                }
                else
                {
                    log("[autostart] no preview character to copy; creating an empty profile.");
                }

                if (!profile.Save())
                {
                    log($"[autostart] could not save a new character '{characterName}'.");
                    return false;
                }

                log($"[autostart] created character '{characterName}' (local).");
                return true;
            }
            catch (Exception e)
            {
                log($"[autostart] creating character '{characterName}' failed: {e.Message}");
                return false;
            }
        }

        /// <summary>Creates a local test world, the way the new-world screen does (see FejdStartup.OnNewWorldDone).</summary>
        private static World TryCreateWorld(string worldName, Action<string> log)
        {
            try
            {
                if (World.HaveWorld(worldName))
                {
                    log($"[autostart] a world file for '{worldName}' exists but did not list; not overwriting it.");
                    return null;
                }

                var world = new World(worldName, "odinshand");
                world.m_fileSource = FileHelpers.FileSource.Local;
                world.m_needsDB = false;
                SaveSystem.SetSaveNumber(0u);
                world.SaveWorldFWLData(DateTime.Now);
                SaveSystem.InvalidateCache(SaveDataType.World);

                var created = SaveSystem.GetWorldList().FirstOrDefault(w =>
                    string.Equals(w.m_name, worldName, StringComparison.OrdinalIgnoreCase));
                log(created != null
                    ? $"[autostart] created world '{worldName}' (local, seed 'odinshand')."
                    : $"[autostart] saved world '{worldName}' but it did not list.");
                return created;
            }
            catch (Exception e)
            {
                log($"[autostart] creating world '{worldName}' failed: {e.Message}");
                return null;
            }
        }

        public static IEnumerator Run(
            string characterName, string worldName, Action<string> log,
            string serverAddress = null, string serverPassword = null)
        {
            if (_attempted) yield break;
            _attempted = true;

            // Logged before anything can go wrong, so a run that produces no autostart
            // output at all is distinguishable from one that never got here.
            log($"[autostart] character='{characterName}' world='{worldName}' " +
                $"server='{serverAddress}'.");

            if (string.IsNullOrEmpty(characterName) ||
                (string.IsNullOrEmpty(worldName) && string.IsNullOrEmpty(serverAddress)))
            {
                log("[autostart] character and neither world nor server configured; staying on the menu.");
                yield break;
            }

            // The menu needs a moment to finish building its lists.
            var deadline = Time.realtimeSinceStartup + 30f;
            while (FejdStartup.instance == null && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (FejdStartup.instance == null)
            {
                log("[autostart] FejdStartup never appeared; giving up.");
                yield break;
            }

            yield return new WaitForSeconds(2f);

            var startup = FejdStartup.instance;

            // --- character ---------------------------------------------------
            string profileFile = null;
            try
            {
                var profiles = SaveSystem.GetAllPlayerProfiles();
                log($"[autostart] {profiles.Count} character(s): " +
                    string.Join(", ", profiles.Select(p => p.GetName()).ToArray()));

                var profile = profiles.FirstOrDefault(p =>
                    string.Equals(p.GetName(), characterName, StringComparison.OrdinalIgnoreCase));

                if (profile == null)
                {
                    // Make one rather than give up.
                    //
                    // Characters are per Steam ACCOUNT - they live in that account's
                    // cloud - so a second account, or a second machine, starts with none
                    // of them. This turned a two-client setup into "create a character by
                    // hand on every install before anything can run", and it is thirty
                    // seconds of clicking that the harness can simply do.
                    if (TryCreateCharacter(startup, characterName, log))
                        profile = SaveSystem.GetAllPlayerProfiles().FirstOrDefault(p =>
                            string.Equals(p.GetName(), characterName, StringComparison.OrdinalIgnoreCase));

                    if (profile == null)
                    {
                        log($"[autostart] no character named '{characterName}' and none could be created; " +
                            "staying on the menu.");
                        yield break;
                    }
                }

                profileFile = profile.GetFilename();
            }
            catch (Exception e)
            {
                log($"[autostart] listing characters failed: {e.Message}");
                yield break;
            }

            try
            {
                SetSelectedProfile?.Invoke(startup, new object[] { profileFile });
                log($"[autostart] selected character '{characterName}'.");
            }
            catch (Exception e)
            {
                log($"[autostart] selecting the character failed: {e.Message}");
                yield break;
            }

            yield return new WaitForSeconds(1f);

            // --- server, if one was configured -------------------------------
            // A world name and a server address are alternatives, not companions: joining
            // means the world comes from the server, so anything set here would be ignored.
            if (!string.IsNullOrEmpty(serverAddress))
            {
                try
                {
                    // Vanilla's own path, the one "+connect ip:port" uses. Setting the
                    // password statically first is what stops the client stopping on a
                    // dialog no automated run can answer.
                    // Through reflection, not directly. ServerPassword's setter is
                    // publicized at COMPILE time only - assigning it compiles cleanly and
                    // throws MethodAccessException at runtime, which is the same trap that
                    // killed the Phase 0 spike report and would have thrown on every kill
                    // in MasteryService.
                    SetServerPassword?.Invoke(null, new object[] { serverPassword ?? string.Empty });
                    ZSteamMatchmaking.instance.QueueServerJoin(serverAddress);
                    log($"[autostart] queued join for '{serverAddress}'.");
                }
                catch (Exception e)
                {
                    log($"[autostart] joining '{serverAddress}' failed: {e.Message}");
                    yield break;
                }

                // QueueServerJoin only PARKS the join - it drops the menu on character
                // selection and waits, because the profile chosen earlier is not the one
                // the join path reads. OnCharacterStart is what picks the queued join back
                // up, and it reads m_profileIndex rather than the selected filename.
                yield return new WaitForSeconds(1.5f);

                var started = false;
                try
                {
                    // Re-read, and ASSIGN what was read back onto the menu.
                    //
                    // OnCharacterStart indexes m_profiles, so the index computed here has
                    // to come from the very list it will index. Reading a fresh list and
                    // leaving the menu's stale one in place is how a character that had
                    // just been created came back as "not in the join character list" -
                    // it was on disk, and the list it was looked up in predated it.
                    var profiles = SaveSystem.GetAllPlayerProfiles();
                    ProfilesRef(startup) = profiles;

                    var index = profiles.FindIndex(p =>
                        string.Equals(p.GetName(), characterName, StringComparison.OrdinalIgnoreCase));

                    if (index < 0)
                    {
                        log($"[autostart] '{characterName}' is not in the join character list.");
                    }
                    else
                    {
                        ProfileIndexRef(startup) = index;
                        startup.OnCharacterStart();
                        started = true;
                        log($"[autostart] confirmed '{characterName}' and joining.");
                    }
                }
                catch (Exception e)
                {
                    log($"[autostart] confirming the character failed: {e.Message}");
                }

                if (!started) yield break;

                // Report where it got to. A join can fail long after the call returns -
                // wrong password, version mismatch, server not up - and silence would look
                // identical to success.
                var connectDeadline = Time.realtimeSinceStartup + 60f;
                while (Time.realtimeSinceStartup < connectDeadline)
                {
                    if (ConnectionStatusRef() == ZNet.ConnectionStatus.Connected)
                    {
                        log("[autostart] connected to the server.");
                        yield break;
                    }

                    if (ConnectionStatusRef() != ZNet.ConnectionStatus.None &&
                        ConnectionStatusRef() != ZNet.ConnectionStatus.Connecting)
                    {
                        log($"[autostart] connection failed: {ConnectionStatusRef()}.");
                        yield break;
                    }

                    yield return new WaitForSeconds(1f);
                }

                log($"[autostart] still not connected after 60s (status {ConnectionStatusRef()}).");
                yield break;
            }

            // --- world -------------------------------------------------------
            try
            {
                var worlds = SaveSystem.GetWorldList();
                log($"[autostart] {worlds.Count} world(s): " +
                    string.Join(", ", worlds.Select(w => w.m_name).ToArray()));

                var world = worlds.FirstOrDefault(w =>
                    string.Equals(w.m_name, worldName, StringComparison.OrdinalIgnoreCase));

                if (world == null)
                {
                    // Make one rather than give up, like the character above. A fixed seed so the
                    // test world is the same on every machine. Local, never cloud: it is disposable.
                    world = TryCreateWorld(worldName, log);
                    if (world == null)
                    {
                        log($"[autostart] no world named '{worldName}' and none could be created; staying on the menu.");
                        yield break;
                    }
                }

                WorldRef(startup) = world;
                log($"[autostart] selected world '{worldName}'.");
            }
            catch (Exception e)
            {
                log($"[autostart] selecting the world failed: {e.Message}");
                yield break;
            }

            yield return new WaitForSeconds(1f);

            try
            {
                startup.OnWorldStart();
                log("[autostart] starting the game.");
            }
            catch (Exception e)
            {
                log($"[autostart] starting the game failed: {e.Message}");
            }
        }
    }
}
