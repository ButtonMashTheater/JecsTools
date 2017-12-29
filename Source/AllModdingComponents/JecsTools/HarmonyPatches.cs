﻿using System;
using System.Collections.Generic;
using System.Linq;
using AbilityUser;
using Harmony;
using RimWorld;
using UnityEngine;
using Verse;

namespace JecsTools
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        // Verse.Pawn_HealthTracker
        public static bool StopPreApplyDamageCheck;

        static HarmonyPatches()
        {
            var harmony = HarmonyInstance.Create("rimworld.jecrell.jecstools.main");
            //Allow fortitude to soak damage
            harmony.Patch(AccessTools.Method(typeof(Pawn_HealthTracker), "PreApplyDamage"),
                new HarmonyMethod(typeof(HarmonyPatches), nameof(PreApplyDamage_PrePatch)), null);
        }

        
        
        public static bool PreApplyDamage_PrePatch(Pawn_HealthTracker __instance, DamageInfo dinfo, out bool absorbed)
        {
            var pawn = (Pawn) AccessTools.Field(typeof(Pawn_HealthTracker), "pawn").GetValue(__instance);
            if (pawn != null && !StopPreApplyDamageCheck)
                if (pawn?.health?.hediffSet?.hediffs != null && pawn?.health?.hediffSet?.hediffs?.Count > 0)
                {
                    //A list will stack.
                    var fortitudeHediffs =
                        pawn?.health?.hediffSet?.hediffs?.FindAll(x => x.TryGetComp<HediffComp_DamageSoak>() != null);
                    if (!fortitudeHediffs.NullOrEmpty())
                    {
                        try
                        {
                            if (PreApplyDamage_ApplyDamageSoakers(dinfo, out absorbed, fortitudeHediffs, pawn))
                                return true;
                        }
                        catch (NullReferenceException e)
                        {
                            
                        }
                    }
                    if (dinfo.Weapon is ThingDef weaponDef && !weaponDef.IsRangedWeapon)
                        if (dinfo.Instigator is Pawn instigator)
                        {
                            try
                            {
                                if (PreApplyDamage_ApplyExtraDamages(out absorbed, instigator, pawn)) return true;
                            }
                            catch (NullReferenceException e)
                            {
                                
                            }

                            try
                            {
                                PreApplyDamage_ApplyKnockback(instigator, pawn);
                            }
                            catch (NullReferenceException e)
                            {
                                
                            }
                        }
                }
            absorbed = false;
            return true;
        }

        private static void PreApplyDamage_ApplyKnockback(Pawn instigator, Pawn pawn)
        {
            var knockbackHediff =
                instigator?.health?.hediffSet?.hediffs.FirstOrDefault(y =>
                    y.TryGetComp<HediffComp_Knockback>() != null);
            var knocker = knockbackHediff?.TryGetComp<HediffComp_Knockback>();
            if (knocker != null)
                if (knocker?.Props?.knockbackChance >= Rand.Value)
                {
                    if (knocker.Props.explosiveKnockback)
                    {
                        var explosion = (Explosion) GenSpawn.Spawn(ThingDefOf.Explosion,
                            instigator.PositionHeld, instigator.MapHeld);
                        explosion.radius = knocker.Props.explosionSize;
                        explosion.damType = knocker.Props.explosionDmg;
                        explosion.instigator = instigator;
                        explosion.damAmount = 0;
                        explosion.weapon = null;
                        explosion.projectile = null;
                        explosion.preExplosionSpawnThingDef = null;
                        explosion.preExplosionSpawnChance = 0f;
                        explosion.preExplosionSpawnThingCount = 1;
                        explosion.postExplosionSpawnThingDef = null;
                        explosion.postExplosionSpawnChance = 0f;
                        explosion.postExplosionSpawnThingCount = 1;
                        explosion.applyDamageToExplosionCellsNeighbors = false;
                        explosion.chanceToStartFire = 0f;
                        explosion.dealMoreDamageAtCenter = false;
                        explosion.StartExplosion(null);
                    }
                    if (pawn != instigator && !pawn.Dead && !pawn.Downed && pawn.Spawned)
                    {
                        if (knocker.Props.stunChance > -1 && knocker.Props.stunChance >= Rand.Value)
                            pawn.stances.stunner.StunFor(knocker.Props.stunTicks);
                        PushEffect(instigator, pawn, knocker.Props.knockDistance.RandomInRange,
                            true);
                    }
                }
        }

        private static bool PreApplyDamage_ApplyExtraDamages(out bool absorbed, Pawn instigator, Pawn pawn)
        {
            var extraDamagesHediff =
                instigator.health.hediffSet.hediffs.FirstOrDefault(y =>
                    y.TryGetComp<HediffComp_ExtraMeleeDamages>() != null);
            var damages = extraDamagesHediff?.TryGetComp<HediffComp_ExtraMeleeDamages>();
            if (damages?.Props != null && !damages.Props.extraDamages.NullOrEmpty())
            {
                StopPreApplyDamageCheck = true;
                foreach (var dmg in damages.Props.extraDamages)
                {
                    if (pawn == null || !pawn.Spawned || pawn.Dead)
                    {
                        absorbed = false;
                        StopPreApplyDamageCheck = false;
                        return true;
                    }
                    pawn.TakeDamage(new DamageInfo(dmg.def, dmg.amount, -1, instigator));
                }
                StopPreApplyDamageCheck = false;
            }
            absorbed = false;
            return false;
        }

        private static bool PreApplyDamage_ApplyDamageSoakers(DamageInfo dinfo, out bool absorbed, List<Hediff> fortitudeHediffs,
            Pawn pawn)
        {
            var soakedDamage = 0;
            foreach (var fortitudeHediff in fortitudeHediffs)
            {
                var soaker = fortitudeHediff.TryGetComp<HediffComp_DamageSoak>();
                if (soaker?.Props != null && (soaker?.Props?.damageType == null || soaker?.Props?.damageType == dinfo.Def))
                {
                    if (!soaker.Props.damageTypesToExclude.NullOrEmpty() &&
                        soaker.Props.damageTypesToExclude.Contains(dinfo.Def))
                        continue;
                    var dmgAmount = Mathf.Max(dinfo.Amount - soaker.Props.damageToSoak, 0);
                    dinfo.SetAmount(dmgAmount);
                    soakedDamage += dmgAmount;
                    if (dinfo.Amount > 0) continue;
                    absorbed = true;
                    return true;
                }
            }
            if (soakedDamage != 0 && pawn.Spawned && pawn.MapHeld != null && pawn.DrawPos is Vector3 drawVec &&
                drawVec.InBounds(pawn.MapHeld))
                MoteMaker.ThrowText(drawVec, pawn.MapHeld,
                    "JT_DamageSoaked".Translate(soakedDamage), -1f);
            absorbed = false;
            return false;
        }

        public static Vector3 PushResult(Thing Caster, Thing thingToPush, int pushDist, out bool collision)
        {
            var origin = thingToPush.TrueCenter();
            var result = origin;
            var collisionResult = false;
            for (var i = 1; i <= pushDist; i++)
            {
                var pushDistX = i;
                var pushDistZ = i;
                if (origin.x < Caster.TrueCenter().x) pushDistX = -pushDistX;
                if (origin.z < Caster.TrueCenter().z) pushDistZ = -pushDistZ;
                var tempNewLoc = new Vector3(origin.x + pushDistX, 0f, origin.z + pushDistZ);
                if (tempNewLoc.ToIntVec3().Standable(Caster.Map))
                {
                    result = tempNewLoc;
                }
                else
                {
                    if (thingToPush is Pawn)
                    {
                        //target.TakeDamage(new DamageInfo(DamageDefOf.Blunt, Rand.Range(3, 6), -1, null, null, null));
                        collisionResult = true;
                        break;
                    }
                }
            }
            collision = collisionResult;
            return result;
        }

        public static void PushEffect(Thing Caster, Thing target, int distance, bool damageOnCollision = false)
        {
            LongEventHandler.QueueLongEvent(delegate
            {
                if (target != null && target is Pawn p && p.Spawned && !p.Downed && !p.Dead && p?.MapHeld != null)
                {
                    bool applyDamage;
                    var loc = PushResult(Caster, target, distance, out applyDamage);
                    //if (((Pawn)target).RaceProps.Humanlike) ((Pawn)target).needs.mood.thoughts.memories.TryGainMemory(ThoughtDef.Named("PJ_ThoughtPush"), null);
                    var flyingObject = (FlyingObject) GenSpawn.Spawn(ThingDef.Named("JT_FlyingObject"), p.PositionHeld,
                        p.MapHeld);
                    if (applyDamage && damageOnCollision)
                        flyingObject.Launch(Caster, new LocalTargetInfo(loc.ToIntVec3()), target,
                            new DamageInfo(DamageDefOf.Blunt, Rand.Range(8, 10)));
                    else flyingObject.Launch(Caster, new LocalTargetInfo(loc.ToIntVec3()), target);
                }
            }, "PushingCharacter", false, null);
        }
    }
}