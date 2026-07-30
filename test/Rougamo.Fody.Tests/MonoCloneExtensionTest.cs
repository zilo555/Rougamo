using Mono.Cecil;
using System.Collections.Generic;
using Xunit;

namespace Rougamo.Fody.Tests;

/// <summary>
/// 验证 <see cref="MonoCloneExtension.Clone(MethodDefinition, string, Dictionary{object, object}, bool)"/>
/// 克隆泛型方法时，返回值、参数与泛型约束中对泛型参数的引用都会重映射到克隆后的泛型参数，
/// 且不会污染原方法。
/// </summary>
public class MonoCloneExtensionTest
{
    [Fact]
    public void Clone_GenericMethod_SignatureUsesClonedGenericParameters()
    {
        var module = ModuleDefinition.CreateModule(nameof(MonoCloneExtensionTest), ModuleKind.Dll);
        var typeDef = new TypeDefinition("Ns", "C", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(typeDef);

        // T M<T, U>(List<T> items, T[] array, ref T byRef) where T : U
        var methodDef = new MethodDefinition("M", MethodAttributes.Public, module.TypeSystem.Void);
        typeDef.Methods.Add(methodDef);
        var gpT = new GenericParameter("T", methodDef);
        var gpU = new GenericParameter("U", methodDef);
        methodDef.GenericParameters.Add(gpT);
        methodDef.GenericParameters.Add(gpU);
        gpT.Constraints.Add(new GenericParameterConstraint(gpU));

        var listOfT = new GenericInstanceType(module.ImportReference(typeof(List<>)));
        listOfT.GenericArguments.Add(gpT);
        methodDef.Parameters.Add(new ParameterDefinition("items", ParameterAttributes.None, listOfT));
        methodDef.Parameters.Add(new ParameterDefinition("array", ParameterAttributes.None, new ArrayType(gpT)));
        methodDef.Parameters.Add(new ParameterDefinition("byRef", ParameterAttributes.None, new ByReferenceType(gpT)));
        methodDef.ReturnType = gpT;

        var clonedMethodDef = methodDef.Clone("M2");

        var clonedT = clonedMethodDef.GenericParameters[0];
        var clonedU = clonedMethodDef.GenericParameters[1];

        // 返回值 T 应指向克隆后的泛型参数
        Assert.Same(clonedT, clonedMethodDef.ReturnType);

        // List<T> 参数的泛型实参应指向克隆后的泛型参数
        var clonedItems = Assert.IsType<GenericInstanceType>(clonedMethodDef.Parameters[0].ParameterType);
        Assert.Same(clonedT, clonedItems.GenericArguments[0]);

        // T[] 参数的元素类型应指向克隆后的泛型参数
        var clonedArray = Assert.IsType<ArrayType>(clonedMethodDef.Parameters[1].ParameterType);
        Assert.Same(clonedT, clonedArray.ElementType);

        // ref T 参数的元素类型应指向克隆后的泛型参数
        var clonedByRef = Assert.IsType<ByReferenceType>(clonedMethodDef.Parameters[2].ParameterType);
        Assert.Same(clonedT, clonedByRef.ElementType);

        // 泛型约束 where T : U 应指向克隆后的泛型参数，且不与原泛型参数共享约束实例
        Assert.NotSame(gpT.Constraints[0], clonedT.Constraints[0]);
        Assert.Same(clonedU, clonedT.Constraints[0].ConstraintType);

        // 原方法不应被克隆过程污染
        Assert.Same(gpT, methodDef.ReturnType);
        Assert.Same(gpU, gpT.Constraints[0].ConstraintType);
        Assert.Same(gpT, ((GenericInstanceType)methodDef.Parameters[0].ParameterType).GenericArguments[0]);
        Assert.Same(gpT, ((ArrayType)methodDef.Parameters[1].ParameterType).ElementType);
    }
}
