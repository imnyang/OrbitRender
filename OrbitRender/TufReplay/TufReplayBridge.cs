using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OrbitRender.TufReplay
{
    internal sealed class RecordedRun
    {
        internal string Id;
        internal string StartedUtc;
        internal string Result;
        internal int StartTile;
    }

    // TUFReplay owns the database and export lifecycle. Resolve its runtime at
    // execution time so OrbitRender still starts when TUFReplay is not installed.
    internal static class TufReplayBridge
    {
        private static Type FindType(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
            if (type == null) throw new InvalidOperationException("TUFReplay is not enabled or its version does not support render export.");
            return type;
        }

        private static object Invoke(Type type, string method, params object[] args)
        {
            try
            {
                var target = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
                if (target == null) throw new MissingMethodException(type.FullName, method);
                return target.Invoke(null, args);
            }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }

        internal static List<RecordedRun> ListRuns(string currentLevelPath)
        {
            var results = new List<RecordedRun>();
            var database = FindType("TUFReplay.Shared.Database.Database");
            using (var connection = (DbConnection)Invoke(database, "OpenConnection"))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT r.id,r.started_at_utc,r.result,r.start_tile,g.adofai_path FROM runs r JOIN level_sessions s ON s.id=r.level_session_id JOIN levels g ON g.id=s.level_id JOIN replay_artifacts a ON a.run_id=r.id WHERE r.replay_unavailable_reason IS NULL ORDER BY r.started_at_utc DESC LIMIT 1000";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read() && results.Count < 50)
                    {
                        if (!PathsEqual(currentLevelPath, reader.GetString(4))) continue;
                        results.Add(new RecordedRun { Id = reader.GetString(0), StartedUtc = reader.GetString(1), Result = reader.GetString(2), StartTile = reader.GetInt32(3) });
                    }
                }
            }
            return results;
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
            try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); }
            catch (Exception) { return false; }
        }

        internal static object Start(string id, string levelPath)
        {
            TufReplayKoreanKeyCompatibility.EnsureInstalled();
            return Invoke(FindType("TUFReplay.Replay.Export.RenderBundleExportService"), "Start", id, levelPath, true, true);
        }
        internal static object Status(string id) => Invoke(FindType("TUFReplay.Replay.Export.RenderBundleExportService"), "GetStatus", id);
        internal static void Cancel(string id) => Invoke(FindType("TUFReplay.Replay.Export.RenderBundleExportService"), "Cancel", id);
        internal static string Read(object status, string name) => status?.GetType().GetProperty(name)?.GetValue(status, null) as string;
    }
}
