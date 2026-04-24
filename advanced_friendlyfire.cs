using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Listeners;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AdvancedFriendlyFire
{
    public class AdvancedFriendlyFireConfig : BasePluginConfig
    {
        [JsonPropertyName("Enable/Disable Advanced Friendly Fire")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("Enable/Disable Punishments")]
        public bool PunishmentsEnabled { get; set; } = true;

        [JsonPropertyName("Enforce Only Configured Damage Inflictors")]
        public bool EnforceDamageSources { get; set; } = true;

        [JsonPropertyName("Punishment Delay (Seconds)")]
        public int PunishmentDelay { get; set; } = 2;

        [JsonPropertyName("Damage Inflictors")]
        public string[] DamageInflictors { get; set; } =
        {
            "inferno",
            "hegrenade_projectile",
            "flashbang_projectile",
            "smokegrenade_projectile",
            "decoy_projectile",
            "planted_c4"
        };

        [JsonPropertyName("Warning #1 Required Team Damage (HP Metrics)")]
        public int Warning1Threshold { get; set; } = 100;

        [JsonPropertyName("Warning #1 Chat message")]
        public string Warning1Message { get; set; } = "Avoid friendly fire! Warning [1/3]";

        [JsonPropertyName("Warning #1 Punishment")]
        public string Warning1Punishment { get; set; } = "css_slay {Player} \"Friendly fire warning [1/3]\"";

        [JsonPropertyName("Warning #2 Required Team Damage (HP Metrics)")]
        public int Warning2Threshold { get; set; } = 200;

        [JsonPropertyName("Warning #2 Chat message")]
        public string Warning2Message { get; set; } = "You have been kicked for excessive team damage!";

        [JsonPropertyName("Warning #2 Punishment")]
        public string Warning2Punishment { get; set; } = "css_kick {Player} \"Friendly fire warning [2/3]\"";

        [JsonPropertyName("Warning #3 Required Team Damage (HP Metrics)")]
        public int Warning3Threshold { get; set; } = 300;

        [JsonPropertyName("Warning #3 Chat message")]
        public string Warning3Message { get; set; } = "You have been banned for excessive team damage!";

        [JsonPropertyName("Warning #3 Punishment")]
        public string Warning3Punishment { get; set; } = "css_ban {Player} 30 \"Friendly fire warning [3/3]\"";
    }

    public class AdvancedFriendlyFire : BasePlugin, IPluginConfig<AdvancedFriendlyFireConfig>
    {
        public override string ModuleName => "Advanced Friendly Fire";
        public override string ModuleVersion => "1.2.0";
        public override string ModuleAuthor => "keno";
        public override string ModuleDescription => "https://steamcommunity.com/id/kenoxyd";

        public required AdvancedFriendlyFireConfig Config { get; set; }
        public void OnConfigParsed(AdvancedFriendlyFireConfig config) => Config = config;

        private readonly Dictionary<ulong, (float Damage, int Level)> playerStats = new();

        private readonly string PREFIX = " \x04 AdvancedFF » \x01";

        public override void Load(bool hotReload)
        {
            RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
            RegisterListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamagePre);

            if (Config.IsEnabled)
            {
                string[] commands =
                {
                    "mp_friendlyfire 1",
                    "ff_damage_reduction_bullets 0.33",
                    "ff_damage_reduction_grenade 0.85",
                    "ff_damage_reduction_grenade_self 1",
                    "ff_damage_reduction_other 0.4"
                };

                foreach (var cmd in commands)
                    Server.ExecuteCommand(cmd);
            }
        }

        private HookResult OnPlayerHurt(EventPlayerHurt e, GameEventInfo info)
        {
            if (!Config.IsEnabled || e?.Attacker == null || e.Userid == null || e.Attacker == e.Userid)
                return HookResult.Continue;

            if (e.Attacker.TeamNum != e.Userid.TeamNum)
                return HookResult.Continue;

            ulong attackerId = e.Attacker.SteamID;
            string attackerName = e.Attacker.PlayerName ?? $"SteamID:{attackerId}";
            float damageTaken = e.DmgHealth;

            if (!playerStats.TryGetValue(attackerId, out var stats))
                stats = (0f, 0);

            float newDamage = stats.Damage + damageTaken;
            int level = stats.Level;

            if (Config.PunishmentsEnabled)
            {
                if (newDamage >= Config.Warning3Threshold && level < 3)
                    ApplyPunishment(e.Attacker, attackerName, Config.Warning3Message, Config.Warning3Punishment, attackerId, 3, true);
                else if (newDamage >= Config.Warning2Threshold && level < 2)
                    ApplyPunishment(e.Attacker, attackerName, Config.Warning2Message, Config.Warning2Punishment, attackerId, 2);
                else if (newDamage >= Config.Warning1Threshold && level < 1)
                    ApplyPunishment(e.Attacker, attackerName, Config.Warning1Message, Config.Warning1Punishment, attackerId, 1);
                else
                    playerStats[attackerId] = (newDamage, level);
            }
            else
            {
                playerStats[attackerId] = (newDamage, level);
            }

            return HookResult.Continue;
        }
        private HookResult OnEntityTakeDamagePre(CEntityInstance victim, CTakeDamageInfo info)
        {
            if (!Config.IsEnabled)
                return HookResult.Continue;

            if (victim?.DesignerName != "player" || info?.Attacker?.Value == null)
                return HookResult.Continue;

            var attackerPawn = new CCSPlayerPawn(info.Attacker.Value.Handle);
            var victimPawn = new CCSPlayerPawn(victim.Handle);

            if (attackerPawn?.Controller.Value == null || victimPawn?.Controller.Value == null)
                return HookResult.Continue;

            var attacker = new CCSPlayerController(attackerPawn.Controller.Value.Handle);
            var victimCtrl = new CCSPlayerController(victimPawn.Controller.Value.Handle);

            if (attacker == null || victimCtrl == null)
                return HookResult.Continue;

            if (attackerPawn.TeamNum != victimPawn.TeamNum || attacker == victimCtrl)
                return HookResult.Continue;

            string inflictorName = info.Inflictor?.Value?.DesignerName ?? "";

            if (Config.EnforceDamageSources && !Config.DamageInflictors.Contains(inflictorName))
                return HookResult.Handled;

            attacker.PrintToCenterAlert("DON'T HURT YOUR TEAMMATES!");

            return HookResult.Continue;
        }

        private void ApplyPunishment(CCSPlayerController attacker, string attackerName, string message, string command, ulong attackerId, int newLevel, bool reset = false)
        {
            attacker.PrintToChat($"{PREFIX}{message}");
            Server.PrintToChatAll($"{PREFIX}\x0A{attackerName} \x01has been punished for Friendly Fire [ {newLevel}/3 ].");

            if (reset)
                playerStats[attackerId] = (0f, 0);
            else
                playerStats[attackerId] = (playerStats[attackerId].Damage, newLevel);

            if (Config.PunishmentDelay > 0)
            {
                AddTimer(Config.PunishmentDelay, () =>
                    Server.ExecuteCommand(command.Replace("{Player}", $"\"{attackerName}\""))
                );
            }
            else
            {
                Server.ExecuteCommand(command.Replace("{Player}", $"\"{attackerName}\""));
            }
        }
    }
}
