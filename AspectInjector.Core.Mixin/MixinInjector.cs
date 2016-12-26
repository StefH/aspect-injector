﻿using AspectInjector.Core.Defaults;
using AspectInjector.Core.Extensions;
using AspectInjector.Core.Fluent.Models;
using AspectInjector.Core.Models;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AspectInjector.Core.Mixin
{
    internal class MixinInjector : InjectorBase<Mixin>
    {
        public MixinInjector()
        {
            Priority = 10;
        }

        protected override void Apply(Aspect<TypeDefinition> aspect, Mixin mixin)
        {
            var ts = Context.Editors.GetContext(aspect.Target.Module).TypeSystem;

            var target = aspect.Target;

            var ifaceTree = GetInterfacesTree(mixin.InterfaceType);

            foreach (var iface in ifaceTree)
                if (target.Interfaces.All(i => !i.IsTypeOf(iface)))
                    target.Interfaces.Add(ts.Import(iface));

            //search for implementtions
            var host = aspect.InjectionHost.Resolve();

            var methods = GetInterfaceTreeMembers(mixin.InterfaceType, td => td.Methods)
                .Where(m => !m.IsAddOn && !m.IsRemoveOn && !m.IsSetter && !m.IsGetter)
                .ToArray();

            var props = GetInterfaceTreeMembers(mixin.InterfaceType, td => td.Properties)
                .Select(p => host.Properties.First(hp => hp.Name == p.Name));

            var events = GetInterfaceTreeMembers(mixin.InterfaceType, td => td.Events)
                .Select(e => host.Events.First(he => he.Name == e.Name));

            foreach (var method in methods)
                GetOrCreateMethodProxy(method, mixin.InterfaceType, aspect, ts);

            foreach (var @event in events)
                GetOrCreateEventProxy(@event, mixin.InterfaceType, aspect, ts);

            foreach (var property in props)
                GetOrCreatePropertyProxy(property, mixin.InterfaceType, aspect, ts);
        }

        protected MethodDefinition GetOrCreateMethodProxy(MethodReference method, TypeReference owner, Aspect<TypeDefinition> aspect, ExtendedTypeSystem ts)
        {
            var targetType = aspect.Target;
            var methodDef = method.Resolve();
            var methodName = GenerateMemberProxyName(methodDef);

            var proxy = targetType.Methods.FirstOrDefault(m => m.Name == methodName && SignatureMatches(m, method));
            if (proxy == null)
            {
                proxy = new MethodDefinition(methodName,
                    MethodAttributes.Private | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual,
                     ts.Import(method.ReturnType));

                targetType.Methods.Add(proxy);

                if (methodDef.IsSpecialName)
                    proxy.IsSpecialName = true;

                proxy.Overrides.Add(ts.Import(method));

                foreach (var parameter in method.Parameters)
                    proxy.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, ts.Import(parameter.ParameterType)));

                //TODO:: Use method from SnippetsProcessor, check generic args
                foreach (var genericParameter in method.GenericParameters)
                    proxy.GenericParameters.Add(genericParameter);

                var me = Context.Editors.GetEditor(proxy);

                me.Instead(e =>
                    e.Return(ret =>
                    ret.Load(aspect).Call(method, args => proxy.Parameters.ToList().ForEach(p => args.Load(p)))));
            }

            return proxy;
        }

        protected EventDefinition GetOrCreateEventProxy(EventDefinition originalEvent, TypeReference owner, Aspect<TypeDefinition> aspect, ExtendedTypeSystem ts)
        {
            var eventName = GenerateMemberProxyName(originalEvent);

            var ed = aspect.Target.Events.FirstOrDefault(e => e.Name == eventName && e.EventType.IsTypeOf(originalEvent.EventType));
            if (ed == null)
            {
                var newAddMethod = originalEvent.AddMethod == null ? null : GetOrCreateMethodProxy(originalEvent.AddMethod, owner, aspect, ts);
                var newRemoveMethod = originalEvent.AddMethod == null ? null : GetOrCreateMethodProxy(originalEvent.RemoveMethod, owner, aspect, ts);

                ed = new EventDefinition(eventName, EventAttributes.None, ts.Import(originalEvent.EventType));
                ed.AddMethod = newAddMethod;
                ed.RemoveMethod = newRemoveMethod;

                aspect.Target.Events.Add(ed);
            }

            return ed;
        }

        protected PropertyDefinition GetOrCreatePropertyProxy(PropertyDefinition originalProperty, TypeReference owner, Aspect<TypeDefinition> aspect, ExtendedTypeSystem ts)
        {
            var propertyName = GenerateMemberProxyName(originalProperty);

            var pd = aspect.Target.Properties.FirstOrDefault(p => p.Name == propertyName && p.PropertyType.IsTypeOf(originalProperty.PropertyType));
            if (pd == null)
            {
                var newGetMethod = originalProperty.GetMethod == null ? null : GetOrCreateMethodProxy(originalProperty.GetMethod, owner, aspect, ts);
                var newSetMethod = originalProperty.SetMethod == null ? null : GetOrCreateMethodProxy(originalProperty.SetMethod, owner, aspect, ts);

                pd = new PropertyDefinition(propertyName, PropertyAttributes.None, ts.Import(originalProperty.PropertyType));
                pd.GetMethod = newGetMethod;
                pd.SetMethod = newSetMethod;

                aspect.Target.Properties.Add(pd);
            }

            return pd;
        }

        private static string GenerateMemberProxyName(IMemberDefinition member)
        {
            return member.DeclaringType.FullName + "." + member.Name;
        }

        private static IEnumerable<TypeReference> GetInterfacesTree(TypeReference typeReference)
        {
            var definition = typeReference.Resolve();
            if (!definition.IsInterface)
                throw new NotSupportedException(typeReference.Name + " should be an interface");

            var nestedIfaces = definition.Interfaces.ToList().AsEnumerable();

            if (typeReference.IsGenericInstance)
            {
                var generic = (IGenericInstance)typeReference;

                nestedIfaces = nestedIfaces.Select(nested =>
                {
                    if (nested.IsGenericInstance)
                    {
                        var nestedGeneric = (IGenericInstance)nested;
                        var args = generic.GenericArguments.Concat(nestedGeneric.GenericArguments.Skip(generic.GenericArguments.Count)).ToArray();

                        return nested.Resolve().MakeGenericInstanceType(args);
                    }
                    else
                    {
                        return nested.MakeGenericInstanceType(generic.GenericArguments.ToArray());
                    }
                });
            }

            return new[] { typeReference }.Concat(nestedIfaces);
        }

        private static IEnumerable<T> GetInterfaceTreeMembers<T>(TypeReference typeReference, Func<TypeDefinition, IEnumerable<T>> selector)
        {
            var definition = typeReference.Resolve();
            if (!definition.IsInterface)
                throw new NotSupportedException(typeReference.Name + " should be an interface");

            var members = selector(definition);

            if (definition.Interfaces.Count > 0)
                members = members.Concat(definition.Interfaces.SelectMany(i => GetInterfaceTreeMembers(i, selector)));

            return members;
        }

        private MethodReference ParametrizeGenerics(MethodReference method, GenericInstanceType owner, ExtendedTypeSystem ts)
        {
            var mr = new MethodReference(method.Name, FindMathingArgument(method.ReturnType, owner, ts), owner);

            mr.CallingConvention = method.CallingConvention;
            mr.ExplicitThis = method.ExplicitThis;
            mr.HasThis = method.HasThis;
            mr.MetadataToken = method.MetadataToken;

            foreach (var gp in method.GenericParameters)
                mr.GenericParameters.Add((GenericParameter)ts.Import(gp));

            foreach (var par in method.Parameters)
                mr.Parameters.Add(new ParameterDefinition(par.Name, par.Attributes, FindMathingArgument(par.ParameterType, owner, ts)));

            return mr;
        }

        public static TypeReference FindMathingArgument(TypeReference par, IGenericInstance owner, ExtendedTypeSystem ts)
        {
            if (par is GenericParameter)
            {
                var gpar = (GenericParameter)par;

                if (gpar.Owner is TypeReference)
                    return owner.GenericArguments[gpar.Position];
            }

            return par;
        }

        public static bool SignatureMatches(MethodReference methodReference1, MethodReference methodReference2)
        {
            if (methodReference1.IsGenericInstance && methodReference2.HasGenericParameters)
            {
                // if(methodReference1.)
            }

            if (!methodReference1.MethodReturnType.ReturnType.IsTypeOf(methodReference2.MethodReturnType.ReturnType))
                return false;

            for (int i = 0; i < methodReference1.Parameters.Count; i++)
                if (!methodReference1.Parameters[i].ParameterType.IsTypeOf(methodReference2.Parameters[i].ParameterType))
                    return false;

            return true;
        }
    }
}