using Mutagen.Bethesda;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Another_Archery_Patcher
{
    public class Program
    {
        private static int HandleProjectile(int record_counter, IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IProjectileGetter proj, ProjectileTweaks tweaks, string logMessage = "")
        {
            var projectile = state.PatchMod.Projectiles.GetOrAddAsOverride(proj);
            projectile.Speed = tweaks.stats.speed;
            projectile.Gravity = tweaks.stats.gravity;
            projectile.ImpactForce = tweaks.stats.impactForce;
            projectile.SoundLevel = (uint)tweaks.stats.soundLevel;
            if (projectile.Flags.HasFlag(Projectile.Flag.Supersonic) && Settings.MiscTweaks.disable_supersonic)
                projectile.Flags &= ~Projectile.Flag.Supersonic;
            if (logMessage.Any())
                Console.WriteLine(logMessage);
            return ++record_counter;
        }
        private static int HandleProjectileManual(int record_counter, IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IProjectileGetter proj, ProjectileStats stats, string logMessage = "", string? name_override = null)
        {
            var projectile = state.PatchMod.Projectiles.GetOrAddAsOverride(proj);
            projectile.Speed = stats.speed;
            projectile.Gravity = stats.gravity;
            projectile.ImpactForce = stats.impactForce;
            projectile.SoundLevel = (uint)stats.soundLevel;
            if (projectile.Flags.HasFlag(Projectile.Flag.Supersonic) && Settings.MiscTweaks.disable_supersonic)
                projectile.Flags &= ~Projectile.Flag.Supersonic;
            if (name_override != null)
                projectile.Name = name_override;
            if (logMessage.Any())
                Console.WriteLine(logMessage);
            return ++record_counter;
        }
        private static bool IsValidPatchTarget(IProjectileGetter proj, out string editorID)
        {
            if (proj.EditorID != null) { // Editor ID is valid, check if projectile type is valid & projectile isn't present on any blacklist.
                editorID = proj.EditorID;
                // Return true if: type is Arrow and is not blacklisted OR if the patch_traps option is enabled, type is missile, editor ID contains "trap", and is not blacklisted
                return (proj.Type == Projectile.TypeEnum.Arrow && !Settings.blacklist.IsMatch(editorID)) || (Settings.MiscTweaks.patch_traps && proj.Type == Projectile.TypeEnum.Missile && proj.EditorID.Contains("Trap", StringComparison.OrdinalIgnoreCase) && !Settings.blacklist.IsMatch(editorID));
            }
            editorID = "";
            return false;
        }

        private static Lazy<TopLevelSettings> LazySettings = new();
        private static TopLevelSettings Settings => LazySettings.Value; // convenience wrapper

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings("Settings", "stoplookingforjsonfilesyoudonut.json", out LazySettings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "AnotherArcheryPatcher.esp")
                .Run(args);
        }
        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (Settings._use_verbose_log) {
                Console.WriteLine("--- CONFIGURATION ---");
                Console.WriteLine("Remove Auto-Aim:\t\t" + Settings.GameSettings.disable_autoaim.ToString());
                Console.WriteLine("Fix Ninja Dodge:\t\t" + Settings.GameSettings.disable_npcDodge.ToString());
                Console.WriteLine("Remove Supersonic:\t\t" + Settings.MiscTweaks.disable_supersonic.ToString());
                Console.WriteLine("Patch Trap Projectiles:\t" + Settings.MiscTweaks.patch_traps.ToString());
                Console.WriteLine("Arrow Tweaks Enabled:\t\t" + Settings.ArrowTweaks._enabled.ToString());
                Console.WriteLine("Bolt Tweaks Enabled:\t\t" + Settings.BoltTweaks._enabled.ToString());
                Console.WriteLine("Throwable Tweaks Enabled:\t" + Settings.ThrowableTweaks._enabled.ToString());
                Console.Write("Blacklist:{ ");
                foreach (var id in Settings.blacklist._matchlist)
                    Console.Write('\"' + id.ToString() + "\", ");
                foreach (var id in Settings.blacklist.record)
                    Console.Write('\"' + id.ToString() + "\", ");
                Console.Write("}\n");
            }

            Console.WriteLine("--- BEGIN PATCHER PROCESS ---");
            bool gmst_modified = false;
            if ( Settings == null ) throw new Exception("Settings were null! (How did this happen?)"); // throw early if settings are null
            if (Settings.GameSettings.disable_autoaim) {
                state.PatchMod.GameSettings.Add(new GameSettingFloat(state.PatchMod.GetNextFormKey(), state.PatchMod.SkyrimRelease) { EditorID = "fAutoAimMaxDegrees", Data = 0.0f });          // Add new game setting to patch: "fAutoAimMaxDegrees"
                state.PatchMod.GameSettings.Add(new GameSettingFloat(state.PatchMod.GetNextFormKey(), state.PatchMod.SkyrimRelease) { EditorID = "fAutoAimMaxDistance", Data = 0.0f });         // Add new game setting to patch: "fAutoAimMaxDistance"
                state.PatchMod.GameSettings.Add(new GameSettingFloat(state.PatchMod.GetNextFormKey(), state.PatchMod.SkyrimRelease) { EditorID = "fAutoAimScreenPercentage", Data = 0.0f });    // Add new game setting to patch: "fAutoAimScreenPercentage"
                state.PatchMod.GameSettings.Add(new GameSettingFloat(state.PatchMod.GetNextFormKey(), state.PatchMod.SkyrimRelease) { EditorID = "fAutoAimMaxDegrees3rdPerson", Data = 0.0f }); // Add new game setting to patch: "fAutoAimMaxDegrees3rdPerson"
                Console.WriteLine("Finished removing auto-aim.");
                gmst_modified = true;
            }
            if (Settings.GameSettings.disable_npcDodge) {
                state.PatchMod.GameSettings.Add(new GameSettingFloat(state.PatchMod.GetNextFormKey(), state.PatchMod.SkyrimRelease) { EditorID = "fCombatDodgeChanceMax", Data = 0.0f });       // Add new game setting to patch: "fCombatDodgeChanceMax"
                Console.WriteLine("Finished patching NPC Ninja Dodge bug.");
                gmst_modified = true;
            }
            int count = 0;
            foreach (var proj in state.LoadOrder.PriorityOrder.Projectile().WinningOverrides()) { // iterate through winning projectile overrides (this includes all projectile records added by mods)
                if (IsValidPatchTarget(proj, out string id)) {
                    // Priority 1 - Bloodcursed Arrows
                    if (Settings.MiscTweaks.bloodcursed_id.Contains(id, StringComparer.OrdinalIgnoreCase)) {
                        if (Settings.MiscTweaks.disable_gravity_bloodcursed)
                            count = HandleProjectile(count, state, proj, new(true, Settings.ArrowTweaks.stats.speed, 0.0f, Settings.ArrowTweaks.stats.impactForce, Settings.ArrowTweaks.stats.soundLevel), "Finished processing bloodcursed arrow: \"" + id + "\" (Disabled Gravity)");
                        else
                            count = HandleProjectile(count, state, proj, Settings.ArrowTweaks, "Finished processing arrow: \"" + id + '\"');
                    }
                    // Priority 2 - Trap Projectiles
                    else if (id.Contains("Trap", StringComparison.OrdinalIgnoreCase)) {// handle ballista trap bolts
                        if (id.Contains("TrapDweBallista", StringComparison.OrdinalIgnoreCase))
                            count = HandleProjectileManual(count, state, proj, new(6400.0f, 0.69f, 75.0f, SoundLevel.VeryLoud), "Finished processing trap: \"" + id + '\"', "Ballista Trap Bolt");
                        else
                            count = HandleProjectileManual(count, state, proj, new(3000.0f, 0.0f, 0.2f, SoundLevel.Normal), "Finished processing trap: \"" + id + '\"');
                    }
                    // Priority 3 - Throwable Projectiles
                    else if (Settings.ThrowableTweaks.IsMatch(id))
                        count = HandleProjectile(count, state, proj, Settings.ThrowableTweaks, "Finished processing spear: \"" + id + '\"');
                    // Priority 4 - Arrow Projectiles
                    else if (Settings.ArrowTweaks.IsMatch(id))
                        count = HandleProjectile(count, state, proj, Settings.ArrowTweaks, "Finished processing arrow: \"" + id + '\"');
                    // Priority 5 - Bolt Projectiles
                    else if (Settings.BoltTweaks.IsMatch(id))
                        count = HandleProjectile(count, state, proj, Settings.BoltTweaks, "Finished processing bolt: \"" + id + '\"');
                    else if (Settings._use_verbose_log)
                        Console.WriteLine("Skipping projectile: \"" + id + '\"');
                }
            }
            Console.WriteLine("--- END PATCHER PROCESS ---");
            if (Settings._use_verbose_log) {
                if (count == 0 && !gmst_modified)
                    Console.WriteLine("[WARNING]\tNo records were modified!");
                else if (count > 0)
                    Console.WriteLine("Processed " + count.ToString() + " records.");
            }
        }
    }
}
