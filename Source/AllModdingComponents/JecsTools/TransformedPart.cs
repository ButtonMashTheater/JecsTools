﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace JecsTools
{
    public class Hediff_TransformedPart : Hediff_AddedPart 
    {
        public override bool ShouldRemove
        {
            get
            {
                if (this.TryGetComp<HediffComp_Disappears>() is HediffComp_Disappears hdc_Disappears)
                {
                    return hdc_Disappears.CompShouldRemove;
                }
                return false;
            }
        }

        

        public override string TipStringExtra
        {
            get
            {
                StringBuilder stringBuilder = new StringBuilder();
                if (base.TipStringExtra is string baseString && baseString != "")
                    stringBuilder.Append(baseString);
                if (this.def.comps.FirstOrDefault(x => x is HediffCompProperties_VerbGiver) is HediffCompProperties_VerbGiver props &&
                    props?.tools?.Count() > 0)
                {
                    for (int i = 0; i < props?.tools?.Count(); i++)
                    {
                        stringBuilder.AppendLine("Damage".Translate() + ": " + props.tools[i].power);
                    }
                }
                return stringBuilder.ToString();
            }
        }

        private List<Hediff_MissingPart> temporarilyRemovedParts = new List<Hediff_MissingPart>();

        /// Nothing should happen.
        public override void PostAdd(DamageInfo? dinfo)
        {
            if (base.Part == null)
            {
                Log.Error("Part is null. It should be set before PostAdd for " + this.def + ".");
                return;
            }
            this.pawn.health.RestorePart(base.Part, this, false);
            temporarilyRemovedParts.Clear();
            for (int i = 0; i < base.Part.parts.Count; i++)
            {
                Hediff_MissingPart hediff_MissingPart = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, this.pawn, null);
                hediff_MissingPart.IsFresh = false;
                hediff_MissingPart.lastInjury = null;
                hediff_MissingPart.Part = base.Part.parts[i];
                this.pawn.health.hediffSet.AddDirect(hediff_MissingPart, null);
                temporarilyRemovedParts.Add(hediff_MissingPart);
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            this.pawn.health.RestorePart(base.Part, this, false);
            //for (int i = 0; i < base.Part.parts.Count; i++)
            //{
                
            //}
        }
    }
}
