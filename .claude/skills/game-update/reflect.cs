#:package System.Reflection.MetadataLoadContext@9.0.0
#:property EnableTrimAnalyzer=false

// Reads the REAL shape of a vanilla Lethal Company type off the installed game assembly.
//
//   dotnet run reflect.cs EntranceTeleport
//   dotnet run reflect.cs PlayerControllerB
//   dotnet run reflect.cs ~Teleport            (leading ~ = list every type whose name contains this)
//
// Why this and not a grep: a deleted member's NAME can survive in the assembly's string heap
// (`exitPoint` still appears because `exitPointDoesntExist` shares the prefix), so grep will
// happily "confirm" a member that no longer exists. This loads the metadata properly.
//
// Why this and not a decompiler: nothing executes, the game does not need to run, and it lists
// PRIVATE members too - so the BepInEx publicizer can never confuse the answer about whether a
// member is missing or merely inaccessible.

using System.Reflection;

// Adjust if the game moves. Read it from LethalGargoyles.csproj.user -> GameDirectory.
var managed = @"D:\Program Files\Steam App\steamapps\common\Lethal Company\Lethal Company_Data\Managed";

if (!Directory.Exists(managed))
{
    Console.Error.WriteLine($"Managed folder not found: {managed}");
    Console.Error.WriteLine("If the game folder exists but this does not, the game is UNINSTALLED - Steam leaves mod folders behind.");
    return 1;
}

var wanted = args.Length > 0 ? args[0] : "EntranceTeleport";

var resolver = new PathAssemblyResolver(Directory.GetFiles(managed, "*.dll"));
using var mlc = new MetadataLoadContext(resolver);
var asm = mlc.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));

if (wanted.StartsWith("~"))
{
    foreach (var t in asm.GetTypes())
        if (t.Name.Contains(wanted[1..], StringComparison.OrdinalIgnoreCase))
            Console.WriteLine(t.FullName);
    return 0;
}

var type = asm.GetType(wanted);
if (type is null)
{
    Console.Error.WriteLine($"Type '{wanted}' not found. Try: dotnet run reflect.cs ~{wanted}");
    return 1;
}

const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

Console.WriteLine($"=== {type.FullName} : {type.BaseType?.Name} ===");

Console.WriteLine("--- fields ---");
foreach (var f in type.GetFields(All))
    Console.WriteLine($"  {(f.IsPublic ? "public " : "private")} {(f.IsStatic ? "static " : "")}{f.FieldType.Name,-26} {f.Name}");

Console.WriteLine("--- properties ---");
foreach (var p in type.GetProperties(All))
    Console.WriteLine($"  {p.PropertyType.Name,-26} {p.Name}");

Console.WriteLine("--- methods ---");
foreach (var m in type.GetMethods(All))
{
    if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("__rpc_handler_")) continue;
    var ps = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
    Console.WriteLine($"  {(m.IsPublic ? "public " : "private")} {m.ReturnType.Name,-20} {m.Name}({ps})");
}

return 0;
