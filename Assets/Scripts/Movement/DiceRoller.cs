using System;
using System.Collections.Generic;

namespace StarBound.Movement
{
    public static class DiceRoller
    {
        public static DiceHand Roll(Random rng)
        {
            var dice = MovementDiceSet.Dice;
            var rolled = new List<RolledDie>(dice.Length);

            for (var i = 0; i < dice.Length; i++)
            {
                var faceValue = rng.Next(1, 7); // 1-6 inclusive
                var terrain = dice[i].FaceAt(faceValue);
                rolled.Add(new RolledDie(i, terrain));
            }

            return new DiceHand(rolled);
        }
    }
}
