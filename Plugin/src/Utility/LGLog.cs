using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace LethalGargoyles.src.Utility
{
    /// <summary>
    /// Severity levels, ordered. A message is emitted when its level is at or below
    /// the level configured for its category.
    /// </summary>
    public enum LGLevel
    {
        Off = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Debug = 4,
        Trace = 5
    }

    /// <summary>
    /// Subsystems that can be logged independently. Mirrors the numbered banner sections
    /// in LethalGargoylesAI.cs, plus the non-AI subsystems.
    /// </summary>
    [Flags]
    public enum LogCat
    {
        None = 0,
        Lifecycle = 1 << 0,
        StateMachine = 1 << 1,
        Targeting = 1 << 2,
        Movement = 1 << 3,
        Perception = 1 << 4,
        Combat = 1 << 5,
        Taunt = 1 << 6,
        Audio = 1 << 7,
        Netcode = 1 << 8,
        Config = 1 << 9,
        Scrap = 1 << 10
    }

    /// <summary>
    /// Runtime-gated diagnostic logging.
    ///
    /// This exists because <c>[Conditional("DEBUG")]</c> cannot do the job: it strips the
    /// CALL but not the code around it (which is how a Stopwatch and a pile of interpolated
    /// strings leaked into Release builds), it is all-or-nothing, and it requires shipping a
    /// DEBUG build to get any detail at all - useless for a player reporting a bug.
    ///
    /// Levels here are read from the BepInEx config and are live in RELEASE. Default is Warn
    /// for everything, so a normal player's log stays quiet, but a player chasing a bug can be
    /// told to set one category to Debug and send the log.
    ///
    /// HOT PATHS MUST GUARD THE CALL:
    ///     if (LGLog.On(LogCat.Movement, LGLevel.Trace))
    ///         LGLog.Trace(LogCat.Movement, $"...");
    /// The guard is what keeps the interpolated string from being built when the category is
    /// off. C# 10 interpolated string handlers would make this automatic, but they need a
    /// .NET 6 BCL type and this targets netstandard2.1 - so the guard is explicit, on purpose.
    /// Cold paths (startup, round transitions, error handlers) can call directly.
    /// </summary>
    internal static class LGLog
    {
        private static readonly Dictionary<LogCat, LGLevel> Levels = [];
        private static LGLevel _defaultLevel = LGLevel.Warn;
        private static bool _enabled = true;
        private static bool _includeContext = true;
        private static int _repeatLimit = 20;

        // Repeat suppression. Several of the bugs this was built to find run every frame;
        // unsuppressed they would bury everything else in the log.
        private static readonly Dictionary<string, int> RepeatCounts = [];
        private static readonly HashSet<string> FiredOnce = [];

        internal static void Configure(
            bool enabled,
            LGLevel defaultLevel,
            Dictionary<LogCat, LGLevel> perCategory,
            bool includeContext,
            int repeatLimit)
        {
            _enabled = enabled;
            _defaultLevel = defaultLevel;
            _includeContext = includeContext;
            _repeatLimit = repeatLimit;
            Levels.Clear();
            foreach (var kvp in perCategory) Levels[kvp.Key] = kvp.Value;
        }

        /// <summary>
        /// True when <paramref name="level"/> would be emitted for <paramref name="cat"/>.
        /// Two integer comparisons and a dictionary hit - cheap enough for a per-frame guard.
        /// </summary>
        internal static bool On(LogCat cat, LGLevel level)
        {
            if (!_enabled) return false;
            LGLevel configured = Levels.TryGetValue(cat, out var l) ? l : _defaultLevel;
            return configured != LGLevel.Off && level <= configured;
        }

        internal static void Error(LogCat cat, string msg) => Write(cat, LGLevel.Error, msg);
        internal static void Warn(LogCat cat, string msg) => Write(cat, LGLevel.Warn, msg);
        internal static void Info(LogCat cat, string msg) => Write(cat, LGLevel.Info, msg);
        internal static void Debug(LogCat cat, string msg) => Write(cat, LGLevel.Debug, msg);
        internal static void Trace(LogCat cat, string msg) => Write(cat, LGLevel.Trace, msg);

        /// <summary>Emits once per <paramref name="key"/> per session. For "this should be impossible".</summary>
        internal static void Once(LogCat cat, LGLevel level, string key, string msg)
        {
            if (!On(cat, level)) return;
            if (!FiredOnce.Add(key)) return;
            Write(cat, level, msg);
        }

        /// <summary>
        /// Fires at Error once per <paramref name="key"/> when <paramref name="condition"/> is false.
        ///
        /// This is the point of the whole file. Every feature the audit found broken shared one
        /// shape: a value that was supposed to stay in sync silently didn't. Nothing threw and
        /// nothing logged, which is how they survived to 0.7.0. An invariant that fires once and
        /// names both sides of the desync turns that class of bug into a single log line.
        /// </summary>
        internal static void Invariant(bool condition, LogCat cat, string key, string msg)
        {
            if (condition) return;
            if (!_enabled) return;
            if (!FiredOnce.Add("INV:" + key)) return;
            Emit(LGLevel.Error, $"{Prefix(cat)} INVARIANT VIOLATED: {msg}");
        }

        private static void Write(LogCat cat, LGLevel level, string msg)
        {
            if (!On(cat, level)) return;

            if (_repeatLimit > 0)
            {
                string key = cat + "|" + level + "|" + msg;
                RepeatCounts.TryGetValue(key, out int count);
                if (count > _repeatLimit) return;
                RepeatCounts[key] = count + 1;
                if (count == _repeatLimit)
                {
                    Emit(level, $"{Prefix(cat)} {msg}  (repeated {_repeatLimit}x - suppressing further occurrences)");
                    return;
                }
            }

            Emit(level, $"{Prefix(cat)} {msg}");
        }

        private static string Prefix(LogCat cat)
        {
            if (!_includeContext) return $"[{cat}]";
            // Host vs client is one character, and it is worth more than it looks: a large share
            // of this mod's defects are "works on the host, silently does nothing on a client".
            char side = 'C';
            var nm = NetworkManager.Singleton;
            if (nm == null) side = '?';
            else if (nm.IsHost || nm.IsServer) side = 'H';
            return $"[{side}][{cat}]";
        }

        private static void Emit(LGLevel level, string line)
        {
            switch (level)
            {
                case LGLevel.Error: Plugin.Logger.LogError(line); break;
                case LGLevel.Warn: Plugin.Logger.LogWarning(line); break;
                default: Plugin.Logger.LogInfo(line); break;
            }
        }

        /// <summary>Clears per-round suppression state so a new round logs afresh.</summary>
        internal static void ResetRound()
        {
            RepeatCounts.Clear();
            FiredOnce.Clear();
        }
    }
}
