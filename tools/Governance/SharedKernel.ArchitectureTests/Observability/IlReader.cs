using Mono.Cecil;
using Mono.Cecil.Cil;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>What produced a value on the IL evaluation stack.</summary>
internal enum ProducerKind
{
    /// <summary><c>ldstr</c>: <see cref="Producer.Text"/> holds the string.</summary>
    String,

    /// <summary><c>ldnull</c>.</summary>
    Null,

    /// <summary>A static or instance field load: not a compile-time constant.</summary>
    Field,

    /// <summary>The result of a call.</summary>
    Call,

    /// <summary>A method argument, whose value only the caller knows.</summary>
    Argument,

    /// <summary>The walk could not follow the value (a branch, a merge point, an unsupported instruction).</summary>
    Unresolved,

    /// <summary>Something else (a numeric constant, a conversion, …).</summary>
    Other,
}

/// <summary>The instruction that produced a value, as far as straight-line IL analysis can tell.</summary>
internal readonly record struct Producer(ProducerKind Kind, string Text);

/// <summary>
/// Mono.Cecil helpers shared by the observability assertions. Assemblies are read in memory (so the built file is never locked),
/// never loaded, and never through NetArchTest (its type discovery misses nested types).
/// </summary>
internal static class IlReader
{
    /// <summary>Reads an assembly fully into memory.</summary>
    public static ModuleDefinition Open(string path) =>
        ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true, ReadSymbols = false });

    /// <summary>Every type of the module, nested types at any depth included.</summary>
    public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) => AllTypes(module.Types);

    /// <summary>The types and all their nested types.</summary>
    public static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes))
                yield return nested;
        }
    }

    /// <summary>The site label of a method: <c>Namespace.Type::Method</c>.</summary>
    public static string Site(MethodDefinition method) => $"{method.DeclaringType.FullName}::{method.Name}";

    /// <summary>Gets the position, from the first pushed argument, of the parameter at <paramref name="parameterIndex"/>.</summary>
    /// <param name="call">The call, callvirt or newobj instruction.</param>
    /// <param name="parameterIndex">The zero-based declared parameter index (the instance receiver is not counted).</param>
    /// <returns>The number of values the call pops, and the slot of the parameter among them.</returns>
    private static (int Popped, int Slot) ArgumentSlot(Instruction call, int parameterIndex)
    {
        var method = (MethodReference)call.Operand;
        var hasThis = method.HasThis && call.OpCode.Code != Code.Newobj;
        var popped = method.Parameters.Count + (hasThis ? 1 : 0);
        return (popped, parameterIndex + (hasThis ? 1 : 0));
    }

    /// <summary>
    /// Finds what produced the argument <paramref name="parameterIndex"/> of the call at <paramref name="callIndex"/> by walking the
    /// IL backwards through the evaluation stack. Straight-line code only: a branch, a merge point or an unsupported instruction
    /// yields <see cref="ProducerKind.Unresolved"/>. A local is followed to the store that last wrote it.
    /// </summary>
    public static Producer ArgumentProducer(MethodBody body, int callIndex, int parameterIndex)
    {
        var instructions = body.Instructions;
        var (popped, slot) = ArgumentSlot(instructions[callIndex], parameterIndex);
        return ProducerOfStackSlot(body, callIndex, popped - slot, 0);
    }

    /// <summary>The first parameter of type <see cref="string"/>, or -1.</summary>
    public static int FirstStringParameter(MethodReference method)
    {
        for (var i = 0; i < method.Parameters.Count; i++)
        {
            if (method.Parameters[i].ParameterType.FullName == "System.String")
                return i;
        }

        return -1;
    }

    // Producer of the value that sits 'fromTop' slots below the top of the stack just before instruction 'beforeIndex' runs (1 = top).
    private static Producer ProducerOfStackSlot(MethodBody body, int beforeIndex, int fromTop, int depth)
    {
        var instructions = body.Instructions;
        var targets = BranchTargets(body);
        var need = fromTop;

        for (var j = beforeIndex - 1; j >= 0; j--)
        {
            var instruction = instructions[j];

            if (IsControlFlow(instruction))
                return new Producer(ProducerKind.Unresolved, "control flow");

            if (!TryStackEffect(instruction, out var pops, out var pushes))
                return new Producer(ProducerKind.Unresolved, instruction.OpCode.Name);

            if (need <= pushes)
            {
                // A merge point between the producer and the consumer means the stack depends on the path taken.
                if (HasTargetBetween(instructions, j, beforeIndex, targets))
                    return new Producer(ProducerKind.Unresolved, "merge point");

                return Describe(body, instruction, j, depth);
            }

            need = need - pushes + pops;

            if (targets.Contains(instruction))
                return new Producer(ProducerKind.Unresolved, "merge point");
        }

        return new Producer(ProducerKind.Unresolved, "start of method");
    }

    private static bool HasTargetBetween(Mono.Collections.Generic.Collection<Instruction> instructions, int producerIndex, int consumerIndex, HashSet<Instruction> targets)
    {
        for (var k = producerIndex + 1; k <= consumerIndex && k < instructions.Count; k++)
        {
            if (targets.Contains(instructions[k]))
                return true;
        }

        return false;
    }

    private static Producer Describe(MethodBody body, Instruction producer, int index, int depth)
    {
        switch (producer.OpCode.Code)
        {
            case Code.Ldstr:
                return new Producer(ProducerKind.String, (string)producer.Operand);

            case Code.Ldnull:
                return new Producer(ProducerKind.Null, string.Empty);

            case Code.Ldsfld:
            case Code.Ldfld:
            case Code.Ldsflda:
            case Code.Ldflda:
                return new Producer(ProducerKind.Field, ((FieldReference)producer.Operand).FullName);

            case Code.Call:
            case Code.Callvirt:
                return new Producer(ProducerKind.Call, ((MethodReference)producer.Operand).FullName);

            case Code.Ldarg:
            case Code.Ldarg_0:
            case Code.Ldarg_1:
            case Code.Ldarg_2:
            case Code.Ldarg_3:
            case Code.Ldarg_S:
                return new Producer(ProducerKind.Argument, string.Empty);

            case Code.Ldloc:
            case Code.Ldloc_0:
            case Code.Ldloc_1:
            case Code.Ldloc_2:
            case Code.Ldloc_3:
            case Code.Ldloc_S:
                return depth >= 3 ? new Producer(ProducerKind.Unresolved, "local chain") : ProducerOfLocal(body, producer, index, depth);

            default:
                return new Producer(ProducerKind.Other, producer.OpCode.Name);
        }
    }

    private static Producer ProducerOfLocal(MethodBody body, Instruction load, int loadIndex, int depth)
    {
        var instructions = body.Instructions;
        var variable = LocalOf(body, load);
        if (variable is null)
            return new Producer(ProducerKind.Unresolved, "local");

        for (var k = loadIndex - 1; k >= 0; k--)
        {
            var candidate = instructions[k];
            if (IsControlFlow(candidate))
                return new Producer(ProducerKind.Unresolved, "control flow");

            if (IsStoreTo(body, candidate, variable))
                return ProducerOfStackSlot(body, k, 1, depth + 1);
        }

        return new Producer(ProducerKind.Unresolved, "local never stored");
    }

    private static VariableDefinition? LocalOf(MethodBody body, Instruction instruction) =>
        instruction.OpCode.Code switch
        {
            Code.Ldloc_0 or Code.Stloc_0 => body.Variables.Count > 0 ? body.Variables[0] : null,
            Code.Ldloc_1 or Code.Stloc_1 => body.Variables.Count > 1 ? body.Variables[1] : null,
            Code.Ldloc_2 or Code.Stloc_2 => body.Variables.Count > 2 ? body.Variables[2] : null,
            Code.Ldloc_3 or Code.Stloc_3 => body.Variables.Count > 3 ? body.Variables[3] : null,
            _ => instruction.Operand as VariableDefinition,
        };

    private static bool IsStoreTo(MethodBody body, Instruction instruction, VariableDefinition variable) =>
        instruction.OpCode.Code is Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3
        && ReferenceEquals(LocalOf(body, instruction), variable);

    private static HashSet<Instruction> BranchTargets(MethodBody body)
    {
        var targets = new HashSet<Instruction>();
        foreach (var instruction in body.Instructions)
        {
            switch (instruction.Operand)
            {
                case Instruction target:
                    targets.Add(target);
                    break;
                case Instruction[] many:
                    foreach (var target in many)
                        targets.Add(target);
                    break;
            }
        }

        foreach (var handler in body.ExceptionHandlers)
        {
            if (handler.HandlerStart is not null)
                targets.Add(handler.HandlerStart);
            if (handler.FilterStart is not null)
                targets.Add(handler.FilterStart);
        }

        return targets;
    }

    private static bool IsControlFlow(Instruction instruction) =>
        instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw;

    // The number of stack values the instruction pops and pushes, or false for one this walker does not model.
    private static bool TryStackEffect(Instruction instruction, out int pops, out int pushes)
    {
        var code = instruction.OpCode.Code;

        if (code is Code.Call or Code.Callvirt or Code.Newobj)
        {
            var method = (MethodReference)instruction.Operand;
            var hasThis = method.HasThis && code != Code.Newobj;
            pops = method.Parameters.Count + (hasThis ? 1 : 0);
            pushes = code == Code.Newobj || method.ReturnType.FullName != "System.Void" ? 1 : 0;
            return true;
        }

        if (code is Code.Calli or Code.Ret or Code.Jmp or Code.Switch)
        {
            pops = pushes = 0;
            return false;
        }

        pops = instruction.OpCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
                or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref => 3,
            _ => -1,
        };

        pushes = instruction.OpCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            _ => -1,
        };

        return pops >= 0 && pushes >= 0;
    }
}
