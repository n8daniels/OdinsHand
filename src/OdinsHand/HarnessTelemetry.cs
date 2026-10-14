using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace OdinsHand
{
    /// <summary>The nearest point found of one biome, relative to a location.</summary>
    public sealed class BiomeDistance
    {
        public string Biome;
        public float Meters;
        /// <summary>Compass bearing to it in degrees, clockwise from north (north is +Z in Valheim).</summary>
        public float Bearing;
        public float X, Z;

        public string Compass
        {
            get
            {
                var names = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
                var deg = ((Bearing % 360f) + 360f) % 360f;
                return names[(int)Math.Round(deg / 45f) % 8];
            }
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0} {1:0} m {2}", Biome, Meters, Compass);
    }

    /// <summary>Finds the nearest sample of every biome other than the one a location is in.</summary>
    public static class BiomeSearch
    {
        /// <summary>Ring spacing in metres. The answer is accurate to about this far along a ray.</summary>
        public const float Step = 40f;
        /// <summary>How far to look. A biome further away than this is simply not reported.</summary>
        public const float MaxMeters = 3000f;
        /// <summary>Rays per ring. 32 rays is 11.25 degrees apart, so a thin biome between rays can be missed.</summary>
        public const int Rays = 32;

        /// <summary>
        /// Samples the world generator on expanding rings and, for each other biome first seen on a ring, bisects back along
        /// that ray to the biome's edge. Cheap enough for a few calls per run (a couple of thousand lookups), not per frame.
        /// Returns an empty list when no world generator is available.
        /// </summary>
        public static List<BiomeDistance> FindNearest(Vector3 from, float maxMeters = MaxMeters)
        {
            var found = new Dictionary<Heightmap.Biome, BiomeDistance>();
            var wg = WorldGenerator.instance;
            if (wg == null) return new List<BiomeDistance>();

            var here = wg.GetBiome(from.x, from.z);

            for (var r = Step; r <= maxMeters; r += Step)
            {
                for (var i = 0; i < Rays; i++)
                {
                    var angle = i * (2f * Mathf.PI / Rays);
                    var dx = Mathf.Sin(angle);   // angle 0 is north (+Z), clockwise
                    var dz = Mathf.Cos(angle);
                    var biome = wg.GetBiome(from.x + dx * r, from.z + dz * r);
                    if (biome == here || found.ContainsKey(biome)) continue;

                    // Bisect between the previous ring (assumed not this biome) and this one for the edge.
                    float lo = r - Step, hi = r;
                    for (var k = 0; k < 6; k++)
                    {
                        var mid = (lo + hi) * 0.5f;
                        if (wg.GetBiome(from.x + dx * mid, from.z + dz * mid) == biome) hi = mid; else lo = mid;
                    }

                    found[biome] = new BiomeDistance
                    {
                        Biome = biome.ToString(),
                        Meters = hi,
                        Bearing = angle * Mathf.Rad2Deg,
                        X = from.x + dx * hi,
                        Z = from.z + dz * hi,
                    };
                }
            }

            var list = new List<BiomeDistance>(found.Values);
            list.Sort((a, b) => a.Meters.CompareTo(b.Meters));
            return list;
        }
    }
    /// <summary>Where the local player was, and what the world looked like there, at one moment.</summary>
    public sealed class LocationSnapshot
    {
        public float X, Y, Z;
        public string Biome;
        public int ZoneX, ZoneY;
        public float Fps;
        public double Seconds;   // real seconds since the game started, so snapshots can be ordered and timed

        /// <summary>Nearest sample of each other biome (nearest first). Empty unless captured with biomes.</summary>
        public List<BiomeDistance> OtherBiomes = new List<BiomeDistance>();

        public Vector3 Position => new Vector3(X, Y, Z);

        /// <summary>
        /// Captures the local player's position. Returns null when there is no player to locate. With <paramref name="biomes"/> it
        /// also measures the distance to each other biome (see <see cref="BiomeSearch"/>), which costs a couple of thousand world lookups.
        /// </summary>
        public static LocationSnapshot Capture(bool biomes = false)
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player == null) return null;

                var p = player.transform.position;
                var zone = ZoneSystem.GetZone(p);
                var snapshot = new LocationSnapshot
                {
                    X = p.x, Y = p.y, Z = p.z,
                    Biome = Heightmap.FindBiome(p).ToString(),
                    ZoneX = zone.x, ZoneY = zone.y,
                    Fps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f,
                    Seconds = Time.realtimeSinceStartup,
                };
                if (biomes) snapshot.OtherBiomes = BiomeSearch.FindNearest(p);
                return snapshot;
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
             .Append(",\"fps\":").Append(F(Fps)).Append(",\"t\":").Append(F((float)Seconds));
            b.Append(",\"nearestBiomes\":{");
            for (var i = 0; i < OtherBiomes.Count; i++)
            {
                var o = OtherBiomes[i];
                if (i > 0) b.Append(',');
                b.Append(HarnessReport.Quote(o.Biome)).Append(":{\"meters\":").Append(F(o.Meters)).Append(",\"bearing\":").Append(F(o.Bearing))
                 .Append(",\"compass\":").Append(HarnessReport.Quote(o.Compass)).Append(",\"x\":").Append(F(o.X)).Append(",\"z\":").Append(F(o.Z)).Append('}');
            }
            b.Append("}}");
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
            var s = LocationSnapshot.Capture(biomes: true);
            if (s != null) SpawnList.Add(s);
        }

        public string Character, World, GameVersion;
        /// <summary>Which Odin's Hand and BepInEx produced the report, and what else was loaded. For bug reports.</summary>
        public string OdinsHandVersion, BepInExVersion;
        public readonly List<string> Plugins = new List<string>();
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
            try { OdinsHandVersion = typeof(HarnessTelemetry).Assembly.GetName().Version.ToString(3); } catch { }
            try { BepInExVersion = typeof(BepInEx.BaseUnityPlugin).Assembly.GetName().Version.ToString(); } catch { }
            try
            {
                Plugins.Clear();
                foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                    Plugins.Add(info.Metadata.GUID + " " + info.Metadata.Version);
                Plugins.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
        }

        /// <summary>Human-readable lines for the log and the text report.</summary>
        public IEnumerable<string> Describe()
        {
            yield return $"character '{Character}' in world '{World}' (seed {Seed}), game {GameVersion}, day {Day}";
            yield return $"Odin's Hand {OdinsHandVersion}, BepInEx {BepInExVersion}, {Plugins.Count} plugin(s): {string.Join(", ", Plugins.ToArray())}";
            for (var i = 0; i < SpawnList.Count; i++)
            {
                yield return $"spawn #{i + 1}: {SpawnList[i]}";
                if (SpawnList[i].OtherBiomes.Count > 0)
                    yield return "  nearest other biomes: " + string.Join(", ", SpawnList[i].OtherBiomes.ConvertAll(b => b.ToString()).ToArray());
            }
            if (RunStart != null) yield return "run started at: " + RunStart;
            if (RunEnd != null)
            {
                yield return "run ended at:   " + RunEnd;
                if (RunEnd.OtherBiomes.Count > 0)
                    yield return "  nearest other biomes: " + string.Join(", ", RunEnd.OtherBiomes.ConvertAll(b => b.ToString()).ToArray());
            }
        }

        internal void AppendJson(StringBuilder b)
        {
            b.Append("{\"character\":").Append(HarnessReport.Quote(Character))
             .Append(",\"world\":").Append(HarnessReport.Quote(World))
             .Append(",\"seed\":").Append(Seed.ToString(CultureInfo.InvariantCulture))
             .Append(",\"gameVersion\":").Append(HarnessReport.Quote(GameVersion))
             .Append(",\"day\":").Append(Day)
             .Append(",\"odinsHandVersion\":").Append(HarnessReport.Quote(OdinsHandVersion))
             .Append(",\"bepInExVersion\":").Append(HarnessReport.Quote(BepInExVersion));
            b.Append(",\"plugins\":[");
            for (var i = 0; i < Plugins.Count; i++) { if (i > 0) b.Append(','); b.Append(HarnessReport.Quote(Plugins[i])); }
            b.Append(']');

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
