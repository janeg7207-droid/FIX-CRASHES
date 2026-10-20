using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

if (args.Length != 3)
    throw new ArgumentException("Usage: Patcher <original.exe> <GorillaCrashDetails.dll> <output.exe>");
string originalPath = Path.GetFullPath(args[0]);
string addOnPath = Path.GetFullPath(args[1]);
string outputPath = Path.GetFullPath(args[2]);
if (originalPath == outputPath) throw new InvalidOperationException("The original must not be overwritten.");
const string expectedHash = "f09f6ff9d8da90978a747cd39ed8d5445607478419f4af8c9b65ce3db2dc684b";
if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalPath))).ToLowerInvariant() != expectedHash)
    throw new InvalidOperationException("This patch is only validated for the supplied v2.0.4 executable; original SHA-256 differs.");
using var assembly = AssemblyDefinition.ReadAssembly(originalPath);
using var addOn = AssemblyDefinition.ReadAssembly(addOnPath);
if (assembly.Name.HasPublicKey) throw new InvalidOperationException("A signed original requires a different release process.");
var doctor = assembly.MainModule.Types.Single(t => t.FullName == "Gorilla.Manager.Engine.Doctor");
var examine = doctor.Methods.Single(m => m.Name == "Examine" && !m.HasParameters);
var core = doctor.Fields.Single(f => f.Name == "_core");
var clock = doctor.Fields.Single(f => f.Name == "Now");
var enhance = addOn.MainModule.Types.Single(t => t.FullName == "Gorilla.CrashDetails.CrashDetailsBridge")
    .Methods.Single(m => m.Name == "Enhance");
var imported = assembly.MainModule.ImportReference(enhance);
var returns = examine.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToList();
if (returns.Count != 2) throw new InvalidOperationException("Unexpected Doctor.Examine control flow; refusing to patch.");
examine.Body.SimplifyMacros();
var il = examine.Body.GetILProcessor();
foreach (var end in returns)
{
    // Keep this instruction's identity, so branches to the old return also run
    // the hook. The original Report is already on the evaluation stack.
    end.OpCode = OpCodes.Ldarg_0;
    end.Operand = null;
    var tail = end;
    foreach (var instruction in new[] {
        Instruction.Create(OpCodes.Ldfld, core),
        Instruction.Create(OpCodes.Ldarg_0),
        Instruction.Create(OpCodes.Ldfld, clock),
        Instruction.Create(OpCodes.Call, imported),
        Instruction.Create(OpCodes.Ret) })
    {
        il.InsertAfter(tail, instruction);
        tail = instruction;
    }
}
examine.Body.MaxStackSize = Math.Max(examine.Body.MaxStackSize, 4);
examine.Body.OptimizeMacros();
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
assembly.Write(outputPath);

using var original = AssemblyDefinition.ReadAssembly(originalPath);
using var patched = AssemblyDefinition.ReadAssembly(outputPath);
var originalMethods = AllTypes(original.MainModule.Types).SelectMany(t => t.Methods).ToList();
var patchedMethods = AllTypes(patched.MainModule.Types).SelectMany(t => t.Methods).ToList();
if (originalMethods.Count != patchedMethods.Count) throw new Exception("Method count changed.");
int unchanged = 0;
for (int n = 0; n < originalMethods.Count; n++)
{
    var before = originalMethods[n];
    var after = patchedMethods[n];
    if (before.FullName != after.FullName) throw new Exception("Method identity changed.");
    if (before.FullName == examine.FullName) continue;
    if (MethodSignature(before) != MethodSignature(after)) throw new Exception("Unexpected change in " + before.FullName);
    unchanged++;
}
if (original.MainModule.Resources.Count != patched.MainModule.Resources.Count) throw new Exception("Resource count changed.");
foreach (var resource in original.MainModule.Resources.OfType<EmbeddedResource>())
{
    var updated = patched.MainModule.Resources.OfType<EmbeddedResource>().Single(r => r.Name == resource.Name);
    if (!resource.GetResourceData().SequenceEqual(updated.GetResourceData())) throw new Exception("Resource changed: " + resource.Name);
}
if (original.Name.FullName != patched.Name.FullName) throw new Exception("Assembly identity changed.");
var hook = AllTypes(patched.MainModule.Types).SelectMany(t => t.Methods).Single(m => m.FullName == examine.FullName);
if (hook.Body.Instructions.Count(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference m && m.FullName == imported.FullName) != returns.Count)
    throw new Exception("Not every report return was hooked.");
Console.WriteLine($"Verified: {unchanged} method bodies unchanged; only Doctor.Examine extended at its {returns.Count} returns.");
Console.WriteLine($"Verified: all {original.MainModule.Resources.Count} original resources and the assembly identity are unchanged.");

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
    }
}

static string MethodSignature(MethodDefinition method)
{
    if (!method.HasBody) return "no body";
    var body = method.Body;
    var text = new StringBuilder().Append(body.InitLocals).Append('|');
    foreach (var variable in body.Variables) text.Append(variable.VariableType.FullName).Append(';');
    foreach (var instruction in body.Instructions)
    {
        text.Append(instruction.OpCode.Name).Append(':');
        switch (instruction.Operand)
        {
            case Instruction target: text.Append(body.Instructions.IndexOf(target)); break;
            case Instruction[] targets: text.AppendJoin(',', targets.Select(t => body.Instructions.IndexOf(t))); break;
            case MemberReference member: text.Append(member.FullName); break;
            default: text.Append(instruction.Operand); break;
        }
        text.Append('|');
    }
    foreach (var handler in body.ExceptionHandlers)
        text.Append(handler.HandlerType).Append(':').Append(handler.CatchType?.FullName).Append(':')
            .Append(Index(handler.TryStart)).Append(',').Append(Index(handler.TryEnd)).Append(',')
            .Append(Index(handler.HandlerStart)).Append(',').Append(Index(handler.HandlerEnd)).Append(',')
            .Append(Index(handler.FilterStart)).Append('|');
    return text.ToString();
    int Index(Instruction? instruction) => instruction == null ? -1 : body.Instructions.IndexOf(instruction);
}
