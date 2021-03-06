using System;
using System.Collections.Generic;

using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.WorldObjects.Entity;

namespace ACE.Server.WorldObjects
{
    partial class Creature
    {
        public readonly Dictionary<PropertyAttribute2nd, CreatureVital> Vitals = new Dictionary<PropertyAttribute2nd, CreatureVital>();

        public CreatureVital Health => Vitals[PropertyAttribute2nd.MaxHealth];
        public CreatureVital Stamina => Vitals[PropertyAttribute2nd.MaxStamina];
        public CreatureVital Mana => Vitals[PropertyAttribute2nd.MaxMana];

        public uint GetCurrentCreatureVital(PropertyAttribute2nd vital)
        {
            switch (vital)
            {
                case PropertyAttribute2nd.Mana:
                    return Mana.Current;
                case PropertyAttribute2nd.Stamina:
                    return Stamina.Current;
                default:
                    return Health.Current;
            }
        }

        /// <summary>
        /// Sets the current vital to a new value
        /// </summary>
        /// <returns>The actual change in the vital, after clamping between 0 and MaxVital</returns>
        public virtual int UpdateVital(CreatureVital vital, int newVal)
        {
            var before = vital.Current;
            vital.Current = (uint)Math.Clamp(newVal, 0, vital.MaxValue);
            return (int)(vital.Current - before);
        }

        public virtual int UpdateVital(CreatureVital vital, uint newVal)
        {
            return UpdateVital(vital, (int)newVal);
        }

        /// <summary>
        /// Updates a vital relative to current value
        /// </summary>
        public int UpdateVitalDelta(CreatureVital vital, int delta)
        {
            var newVital = (int)vital.Current + delta;

            return UpdateVital(vital, newVital);
        }

        public int UpdateVitalDelta(CreatureVital vital, uint delta)
        {
            return UpdateVitalDelta(vital, (int)delta);
        }

        /// <summary>
        /// Called every ~5 secs to regenerate vitals
        /// </summary>
        public void VitalHeartBeat()
        {
            if (IsDead)
                return;

            VitalHeartBeat(Health);

            VitalHeartBeat(Stamina);

            VitalHeartBeat(Mana);
        }

        /// <summary>
        /// Updates a particular vital according to regeneration rate
        /// </summary>
        /// <param name="vital">The vital stat to update (health/stamina/mana)</param>
        public void VitalHeartBeat(CreatureVital vital)
        {
            // Current and MaxValue are properties and include overhead in getting their values. We cache them so we only hit the overhead once.
            var vitalCurrent = vital.Current;
            var vitalMax = vital.MaxValue;

            if (vitalCurrent == vitalMax)
                return;

            if (vitalCurrent > vitalMax)
            {
                UpdateVital(vital, vitalMax);
                return;
            }

            if (vital.RegenRate == 0.0) return;

            // take attributes into consideration (strength, endurance)
            var attributeMod = GetAttributeMod(vital);

            // take stance into consideration (combat, crouch, sitting, sleeping)
            var stanceMod = GetStanceMod(vital);

            // take enchantments into consideration:
            // (regeneration / rejuvenation / mana renewal / etc.)
            var enchantmentMod = EnchantmentManager.GetRegenerationMod(vital);

            // cap rate?
            var currentTick = vital.RegenRate * attributeMod * stanceMod * enchantmentMod;

            // add in partially accumulated / rounded vitals from previous tick(s)
            var totalTick = currentTick + vital.PartialRegen;

            // accumulate partial vital rates between ticks
            var intTick = (int)totalTick;
            vital.PartialRegen = totalTick - intTick;

            if (intTick > 0)
            {
                UpdateVitalDelta(vital, intTick);
                if (vital.Vital == PropertyAttribute2nd.MaxHealth)
                    DamageHistory.OnHeal((uint)intTick);
            }
            //Console.WriteLine($"VitalTick({vital.Vital.ToSentence()}): attributeMod={attributeMod}, stanceMod={stanceMod}, enchantmentMod={enchantmentMod}, regenRate={vital.RegenRate}, currentTick={currentTick}, totalTick={totalTick}, accumulated={vital.PartialRegen}");
        }

        /// <summary>
        /// Returns the vital regeneration modifier based on attributes
        /// (strength, endurance for health, stamina)
        /// </summary>
        public float GetAttributeMod(CreatureVital vital)
        {
            // only applies to players
            if ((this as Player) == null) return 1.0f;

            // only applies for health?
            if (vital.Vital != PropertyAttribute2nd.MaxHealth) return 1.0f;

            // The combination of strength and endurance (with endurance being more important) allows one to regenerate hit points 
            // at a faster rate the higher one's endurance is. This bonus is in addition to any regeneration spells one may have placed upon themselves.
            // This regeneration bonus caps at around 110%.

            var strength = Strength.Base;
            var endurance = Endurance.Base;

            var strAndEnd = strength + (endurance * 2);

            var modifier = 1.0 + (0.0494 * Math.Pow(strAndEnd, 1.179) / 100.0f);    // formula deduced from values present in the client pdb
            var attributeMod = Math.Clamp(modifier, 1.0, 2.1);      // cap between + 0-110%

            return (float)attributeMod;
        }

        /// <summary>
        /// Returns the vital regeneration modifier based on player stance
        /// (combat, crouch, sitting, sleeping)
        /// </summary>
        public float GetStanceMod(CreatureVital vital)
        {
            // only applies to players
            if ((this as Player) == null) return 1.0f;

            // does not apply for mana?
            if (vital.Vital == PropertyAttribute2nd.MaxMana) return 1.0f;

            // combat mode / running
            if (CombatMode != CombatMode.NonCombat || CurrentMotionCommand == MotionCommand.RunForward)
                return 0.5f;

            switch (CurrentMotionCommand)
            {
                // TODO: verify multipliers
                default:
                    return 1.0f;
                case MotionCommand.Crouch:
                    return 2.0f;
                case MotionCommand.Sitting:
                    return 2.5f;
                case MotionCommand.Sleeping:
                    return 3.0f;
            }
        }
    }
}
