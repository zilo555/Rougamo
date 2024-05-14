﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Rougamo.Fody
{
    partial class ModuleWeaver
    {
        private void FindRous()
        {
            _rouTypes = new List<RouType>();
            FullScan();
            ExtractTypeReferences();
        }

        private void ExpandTypes(Collection<TypeDefinition> topLevelTypes, List<TypeDefinition> expandedTypes)
        {
            foreach (var type in topLevelTypes)
            {
                expandedTypes.Add(type);
                ExpandTypes(type.NestedTypes, expandedTypes);
            }
        }

        private void FullScan()
        {
            var globalMos = FindGlobalAttributes();

            if (globalMos.GlobalIgnore) return;

            var types = new List<TypeDefinition>();
            ExpandTypes(ModuleDefinition.Types, types);
            foreach (var typeDef in types)
            {
                if (typeDef.IsEnum || typeDef.IsInterface || typeDef.IsArray || typeDef.IsDelegate() || !typeDef.HasMethods || typeDef.CustomAttributes.Any(x => x.AttributeType.Is(Constants.TYPE_CompilerGeneratedAttribute) || x.AttributeType.Is(Constants.TYPE_Runtime_CompilerGeneratedAttribute))) continue;
                if (typeDef.Implement(Constants.TYPE_IMo) || typeDef.DerivesFromAny(Constants.TYPE_MoRepulsion, Constants.TYPE_IgnoreMoAttribute, Constants.TYPE_MoProxyAttribute)) continue;
                if (_config.ExceptTypePatterns.Any(x => x.IsMatch(typeDef.FullName))) continue;

                var typeIgnores = ExtractIgnores(typeDef.CustomAttributes);
                if (typeIgnores == null) continue;

                var rouType = new RouType(typeDef);
                var implementations = ExtractClassImplementations(typeDef);
                var classExtracts = ExtractAttributes(typeDef.CustomAttributes, globalMos.Proxies!);

                foreach (var methodDef in typeDef.Methods)
                {
                    if ((methodDef.Attributes & MethodAttributes.Abstract) != 0) continue;

                    if (!methodDef.IsGetter && !methodDef.IsSetter && methodDef.CustomAttributes.Any(x => x.AttributeType.Is(Constants.TYPE_CompilerGeneratedAttribute) || x.AttributeType.Is(Constants.TYPE_Runtime_CompilerGeneratedAttribute))) continue;

                    var attributes = new Collection<CustomAttribute>();
                    var property = typeDef.Properties.SingleOrDefault(x => x.SetMethod == methodDef || x.GetMethod == methodDef);
                    if (property != null)
                    {
                        attributes.Add(property.CustomAttributes);
                    }
                    attributes.Add(methodDef.CustomAttributes);

                    var methodIgnores = ExtractIgnores(attributes);
                    if (methodIgnores == null) continue;

                    var methodExtracts = ExtractAttributes(attributes, globalMos.Proxies!);
                    rouType.Initialize(methodDef, globalMos.Directs!, globalMos.Generics, implementations, classExtracts.Mos, classExtracts.GenericMos, classExtracts.Proxied, methodExtracts.Mos, methodExtracts.GenericMos, methodExtracts.Proxied, globalMos.Ignores!, typeIgnores, methodIgnores, _config.CompositeAccessibility);
                }
                if (rouType.HasMo)
                {
                    _rouTypes.Add(rouType);
                }
            }
        }

        /// <summary>
        /// extract common usage TypeReferences
        /// </summary>
        private void ExtractTypeReferences()
        {
            if(_rouTypes.Count > 0)
            {
                var sampleMo = _rouTypes.First().Methods.First().Mos.First();
                var imoTypeDef = sampleMo.MoTypeDef.GetInterfaceDefinition(Constants.TYPE_IMo);
                _methodIMosRef = new Dictionary<string, MethodReference>(4);
                foreach (var methodDef in imoTypeDef!.Methods)
                {
                    if(methodDef.Name == Constants.METHOD_OnEntry ||
                        methodDef.Name == Constants.METHOD_OnSuccess ||
                        methodDef.Name == Constants.METHOD_OnException ||
                        methodDef.Name == Constants.METHOD_OnExit)
                    {
                        _methodIMosRef.Add(methodDef.Name, _importer.Import(methodDef));
                        if (_typeMethodContextRef == null)
                        {
                            _typeMethodContextRef = _importer.Import(methodDef.Parameters.First().ParameterType);
                        }
                    }
                }
                _typeIMoRef = _importer.Import(imoTypeDef);
                _typeIMoArrayRef = new ArrayType(_typeIMoRef);
                var typeMethodContextDef = _typeMethodContextRef.Resolve();
                _methodMethodContextCtorRef = _importer.Import(typeMethodContextDef.GetConstructors().First(x => x.Parameters.Count == 8));
                _methodMethodContextSetExceptionRef = _importer.Import(typeMethodContextDef.GetPropertySetterDef(Constants.PROP_Exception));
                _methodMethodContextSetReturnValueRef = _importer.Import(typeMethodContextDef.GetPropertySetterDef(Constants.PROP_ReturnValue));
                _methodMethodContextGetReturnValueRef = _importer.Import(typeMethodContextDef.GetPropertyGetterDef(Constants.PROP_ReturnValue));
                _methodMethodContextGetExceptionHandledRef = _importer.Import(typeMethodContextDef.GetPropertyGetterDef(Constants.PROP_ExceptionHandled));
                _methodMethodContextGetReturnValueReplacedRef = _importer.Import(typeMethodContextDef.GetPropertyGetterDef(Constants.PROP_ReturnValueReplaced));
                _methodMethodContextGetArgumentsRef = _importer.Import(typeMethodContextDef.GetPropertyGetterDef(Constants.PROP_Arguments));
                _methodMethodContextGetRewriteArgumentsRef = _importer.Import(typeMethodContextDef.GetPropertyGetterDef(Constants.PROP_RewriteArguments));
                _methodMethodContextGetRetryCountRef = _importer.Import(typeMethodContextDef.GetPropertyGetterDef(Constants.PROP_RetryCount));
                // Private fields cannot be accessed externally even using IL
                //_fieldMethodContextExceptionRef = typeMethodContextDef.Fields.Single(x => x.Name == Constants.FIELD_Exception).ImportInto(ModuleDefinition);
                //_fieldMethodContextReturnValueRef = typeMethodContextDef.Fields.Single(x => x.Name == Constants.FIELD_ReturnValue).ImportInto(ModuleDefinition);
                _methodMethodContextGetHasExceptionRef = _importer.Import(typeMethodContextDef.GetPropertyGetterDef(Constants.PROP_HasException));
            }
        }

        /// <summary>
        /// 查找程序集级别继承自MoAttribute以及使用MoProxyAttribute代理的Attribute，module级别会覆盖assembly级别
        /// </summary>
        /// <returns>
        /// directs: 继承自MoAttribute的类型
        /// proxies: 通过MoProxyAttribute代理的类型
        /// ignores: 需要忽略的实现了IMo的织入类型
        /// </returns>
        private SimplifyGlobalMos FindGlobalAttributes()
        {
            var assemblyMos = FindGlobalAttributes(ModuleDefinition.Assembly.CustomAttributes, "assembly");
            var moduleMos = FindGlobalAttributes(ModuleDefinition.CustomAttributes, "module");

            if (assemblyMos.GlobalIgnore || moduleMos.GlobalIgnore) return new SimplifyGlobalMos();

            foreach (var direct in moduleMos.Directs)
            {
                if (assemblyMos.Directs.ContainsKey(direct.Key))
                {
                    WriteInfo($"module replaces assembly MoAttribute: {direct.Key}");
                }
                assemblyMos.Directs[direct.Key] = direct.Value;
            }

            foreach (var generic in moduleMos.Generics)
            {
                if (assemblyMos.Generics.ContainsKey(generic.Key))
                {
                    WriteInfo($"module replaces assembly Generic MoAttribute: {generic.Key}]");
                }
                assemblyMos.Generics[generic.Key] = generic.Value;
            }

            foreach (var proxy in moduleMos.Proxies)
            {
                if (assemblyMos.Proxies.ContainsKey(proxy.Key))
                {
                    WriteInfo($"module replaces assembly MoProxyAttribute: {proxy.Key}");
                }
                assemblyMos.Proxies[proxy.Key] = proxy.Value;
            }

            // above GlobalIgnore has been checked null reference
            assemblyMos.Ignores!.AddRange(moduleMos.Ignores!);

            foreach (var ignore in assemblyMos.Ignores!.Keys)
            {
                if (assemblyMos.Directs.ContainsKey(ignore))
                {
                    assemblyMos.Directs.Remove(ignore);
                }
                if (assemblyMos.Generics.ContainsKey(ignore))
                {
                    assemblyMos.Generics.Remove(ignore);
                }
                var keys = assemblyMos.Proxies.Where(x => x.Value.FullName == ignore).Select(x => x.Key);
                foreach (var key in keys)
                {
                    assemblyMos.Proxies.Remove(key);
                }
            }

            return new SimplifyGlobalMos(assemblyMos.Directs.Values.SelectMany(x => x).ToArray(), assemblyMos.Generics.Values.ToArray(), assemblyMos.Proxies, assemblyMos.Ignores.Keys.ToArray());
        }

        /// <summary>
        /// 从attributes中查找继承自MoAttribute以及使用MoProxyAttribute代理的Attribute
        /// </summary>
        /// <param name="attributes">给定查找范围</param>
        /// <param name="locationName">全局范围名称</param>
        /// <returns>
        ///  directs: 继承自MoAttribute的类型
        /// generics: 使用RougamoAttribute的类型
        ///  proxies: 通过MoProxyAttribute代理的类型
        ///  ignores: 需要忽略的实现了IMo的织入类型
        /// </returns>
        private GlobalMos FindGlobalAttributes(Collection<CustomAttribute> attributes, string locationName)
        {
            var directs = new Dictionary<string, List<CustomAttribute>>();
            var generics = new Dictionary<string, TypeReference>();
            var proxies = new Dictionary<string, ProxyReleation>();
            var ignores = new Dictionary<string, TypeReference>();

            foreach (var attribute in attributes)
            {
                var attrType = attribute.AttributeType;
                if (attrType.DerivesFrom(Constants.TYPE_MoAttribute))
                {
                    ExtractMoAttributeUniq(directs, attribute);
                }
                else if (attribute.AttributeType.IsGeneric(Constants.TYPE_RougamoAttribute_1, out var genericTypeRefs))
                {
                    var moTypeRef = genericTypeRefs![0];
                    generics.TryAdd(moTypeRef.FullName, moTypeRef);
                }
                else if (attrType.Is(Constants.TYPE_MoProxyAttribute))
                {
                    var origin = (TypeReference)attribute.ConstructorArguments[0].Value;
                    var proxy = (TypeReference)attribute.ConstructorArguments[1].Value;
                    if (!proxy.DerivesFrom(Constants.TYPE_MoAttribute))
                    {
                        WriteError($"Mo proxy type({proxy.FullName}) must inherit from Rougamo.MoAttribute");
                    }
                    else if (!proxy.Resolve().GetConstructors().Any(ctor => !ctor.HasParameters))
                    {
                        WriteError($"Mo proxy type({proxy.FullName}) must contains non-parameters constructor");
                    }
                    else
                    {
                        var key = $"{origin.FullName}|{proxy.FullName}";
                        if (proxies.TryAdd(key, new ProxyReleation(origin, proxy)))
                        {
                            WriteInfo($"{locationName} MoProxyAttribute found: {key}");
                        }
                        else
                        {
                            WriteError($"duplicate {locationName} MoProxyAttribute found: {key}");
                        }
                    }
                }
                else if (attrType.Is(Constants.TYPE_IgnoreMoAttribute))
                {
                    if (!ExtractIgnores(ignores, attribute))
                    {
                        ignores = null;
                        break;
                    }
                }
            }

            return new GlobalMos(directs, generics, proxies.Values.ToDictionary(x => x.Origin.FullName, x => x.Proxy), ignores);
        }

        /// <summary>
        /// 从接口继承的方式中提取IMo以及对应的互斥类型
        /// </summary>
        /// <param name="typeDef">程序集中的类型</param>
        /// <returns>
        ///         mo: 实现IMo接口的类型
        /// repulsions: 与mo互斥的类型
        /// </returns>
        private RepulsionMo[] ExtractClassImplementations(TypeDefinition typeDef)
        {
            var mos = new List<RepulsionMo>();
            var mosInterfaces = typeDef.GetGenericInterfaces(Constants.TYPE_IRougamo_1);
            var repMosInterfaces = typeDef.GetGenericInterfaces(Constants.TYPE_IRougamo_2);
            var multiRepMosInterfaces = typeDef.GetGenericInterfaces(Constants.TYPE_IRepulsionsRougamo);

            mos.AddRange(mosInterfaces.Select(x => new RepulsionMo(x.GenericArguments[0], [])));
            mos.AddRange(repMosInterfaces.Select(x => new RepulsionMo(x.GenericArguments[0], [x.GenericArguments[1]])));
            mos.AddRange(multiRepMosInterfaces.Select(x => new RepulsionMo(x.GenericArguments[0], ExtractRepulsionFromIl(x.GenericArguments[1]))));

            return mos.ToArray();
        }

        /// <summary>
        /// 从IRepulsionsRougamo的泛型类型IL代码中提取互斥类型
        /// </summary>
        /// <param name="typeRef">IRepulsionsRougamo</param>
        /// <returns>互斥类型</returns>
        private TypeReference[] ExtractRepulsionFromIl(TypeReference typeRef)
        {
            return ExtractRepulsionFromProp(typeRef) ?? ExtractRepulsionFromCtor(typeRef) ?? [];
        }

        /// <summary>
        /// 从IRepulsionsRougamo泛型类型的属性Get方法中提取互斥类型
        /// </summary>
        /// <param name="typeRef">IRepulsionsRougamo</param>
        /// <returns>互斥类型</returns>
        private TypeReference[]? ExtractRepulsionFromProp(TypeReference typeRef)
        {
            var typeDef = typeRef.Resolve();
            while (typeDef != null)
            {
                var property = typeDef.Properties.FirstOrDefault(prop => prop.Name == Constants.PROP_Repulsions);
                if(property != null)
                {
                    Dictionary<string, TypeReference>? repulsions = null;
                    foreach (var instruction in property.GetMethod.Body.Instructions)
                    {
                        if(instruction.OpCode == OpCodes.Newarr)
                        {
                            repulsions = [];
                        }
                        else if(repulsions != null && instruction.IsLdtoken(Constants.TYPE_IMo, out var @ref) && !repulsions.ContainsKey(@ref!.FullName))
                        {
                            repulsions.Add(@ref.FullName, @ref);
                        }
                    }
                    return repulsions?.Values.ToArray();
                }
                typeDef = typeDef.BaseType?.Resolve();
            }
            return null;
        }

        /// <summary>
        /// 从IRepulsionsRougamo泛型类型的构造方法中提取互斥类型
        /// </summary>
        /// <param name="typeRef">IRepulsionsRougamo</param>
        /// <returns>互斥类型</returns>
        private TypeReference[]? ExtractRepulsionFromCtor(TypeReference typeRef)
        {
            var typeDef = typeRef.Resolve();
            while (typeDef != null)
            {
                var nonCtor = typeDef.GetConstructors().FirstOrDefault(ctor => !ctor.HasParameters);
                if (nonCtor != null)
                {
                    Dictionary<string, TypeReference>? repulsions = null;
                    var instructions = nonCtor.Body.Instructions;
                    for (int i = instructions.Count - 1; i >= 0; i--)
                    {
                        if (instructions[i].IsStfld(Constants.FIELD_Repulsions, Constants.TYPE_ARRAY_Type))
                        {
                            repulsions = [];
                        }
                        else if(repulsions != null && instructions[i].IsLdtoken(Constants.TYPE_IMo, out var @ref) && !repulsions.ContainsKey(@ref!.FullName))
                        {
                            repulsions.Add(@ref.FullName, @ref);
                        }
                        else if(instructions[i].OpCode == OpCodes.Newarr && repulsions != null)
                        {
                            return repulsions.Values.ToArray();
                        }
                    }
                }
                typeDef = typeDef.BaseType?.Resolve();
            }
            return null;
        }

        /// <summary>
        /// 从一堆CustomAttribute中提取MoAttribute的子类以及被代理的Attribute
        /// </summary>
        /// <param name="attributes">一堆CustomAttribute</param>
        /// <param name="proxies">代理声明</param>
        /// <returns>
        ///     mos: 继承自MoAttribute的类型
        /// proxied: 通过代理设置的实现IMo接口的类型
        /// </returns>
        private ExtractMos ExtractAttributes(Collection<CustomAttribute> attributes, Dictionary<string, TypeReference> proxies)
        {
            var mos = new Dictionary<string, List<CustomAttribute>>();
            var genericMos = new Dictionary<string, TypeReference>();
            var proxied = new Dictionary<string, TypeReference>();
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.DerivesFrom(Constants.TYPE_MoAttribute))
                {
                    ExtractMoAttributeUniq(mos, attribute);
                }
                else if (attribute.AttributeType.IsGeneric(Constants.TYPE_RougamoAttribute_1, out var genericTypeRefs))
                {
                    var moTypeRef = genericTypeRefs![0];
                    genericMos.TryAdd(moTypeRef.FullName, moTypeRef);
                }
                else if (attribute.AttributeType.Is(Constants.TYPE_RougamoAttribute))
                {
                    var moTypeRef = (TypeReference)attribute.ConstructorArguments[0].Value;
                    genericMos.TryAdd(moTypeRef.FullName, moTypeRef);
                }
                else if (proxies.TryGetValue(attribute.AttributeType.FullName, out var proxy))
                {
                    proxied.TryAdd(proxy.FullName, proxy);
                }
            }

            // proxies在FindGlobalAttributes中已经过滤了
            return new ExtractMos(mos.Values.SelectMany(x => x).ToArray(), genericMos.Values.ToArray(), proxied.Values.ToArray());
        }

        /// <summary>
        /// 去重的将MoAttribute添加到已有集合中并记录日志
        /// </summary>
        /// <param name="mos">已有的MoAttribute子类</param>
        /// <param name="attribute">CustomAttribute</param>
        private void ExtractMoAttributeUniq(Dictionary<string, List<CustomAttribute>> mos, CustomAttribute attribute)
        {
            if (!mos.TryGetValue(attribute.AttributeType.FullName, out var list))
            {
                list = [];
                mos.Add(attribute.AttributeType.FullName, list);
            }
            list.Add(attribute);
        }

        /// <summary>
        /// 从一堆Attribute中找到IgnoreAttribute并提取忽略的织入类型
        /// </summary>
        /// <param name="attributes">一堆Attribute</param>
        /// <returns>忽略的织入类型，如果返回null表示忽略全部</returns>
        private string[]? ExtractIgnores(Collection<CustomAttribute> attributes)
        {
            var ignores = new Dictionary<string, TypeReference>();
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.Is(Constants.TYPE_IgnoreMoAttribute) && !ExtractIgnores(ignores, attribute)) return null;
            }
            return ignores?.Keys.ToArray();
        }

        /// <summary>
        /// 从IgnoreMoAttribute中提取忽略的织入类型
        /// </summary>
        /// <param name="ignores">已有的忽略类型</param>
        /// <param name="attribute">IgnoreAttribute</param>
        /// <returns>如果忽略全部返回false，否则返回true</returns>
        private bool ExtractIgnores(Dictionary<string, TypeReference>? ignores, CustomAttribute attribute)
        {
            if (ignores == null || !attribute.HasProperties || !attribute.Properties.TryGet(Constants.PROP_MoTypes, out var property))
            {
                return false;
            }

            var enumerable = (IEnumerable)property!.Value.Argument.Value;
            foreach (CustomAttributeArgument item in enumerable)
            {
                var value = item.Value;
                var typeRef = value as TypeReference;
                if (typeRef != null && typeRef.Implement(Constants.TYPE_IMo))
                {
                    ignores.TryAdd(typeRef.FullName, typeRef);
                }
            }
            return true;
        }
    }
}
