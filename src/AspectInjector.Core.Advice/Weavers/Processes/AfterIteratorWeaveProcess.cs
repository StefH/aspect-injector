﻿using AspectInjector.Core.Advice.Effects;
using AspectInjector.Core.Contracts;
using AspectInjector.Core.Extensions;
using AspectInjector.Core.Models;
using FluentIL;
using FluentIL.Extensions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Linq;

namespace AspectInjector.Core.Advice.Weavers.Processes
{
    internal class AfterIteratorWeaveProcess : AfterStateMachineWeaveProcessBase
    {
        public AfterIteratorWeaveProcess(ILogger log, MethodDefinition target, InjectionDefinition injection)
            : base(log, target, injection)
        {

        }

        protected override TypeDefinition GetStateMachine()
        {
            return _target.CustomAttributes.First(ca => ca.AttributeType.FullName == WellKnownTypes.IteratorStateMachineAttribute)
                .GetConstructorValue<TypeReference>(0).Resolve();
        }

        protected override MethodDefinition FindOrCreateAfterStateMachineMethod()
        {
            var afterMethod = _stateMachine.Methods.FirstOrDefault(m => m.Name == Constants.AfterStateMachineMethodName);

            if (afterMethod == null)
            {
                var moveNext = _stateMachine.Methods.First(m => m.Name == "MoveNext");

                var moveNextEditor = moveNext.GetEditor();

                var exitPoints = moveNext.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();

                afterMethod = new MethodDefinition(Constants.AfterStateMachineMethodName, MethodAttributes.Private, _ts.Void);
                _stateMachine.Methods.Add(afterMethod);
                afterMethod.GetEditor().Mark(_ts.DebuggerHiddenAttribute);
                afterMethod.GetEditor().Instead(pc => pc.Return());

                foreach (var exit in exitPoints.Where(e => e.Previous.OpCode == OpCodes.Ldc_I4 && (int)e.Previous.Operand == 0))
                {
                    moveNextEditor.Before(exit, il =>
                    {
                        return il.ThisOrStatic().Call(afterMethod.MakeHostInstanceGeneric(_stateMachine));
                    });
                }
            }

            return afterMethod;
        }


        protected override Cut LoadReturnValueArgument(Cut pc, AdviceArgument parameter)
        {
            return pc.This();
        }

        protected override Cut LoadReturnTypeArgument(Cut pc, AdviceArgument parameter)
        {
            return pc.TypeOf(_stateMachine.Interfaces.First(i => i.InterfaceType.Name.StartsWith("IEnumerable`1")).InterfaceType);
        }

        protected override void InsertStateMachineCall(PointCut code)
        {
            _target.GetEditor().BeforeExit(code);
        }
    }
}