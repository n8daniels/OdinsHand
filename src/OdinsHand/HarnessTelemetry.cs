using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>Where the local player was, and what the world looked like there, at one moment.</summary>
    public sealed class LocationSnapshot
    {
        public float X, Y, Z;
        public string Biome;
        public int ZoneX, ZoneY;
        public float Fps;
        public double Seconds;   // real seconds since the game started, so snapshots can be ordered and timed

        public Vector3 Position => new Vector3(X, Y, Z);

        /// <summary>Captures the local player's position. Returns null when there is no player to locate.</summary>
        public static LocationSnapshot Capture()
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null) return null;

                var p = player.transform.position;
                var zone = ZoneSystem.GetZone(p);
                return new LocationSnapshot
                {
                    X = p.x, Y = p.y, Z = p.z,
                    Biome = Heightmap.FindBiome(p).ToString(),
                    ZoneX = zone.x, ZoneY = zone.y,
                    Fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f,
                    Seconds = Time.realtimeSinceStartup,
                };
            }
            catch
            {
                return null;   // telemetry must never be the reason a run fails
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0}, {2:0.0}) {3}, zone {4},{5}", X, Y, Z, Biome, ZoneX, ZoneY);

        internal void AppendJson(StringBuilder b)
        {
            b.Append("{\"x\":").Append(F(X)).Append(",\"y\":").Append(F(Y)).Append(",\"z\":").Append(F(Z))
             .Append(",\"biome\":").Append(HarnessReport.Quote(Biome))
             .Append(",\"zoneX\":").Append(ZoneX).Append(",\"zoneY\":").Append(ZoneY)
             .Append(",\"fps\":").Append(F(Fps)).Append(",\"t\":").Append(F((float)Seconds)).Append('}');
        }

        private static string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>One case's start and end locations, so a run can show where each check actually happened.</summary>
    public sealed class CaseTelemetry
    {
        public string Case;
        public LocationSnapshot Start, End;
        public double Seconds;
    }

    /// <summary>
    /// Where things happened during a run. Recorded for every run and written to <c>report.json</c> under
    /// <c>"telemetry"</c>, to <c>report.txt</c>, and to the log.
    ///
    /// Added for mods with random spawn points: a failure that only happens in one biome, or far from the origin, is
    /// invisible in a report that says nothing about where the player was. <see cref="Spawns"/> holds the position each
    /// time the local player spawned (the first is the spawn location of the test character); <see cref="Cases"/> holds
    /// where each case began and ended.
    /// </summary>
    public sealed class HarnessTelemetry
    {
        private static readonly List<LocationSnapshot> SpawnList = new List<LocationSnapshot>();

        /// <summary>Every position the local player has spawned at this session, oldest first.</summary>
        public static IReadOnlyList<LocationSnapshot> Spawns => SpawnList;

        /// <summary>The most recent spawn, or null if the player has not spawned yet.</summary>
        public static LocationSnapshot LastSpawn => SpawnList.Count > 0 ? SpawnList[SpawnList.Count - 1] : null;

        internal static void RecordSpawn()
        {
            var s = LocationSnapshot.Capture();
            if (s != null) SpawnList.Add(s);
        }

        public string Character, World, GameVersion;
        public long Seed;
        public int Day;

        public LocationSnapshot RunStart, RunEnd;
        public readonly List<CaseTelemetry> Cases = new List<CaseTelemetry>();

        /// <summary>Fills in who and where; safe to call when little is loaded.</summary>
        internal void CaptureContext()
        {
            try { Character = Game.instance != null ? Game.instance.GetPlayerProfile()?.GetName() : null; } catch { }
            try { World = ZNet.instance != null ? ZNet.instance.GetWorldName() : null; } catch { }
            try { Seed = ZNet.World != null ? ZNet.World.m_seed : 0; } catch { }
            try { GameVersion = Version.GetVersionString(); } catch { }
            try { Day = EnvMan.instance != null ? EnvMan.instance.GetCurrentDay() : 0; } catch { }
        }

        /// <summary>Human-readable lines for the log and the text report.</summary>
        public IEnumerable<string> Describe()
        {
            yield return $"character '{Character}' in world '{World}' (seed {Seed}), game {GameVersion}, day {Day}";
            for (var i = 0; i < SpawnList.Count; i++)
                yield return $"spawn #{i + 1}: {SpawnList[i]}";
            if (RunStart != null) yield return "run started at: " + RunStart;
            if (RunEnd != null) yield return "run ended at:   " + RunEnd;
        }

        internal void AppendJson(StringBuilder b)
        {
            b.Append("{\"character\":").Append(HarnessReport.Quote(Character))
             .Append(",\"world\":").Append(HarnessReport.Quote(World))
             .Append(",\"seed\":").Append(Seed.ToString(CultureInfo.InvariantCulture))
             .Append(",\"gameVersion\":").Append(HarnessReport.Quote(GameVersion))
             .Append(",\"day\":").Append(Day);

            b.Append(",\"spawns\":[");
            for (var i = 0; i < SpawnList.Count; i++) { if (i > 0) b.Append(','); SpawnList[i].AppendJson(b); }
            b.Append(']');

            b.Append(",\"runStart\":"); if (RunStart != null) RunStart.AppendJson(b); else b.Append("null");
            b.Append(",\"runEnd\":"); if (RunEnd != null) RunEnd.AppendJson(b); else b.Append("null");

            b.Append(",\"cases\":[");
            for (var i = 0; i < Cases.Count; i++)
            {
                if (i > 0) b.Append(',');
                var c = Cases[i];
                b.Append("{\"case\":").Append(HarnessReport.Quote(c.Case))
                 .Append(",\"seconds\":").Append(c.Seconds.ToString("0.##", CultureInfo.InvariantCulture))
                 .Append(",\"start\":"); if (c.Start != null) c.Start.AppendJson(b); else b.Append("null");
                b.Append(",\"end\":"); if (c.End != null) c.End.AppendJson(b); else b.Append("null");
                b.Append('}');
            }
            b.Append("]}");
        }
    }
}
