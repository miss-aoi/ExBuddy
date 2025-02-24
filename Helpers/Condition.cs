﻿namespace ExBuddy.Helpers
{
    using Clio.Utilities;
    using ExBuddy.Logging;
    using ff14bot.Forms.ugh;
    using ff14bot.Managers;
    using ff14bot.NeoProfiles;
    using Localization;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Data;

    public static class Condition
    {
        public static readonly TimeSpan OneDay = new TimeSpan(1, 0, 0, 0);

        private static readonly string RebornBuddyTitle;

        internal static readonly ConcurrentDictionary<int, ConditionTimer> Timers =
            new ConcurrentDictionary<int, ConditionTimer>();

        static Condition()
        {
            LocalizationInitializer.Initalize();

            SecondaryOffsetManager.IntalizeOffsets();

            AddNamespacesToScriptManager("ExBuddy", "ExBuddy.Helpers");

            RebornBuddyTitle = MainWpf.current.Title;

            GameEvents.OnMapChanged += SetPlayerNameInWindowTitle;

            SetPlayerNameInWindowTitle(null, EventArgs.Empty);
        }

        public static bool All(params object[] param)
        {
            if (param == null || param.Length == 0)
            {
                return false;
            }

            return param.All(IsTrue);
        }

        public static bool Any(params object[] param)
        {
            if (param == null || param.Length == 0)
            {
                return false;
            }

            return param.Any(IsTrue);
        }

        public static float Distance2D(float x, float y, float z)
        {
            return GameObjectManager.LocalPlayer.Location.Distance2D(new Vector3(x, y, z));
        }

        public static float Distance3D(float x, float y, float z)
        {
            return GameObjectManager.LocalPlayer.Location.Distance3D(new Vector3(x, y, z));
        }

        // Is overnight between =)
        public static bool IsTimeBetween(double start, double end)
        {
            if (Math.Abs(start - end) < double.Epsilon)
            {
                return false;
            }

            start = start.Clamp(0, 24);
            end = end.Clamp(0, 24);

            var eorzea = WorldManager.EorzaTime.TimeOfDay;
            var startTimeOffset = TimeSpan.FromHours(start);
            var endTimeOffset = TimeSpan.FromHours(end);

            if (start > end)
            {
                return eorzea.InRange(startTimeOffset, OneDay) || eorzea.InRange(TimeSpan.Zero, endTimeOffset);
            }

            return eorzea.InRange(startTimeOffset, endTimeOffset);
        }

        public static bool IsTrue(this object value)
        {
            var result = string.Concat(value).ConvertToBoolean().GetValueOrDefault();
            return result;
        }

        public static bool IsSpiritBondDone(int id, bool nqOnly = false)
        {
            return InventoryManager.EquippedItems.Any(slot => slot.RawItemId == id && Math.Abs(slot.SpiritBond - 100.0) < 0.01 && (!nqOnly || slot.TrueItemId == id));
        }

        public static bool IsFateActive(int id)
        {
            return FateManager.AllFates.Any(fate => fate.Id == id && fate.Status == ff14bot.Enums.FateStatus.ACTIVE);
        }

        public static int CollectableCount(int id, int collectability)
        {
            return InventoryManager.FilledSlots.Count(slot => slot.RawItemId == id && slot.Collectability >= collectability);
        }

        public static bool TrueFor(int id, TimeSpan span)
        {
            ConditionTimer timer;
            if (Timers.TryGetValue(id, out timer))
            {
                if (timer.TimeSpan != span)
                {
                    timer.Dispose();
                    Timers[id] = new ConditionTimer(id, span);
                    return true;
                }

                return timer.IsValid;
            }

            Timers[id] = new ConditionTimer(id, span);

            return true;
        }

        internal static void AddNamespacesToScriptManager(params string[] param)
        {
            ScriptManager.AddNamespaces(param);
        }

        private static void SetPlayerNameInWindowTitle(object sender, EventArgs eventArgs)
        {
            if (ExBuddySettings.Instance.CharacterNameInWindowTitle)
            {
                MainWpf.current.Dispatcher.Invoke(
                    () => MainWpf.current.Title = string.Concat(RebornBuddyTitle, " - ", GameObjectManager.LocalPlayer.Name));
            }
        }
    }
}
