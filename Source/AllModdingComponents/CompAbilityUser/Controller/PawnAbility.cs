﻿using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;
using Verse.AI;
using System;
using System.Text;
using System.Linq;

namespace AbilityUser
{
    //public class PawnAbility : ThingWithComps
    public class PawnAbility : IExposable, IEquatable<PawnAbility>
    {
        private int TicksUntilCasting = -1;
        private Pawn pawn;
        private AbilityDef powerdef;
        private Texture2D powerButton;
        private Verb_UseAbility verb;
        private List<ThingComp> comps = new List<ThingComp>();

        public CompAbilityUser abilityUser; //public for resolving saving / loading issues between versions
        public virtual CompAbilityUser AbilityUser
        {
            get
            {
                if (abilityUser == null)
                {
                    abilityUser = (CompAbilityUser)pawn.AllComps.FirstOrDefault(x => x is CompAbilityUser);
                }
                return abilityUser;
            }
        }
        public Pawn Pawn { get => pawn; set => pawn = value; }
        public AbilityDef Def { get => powerdef; set => powerdef = value; }
        public List<ThingComp> Comps => comps;
        public Texture2D PowerButton
        {
            get
            {
                if (powerButton == null)
                {
                    powerButton = this.powerdef.uiIcon;
                }
                return powerButton;
            }
        }
        public int CooldownTicksLeft { get => TicksUntilCasting; set { TicksUntilCasting = value; } } //Log.Message(value.ToString()); } }
        public int MaxCastingTicks => (int)(this.powerdef.MainVerb.SecondsToRecharge * GenTicks.TicksPerRealSecond);

        public Verb_UseAbility Verb
        {
            get
            {
                if (verb == null)
                {
                    Verb_UseAbility newVerb = (Verb_UseAbility)Activator.CreateInstance(this.powerdef.MainVerb.verbClass);
                    newVerb.caster = this.AbilityUser.AbilityUser;
                    newVerb.Ability = this;
                    newVerb.verbProps = this.powerdef.MainVerb;
                    verb = newVerb;
                }
                return verb;
            }
        }

        public PawnAbility()
        {
            //Log.Message("PawnAbility Created: "  + this.Def.defName);
        }
        public PawnAbility(CompAbilityUser comp)
        {
            this.pawn = comp.AbilityUser; this.abilityUser = comp;
            //Log.Message("PawnAbility Created: " + this.Def.defName);
        }
        public PawnAbility(AbilityData data)
        {
            this.pawn = data.Pawn; this.abilityUser = data.Pawn.AllComps.FirstOrDefault(x => x.GetType() == data.AbilityClass) as CompAbilityUser;
            //Log.Message("PawnAbility Created: " + this.Def.defName);
        }
        public PawnAbility(Pawn user, AbilityDef pdef)
        {
            this.pawn = user;
            this.powerdef = pdef;
            this.powerButton = pdef.uiIcon;
            //Log.Message("PawnAbility Created: " + this.Def.defName);
        }

        public void Tick()
        {
            if (this.powerdef?.PassiveProps?.Worker is PassiveEffectWorker w)
            {
                w.Tick(this.abilityUser);
            }
            if (this.Verb != null)
            {
                Verb.VerbTick();
            }
            if (this.CooldownTicksLeft > -1 && !Find.TickManager.Paused)
            {
                this.CooldownTicksLeft--;
            }
        }

        public virtual bool ShouldShowGizmo()
        {
            return true;
        }

        public virtual bool CanOverpowerTarget(AbilityContext context, LocalTargetInfo target, out string reason)
        {
            reason = "";
            return true;
        }

        public virtual Job UseAbility(AbilityContext context, LocalTargetInfo target)
        {
            string reason = "";
            if (target.Thing != null && !CanOverpowerTarget(context, target, out reason))
            {
                return null;
            }
            Job job = GetJob(context, target);
            if (context == AbilityContext.Player)
                pawn.jobs.TryTakeOrderedJob(job);
            return job;
        }

        public virtual Job GetJob(AbilityContext context, LocalTargetInfo target)
        {
            Job job;
            job = powerdef.GetJob(verb.UseAbilityProps.AbilityTargetCategory, target);
            job.playerForced = true;
            job.verbToUse = verb;
            job.count = (context == AbilityContext.Player) ? 1 : 0; //Count 1 for Player : 0 for AI
            if (target != null)
            {
                if (target.Thing is Pawn pawn2)
                {
                    job.killIncappedTarget = pawn2.Downed;
                }
            }
            return job;
        }

        public bool TryCastAbility(AbilityContext context, LocalTargetInfo target)
        {
            //Can our body cast?
            string reason = "";
            if (!CanCastPowerCheck(context, out reason))
            {
                //Log.Message("Failed");
                // .Disable(reason.Translate(new object[]
                //{
                //    Verb.CasterPawn.NameStringShort
                //}));
                return false;
            }

            //If we're a player, let's target.
            if (context == AbilityContext.Player)
            {

                Targeter targeter = Find.Targeter;
                if (this.verb.CasterIsPawn && targeter.targetingVerb != null && targeter.targetingVerb.verbProps == this.verb.verbProps)
                {
                    Pawn casterPawn = this.verb.CasterPawn;
                    if (!targeter.IsPawnTargeting(casterPawn))
                    {

                        targeter.targetingVerbAdditionalPawns.Add(casterPawn);
                    }
                }
                UseAbility(AbilityContext.Player, target);
            }
            else
            {
                UseAbility(AbilityContext.AI, target);
            }
            return true;
        }

        public Command_PawnAbility GetGizmo()
        {
            Command_PawnAbility command_CastPower = new Command_PawnAbility(this.abilityUser, this, this.CooldownTicksLeft)
            {
                verb = Verb,
                defaultLabel = this.powerdef.LabelCap,
            };

            command_CastPower.curTicks = this.CooldownTicksLeft;
            
            //GetDesc
            StringBuilder s = new StringBuilder();
            s.AppendLine(this.powerdef.GetDescription());
            s.AppendLine(this.PostAbilityVerbCompDesc(this.Verb.UseAbilityProps));
            command_CastPower.defaultDesc = s.ToString();
            s = null;
            command_CastPower.targetingParams = this.powerdef.MainVerb.targetParams;
            command_CastPower.icon = this.powerdef.uiIcon;
            command_CastPower.action = delegate (Thing target)
            {
                LocalTargetInfo tInfo = GenUI.TargetsAt(UI.MouseMapPosition(), Verb.verbProps.targetParams, false)?.First() ?? target;
                this.TryCastAbility(AbilityContext.Player, tInfo);
            };

            string reason = "";
            if (!this.CanCastPowerCheck(AbilityContext.Player, out reason))
            {
                command_CastPower.Disable(reason);
            }
            return command_CastPower;
        }

        public virtual bool CanCastPowerCheck(AbilityContext context, out string reason)
        {
            reason = "";

            if (context == AbilityContext.Player && Verb.caster.Faction != Faction.OfPlayer)
            {
                reason = "CannotOrderNonControlled".Translate();
                return false;
            }
            if (Verb.CasterPawn.story.WorkTagIsDisabled(WorkTags.Violent) &&
                this.powerdef.MainVerb.isViolent)
            {
                reason = "IsIncapableOfViolence".Translate(new object[]
                {
                    Verb.CasterPawn.NameStringShort
                });
                return false;
            }
            else if (CooldownTicksLeft > 0)
            {
                reason = "AU_PawnAbilityRecharging".Translate(new object[]
                {
                    Verb.CasterPawn.NameStringShort
                });
                return false;
            }
            //else if (!Verb.CasterPawn.drafter.Drafted)
            //{
            //    reason = "IsNotDrafted".Translate(new object[]
            //    {
            //        Verb.CasterPawn.NameStringShort
            //    });
            //}

            return true;
        }


        public virtual void PostAbilityAttempt()
        {
            CooldownTicksLeft = MaxCastingTicks;
        }

        public virtual string PostAbilityVerbCompDesc(VerbProperties_Ability verbDef) => "";

        public virtual string PostAbilityVerbDesc() => "";

        public void ExposeData()
        {
            //base.ExposeData();
            Scribe_Values.Look<int>(ref this.TicksUntilCasting, "TicksUntilcasting", -1);
            Scribe_References.Look<Pawn>(ref this.pawn, "pawn");
            Scribe_Defs.Look<AbilityDef>(ref this.powerdef, "powerdef");
        }


        //Added on 12/3/17 to prevent hash errors.

        public bool Equals(PawnAbility other)
        {
            if (other.GetUniqueLoadID() == this.GetUniqueLoadID()) return true;
            return false;
        }

        public static string GenerateID(Pawn pawn, AbilityDef def)
        {
            return def.defName + "_" + pawn.GetUniqueLoadID();
        }

        public string GetUniqueLoadID()
        {
            return GenerateID(this.Pawn, this.Def);
        }

        public override int GetHashCode()
        {
            int num = 66;
            num = Gen.HashCombineInt(num, (int)this.Def.shortHash);
            if (this.Pawn != null)
            {
                num = Gen.HashCombineInt(num, this.Pawn.thingIDNumber);
            }
            if (this.Verb.AbilityProjectileDef != null)
            {
                num = Gen.HashCombineInt(num, (int)this.Verb.AbilityProjectileDef.shortHash);
            }
            return num;
        }
    }
}
