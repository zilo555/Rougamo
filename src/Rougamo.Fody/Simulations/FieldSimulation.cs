﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;

namespace Rougamo.Fody.Simulations
{
    internal class FieldSimulation(TypeSimulation declaringType, FieldDefinition fieldDef) : Simulation(declaringType.Module), IHost, IAssignable
    {
        protected readonly TypeSimulation _declaringType = declaringType;
        private TypeSimulation? _value;

        public FieldDefinition FieldDef { get; } = fieldDef;

        public FieldReference FieldRef { get; } = new FieldReference(fieldDef.Name, fieldDef.FieldType, declaringType);

        public TypeReference TypeRef => FieldRef.FieldType;

        public TypeSimulation Value => _value ??= FieldRef.FieldType.Simulate(this, Module);

        public IList<Instruction> LoadForCallingMethod()
        {
            return [.. _declaringType.LoadForCallingMethod(), FieldRef.LdfldAny()];
        }

        public IList<Instruction> PrepareLoadAddress(MethodSimulation method) => [];

        public IList<Instruction> LoadAddress(MethodSimulation method)
        {
            return [.. _declaringType.Load(), FieldRef.Ldflda()];
        }

        public IList<Instruction> Load()
        {
            return [.. _declaringType.Load(), FieldRef.Ldfld()];
        }

        public IList<Instruction> Assign(Func<IAssignable, IList<Instruction>> valueFactory)
        {
            return [.. _declaringType.Load(), .. valueFactory(this), FieldRef.Stfld()];
        }

        public IList<Instruction> AssignNew(TypeSimulation type, params IParameterSimulation?[] arguments)
        {
            if (FieldRef.FieldType.IsValueType)
            {
                return [.. _declaringType.Load(), FieldRef.Ldflda(), .. type.New(arguments)];
            }
            return Assign(target => type.New(arguments));
        }

        public IList<Instruction> AssignDefault(TypeSimulation type)
        {
            var filedTypeRef = FieldRef.FieldType;
            if (filedTypeRef.IsValueType || filedTypeRef.IsGenericParameter)
            {
                return [.. _declaringType.Load(), FieldRef.Ldflda(), .. type.Default()];
            }
            return [.. _declaringType.Load(), .. type.Default(), FieldRef.Ldfld()];
        }

        public static implicit operator FieldReference(FieldSimulation value) => value.FieldRef;
    }

    internal class FieldSimulation<T>(TypeSimulation declaringType, FieldDefinition fieldDef) : FieldSimulation(declaringType, fieldDef) where T : TypeSimulation
    {
        private T? _value;

        public new T Value => _value ??= FieldRef.FieldType.Simulate<T>(this, Module);

        public IList<Instruction> AssignNew(params IParameterSimulation?[] arguments)
        {
            return AssignNew(Value, arguments);
        }
    }

    internal static class FieldSimulationExtensions
    {
        public static FieldSimulation Simulate(this FieldDefinition fieldDef, TypeSimulation declaringType)
        {
            return new FieldSimulation(declaringType, fieldDef);
        }

        public static FieldSimulation<T> Simulate<T>(this FieldDefinition fieldDef, TypeSimulation declaringType) where T : TypeSimulation
        {
            return new FieldSimulation<T>(declaringType, fieldDef);
        }

        public static IList<Instruction> AssignDefault(this FieldSimulation field) => field.AssignDefault(field.Value);

        public static IList<Instruction> AssignDefault<T>(this FieldSimulation<T> field) where T : TypeSimulation => field.AssignDefault(field.Value);
    }
}
