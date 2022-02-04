using BepInEx;
using BepInEx.Configuration;
using BepInEx.IL2CPP;
using Hazel;
using System;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnhollowerBaseLib;
using TownOfHost;

namespace TownOfHost
{
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.StartGame))]
    class changeRoleSettings
    {
        public static void Postfix(AmongUsClient __instance)
        {//注:この時点では役職は設定されていません。
            main.currentWinner = CustomWinner.Default;
            main.CustomWinTrigger = false;
            main.OptionControllerIsEnable = false;
            main.BitPlayers = new Dictionary<byte, (byte, float)>();
            main.BountyTargetPlayer = new List<PlayerControl>();
            main.WarlockTarget = new List<PlayerControl>();
            main.BountyCheck = false;
            main.WarlockCheck = false;
            main.UsedButtonCount = 0;
            main.SabotageMasterUsedSkillCount = 0;
            //TODO:LoversWinTrigger
            if (__instance.AmHost)
            {

                main.VisibleTasksCount = true;

                main.SyncCustomSettingsRPC();
                var opt = PlayerControl.GameOptions;
                if (main.MadmateCount> 0 || main.TerroristCount > 0)
                {//無限ベント
                    opt.RoleOptions.EngineerCooldown = 0.2f;
                    opt.RoleOptions.EngineerInVentMaxTime = float.PositiveInfinity;
                }
                if (main.isFixedCooldown)
                {
                    main.BeforeFixCooldown = opt.KillCooldown;
                    opt.KillCooldown = main.BeforeFixCooldown * 2;
                }

                if(main.SyncButtonMode) main.BeforeFixMeetingCooldown = PlayerControl.GameOptions.EmergencyCooldown;

                if(main.IsHideAndSeek) {
                    main.HideAndSeekKillDelayTimer = main.HideAndSeekKillDelay;
                    main.HideAndSeekImpVisionMin = opt.ImpostorLightMod;
                    opt.ImpostorLightMod = 0f;
                    Logger.SendToFile("HideAndSeekImpVisionMinを" + main.HideAndSeekImpVisionMin + "に変更");
                }

                PlayerControl.LocalPlayer.RpcSyncSettings(opt);
            }
        }
    }
    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    class SelectRolesPatch {
        public static void Prefix(RoleManager __instance) {
            if(!main.IsHideAndSeek) {
                //役職の人数を指定
                RoleOptionsData roleOpt = PlayerControl.GameOptions.RoleOptions;
                int EngineerNum = roleOpt.GetNumPerGame(RoleTypes.Engineer);
                EngineerNum += main.MadmateCount + main.TerroristCount;
                roleOpt.SetRoleRate(RoleTypes.Engineer, EngineerNum, 100);

                int ShapeshifterNum = roleOpt.GetNumPerGame(RoleTypes.Shapeshifter);
                ShapeshifterNum += main.MafiaCount + main.WarlockCount;
                roleOpt.SetRoleRate(RoleTypes.Shapeshifter, ShapeshifterNum, 100);
            }
        }
        public static void Postfix(RoleManager __instance) {
            if(!AmongUsClient.Instance.AmHost) return;
            main.ApplySuffix();
            main.AllPlayerCustomRoles = new Dictionary<byte, CustomRoles>();
            main.AllPlayerCustomSubRoles = new Dictionary<byte, CustomSubRoles>();
            main.RealNames = new Dictionary<byte, string>();
            main.tmpNames = new Dictionary<byte, string>();

            foreach(var pc in PlayerControl.AllPlayerControls)
            {
                main.RealNames[pc.PlayerId] = pc.name;
                main.tmpNames[pc.PlayerId] = pc.name;
            }

            if(main.IsHideAndSeek) {
                var rand = new System.Random();
                SetColorPatch.IsAntiGlitchDisabled = true;

                //Hide And Seek時の処理
                List<PlayerControl> Impostors = new List<PlayerControl>();
                List<PlayerControl> Crewmates = new List<PlayerControl>();
                //リスト作成兼色設定処理
                foreach(var pc in PlayerControl.AllPlayerControls) {
                    main.AllPlayerCustomRoles.Add(pc.PlayerId,CustomRoles.Default);
                    if(pc.Data.Role.IsImpostor) {
                        Impostors.Add(pc);
                        pc.RpcSetColor(0);
                    } else {
                        Crewmates.Add(pc);
                        pc.RpcSetColor(1);
                    }
                    if(main.IgnoreCosmetics) {
                        pc.RpcSetHat("");
                        pc.RpcSetSkin("");
                    }
                }
                //FoxCountとTrollCountを適切に修正する
                int FixedFoxCount = Math.Clamp(main.FoxCount,0,Crewmates.Count);
                int FixedTrollCount = Math.Clamp(main.TrollCount,0,Crewmates.Count - FixedFoxCount);
                List<PlayerControl> FoxList = new List<PlayerControl>();
                List<PlayerControl> TrollList = new List<PlayerControl>();
                //役職設定処理
                for(var i = 0; i < FixedFoxCount; i++) {
                    var id = rand.Next(Crewmates.Count);
                    FoxList.Add(Crewmates[id]);
                    main.AllPlayerCustomRoles[Crewmates[id].PlayerId] = CustomRoles.Fox;
                    Crewmates[id].RpcSetColor(3);
                    Crewmates[id].RpcSetCustomRole(CustomRoles.Fox);
                    Crewmates.RemoveAt(id);
                }
                for(var i = 0; i < FixedTrollCount; i++) {
                    var id = rand.Next(Crewmates.Count);
                    TrollList.Add(Crewmates[id]);
                    main.AllPlayerCustomRoles[Crewmates[id].PlayerId] = CustomRoles.Troll;
                    Crewmates[id].RpcSetColor(2);
                    Crewmates[id].RpcSetCustomRole(CustomRoles.Troll);
                    Crewmates.RemoveAt(id);
                }
                //通常クルー・インポスター用RPC
                foreach(var pc in Crewmates) pc.RpcSetCustomRole(CustomRoles.Default);
                foreach(var pc in Impostors) pc.RpcSetCustomRole(CustomRoles.Default);
            } else {
                List<PlayerControl> Crewmates = new List<PlayerControl>();
                List<PlayerControl> Impostors = new List<PlayerControl>();
                List<PlayerControl> Scientists = new List<PlayerControl>();
                List<PlayerControl> Engineers = new List<PlayerControl>();
                List<PlayerControl> GuardianAngels = new List<PlayerControl>();
                List<PlayerControl> Shapeshifters = new List<PlayerControl>();
                foreach(var pc in PlayerControl.AllPlayerControls) {
                    if(main.AllPlayerCustomRoles.ContainsKey(pc.PlayerId)) continue;
                    switch(pc.Data.Role.Role) {
                        case RoleTypes.Crewmate:
                            Crewmates.Add(pc);
                            main.AllPlayerCustomRoles.Add(pc.PlayerId, CustomRoles.Default);
                            break;
                        case RoleTypes.Impostor:
                            Impostors.Add(pc);
                            main.AllPlayerCustomRoles.Add(pc.PlayerId, CustomRoles.Impostor);
                            break;
                        case RoleTypes.Scientist:
                            Scientists.Add(pc);
                            main.AllPlayerCustomRoles.Add(pc.PlayerId, CustomRoles.Scientist);
                            break;
                        case RoleTypes.Engineer:
                            Engineers.Add(pc);
                            main.AllPlayerCustomRoles.Add(pc.PlayerId, CustomRoles.Engineer);
                            break;
                        case RoleTypes.GuardianAngel:
                            GuardianAngels.Add(pc);
                            main.AllPlayerCustomRoles.Add(pc.PlayerId, CustomRoles.GuardianAngel);
                            break;
                        case RoleTypes.Shapeshifter:
                            Shapeshifters.Add(pc);
                            main.AllPlayerCustomRoles.Add(pc.PlayerId, CustomRoles.Shapeshifter);
                            break;
                        default:
                            Logger.SendInGame("エラー:役職設定中に無効な役職のプレイヤーを発見しました(" + pc.name + ")");
                            break;
                    }
                }

                AssignCustomRolesFromList(CustomRoles.Jester, Crewmates);
                AssignCustomRolesFromList(CustomRoles.Madmate, Engineers);
                AssignCustomRolesFromList(CustomRoles.Bait, Crewmates);
                AssignCustomRolesFromList(CustomRoles.MadGuardian, Crewmates);
                AssignCustomRolesFromList(CustomRoles.Mayor, Crewmates);
                AssignCustomRolesFromList(CustomRoles.Opportunist, Crewmates);
                AssignCustomRolesFromList(CustomRoles.Snitch, Crewmates);
                AssignCustomRolesFromList(CustomRoles.SabotageMaster, Crewmates);
                AssignCustomRolesFromList(CustomRoles.Mafia, Shapeshifters);
                AssignCustomRolesFromList(CustomRoles.Terrorist, Engineers);
                AssignCustomRolesFromList(CustomRoles.Vampire, Impostors);
                AssignCustomRolesFromList(CustomRoles.BountyHunter, Impostors);
                AssignCustomRolesFromList(CustomRoles.Warlock, Shapeshifters);
                AssignCustomSubRolesFromList(CustomSubRoles.Lovers);

                //RPCによる同期
                foreach(var pair in main.AllPlayerCustomRoles) {
                    ExtendedPlayerControl.RpcSetCustomRole(pair.Key, pair.Value);
                }

                main.NotifyRoles();

                //役職の人数を戻す
                RoleOptionsData roleOpt = PlayerControl.GameOptions.RoleOptions;
                int EngineerNum = roleOpt.GetNumPerGame(RoleTypes.Engineer);
                EngineerNum -= main.MadmateCount + main.TerroristCount;
                roleOpt.SetRoleRate(RoleTypes.Engineer, EngineerNum, roleOpt.GetChancePerGame(RoleTypes.Engineer));

                int ShapeshifterNum = roleOpt.GetNumPerGame(RoleTypes.Shapeshifter);
                ShapeshifterNum -= main.MafiaCount + main.WarlockCount;
                roleOpt.SetRoleRate(RoleTypes.Shapeshifter, ShapeshifterNum, roleOpt.GetChancePerGame(RoleTypes.Shapeshifter));
            }
            SetColorPatch.IsAntiGlitchDisabled = false;
        }
        private static void AssignCustomRolesFromList(CustomRoles role,List<PlayerControl> players, int RawCount = -1) {
            if(players.Count <= 0) return;
            var rand = new System.Random();
            var count = Math.Clamp(RawCount, 0, players.Count);
            if(RawCount == -1) count = Math.Clamp(main.GetCountFromRole(role), 0, players.Count);
            if(count <= 0) return;
            for(var i = 0; i < count; i++) {
                var player = players[rand.Next(0, players.Count - 1)];
                players.Remove(player);
                main.AllPlayerCustomRoles[player.PlayerId] = role;
                Logger.info("役職設定:" + player.name + " = " + role.ToString());
            }
        }

        private static void AssignCustomSubRolesFromList(CustomSubRoles subRoles , int RawCount = -1) {
            if(main.isLovers) {
                main.LoversPlayers.Clear();//TODO:使わなくなるかもしれないが一旦置き
                
                var rand = new System.Random();
                var count = 2;

                for(var i = 0; i < count; i++) {
                    var player = PlayerControl.AllPlayerControls[rand.Next(0, PlayerControl.AllPlayerControls.Count - 1)];
                
                    main.AllPlayerCustomSubRoles[player.PlayerId] = subRoles;
                    main.LoversPlayers.Add(player);
                    Logger.info("サブ役職設定:" + player.name + " = " + subRoles.ToString());
                }
            }
        }
    }
}
