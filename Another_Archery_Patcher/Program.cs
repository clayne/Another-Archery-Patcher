using System;
using System.Threading.Tasks;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace Another_Archery_Patcher
{
    public static class Program
    {
        private static Lazy<Settings> _lazySettings = null!;
        private static Settings Settings => _lazySettings.Value; // convenience wrapper

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings("Settings", "settings.json", out _lazySettings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "AnotherArcheryPatcher.esp")
                .Run(args)
                .ConfigureAwait(false);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Console.WriteLine("\n--- PATCHER STARTING ---"); // begin

            // Handle Game Settings
            Settings.GameSettings.AddGameSettingsToPatch(state);

            // Handle Projectiles
            var count = 0;

            if (!Settings.AmmoTweaksArrow.ShouldSkip() || !Settings.AmmoTweaksBolt.ShouldSkip())
            {
                Console.WriteLine("Checking Ammunition Records...");
                foreach (var ammo in state.LoadOrder.PriorityOrder.Ammunition().WinningOverrides())
                {
                    if (ammo.EditorID == null || (ammo.Flags & Ammunition.Flag.NonPlayable) != 0)
                        continue; // skip if ammo has invalid editor ID or if ammo has NonPlayable flag.

                    var ammoCopy = ammo.DeepCopy();
                    var changes = 0;
                    if ((ammo.Flags & Ammunition.Flag.NonBolt) == 0) // arrows
                    {
                        (ammoCopy, changes) = Settings.AmmoTweaksArrow.ApplySettingsTo(ammoCopy);
                        if (changes == 0)
                            continue;
                    }
                    else // bolts
                    {
                        (ammoCopy, changes) = Settings.AmmoTweaksBolt.ApplySettingsTo(ammoCopy);
                        if (changes == 0)
                            continue;
                    }
                    // only reach this point when record was changed
                    state.PatchMod.Ammunitions.Set(ammoCopy);
                    Console.WriteLine($"\tModified {changes} value{(changes > 1 ? "s" : "")} in AMMO record {ammo.EditorID}");
                    ++count;
                }
            }
            Console.WriteLine("Checking Projectile Records...");
            foreach (var proj in state.LoadOrder.PriorityOrder.Projectile().WinningOverrides())
            {
                if (!Settings.IsValidPatchTarget(proj))
                    continue;
                Console.WriteLine($"Processing \"{proj.EditorID}\"");

                var projectile = proj.DeepCopy(); // copy proj to temp projectile

                if (projectile == null)
                    continue;

                var changes = 0u;
                string selectedCategoryIdentifier;
                // modify temp projectile
                (projectile, changes, selectedCategoryIdentifier) = Settings.ApplyHighestPriorityStats(projectile);

                if (changes > 0)
                {
                    state.PatchMod.Projectiles.Set(projectile); // set proj to temp projectile
                    Console.WriteLine($"\tModified {changes} values from category \"{selectedCategoryIdentifier}\"\n");
                    ++count;
                }
            }
            Console.WriteLine("--- PATCHER COMPLETE ---");
            Console.WriteLine($"Modified {count} projectile records.\n");
        }
    }
}
