﻿using Mono.Cecil.Cil;
using System.Collections.Generic;

namespace Rougamo.Fody.Simulations.PlainValues
{
    internal class Null() : PlainValueSimulation(null!)
    {
        private static readonly TypeSimulation _Type = GlobalRefs.TrObject.Simulate(null!);

        public override TypeSimulation Type => _Type;

        public override IList<Instruction> Load() => [Instruction.Create(OpCodes.Ldnull)];

        public override IList<Instruction> Cast(ILoadable to) => [];
    }
}
