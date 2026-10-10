using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Usage: AccessAudit <mod.dll> <original assembly_valheim.dll>
// Lists every field/method/property accessor the mod references on a type from the original assembly
// that is NOT public there (so it only compiles because the reference was publicized).
var modPath = args[0];
var gamePath = args[1];

using var modStream = File.OpenRead(modPath);
using var modPe = new PEReader(modStream);
var mod = modPe.GetMetadataReader();

using var gameStream = File.OpenRead(gamePath);
using var gamePe = new PEReader(gameStream);
var game = gamePe.GetMetadataReader();

string TypeFullName(MetadataReader r, TypeDefinition t)
{
    var ns = r.GetString(t.Namespace);
    var n = r.GetString(t.Name);
    if (!t.GetDeclaringType().IsNil)
        return TypeFullName(r, r.GetTypeDefinition(t.GetDeclaringType())) + "+" + n;
    return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
}

var gameTypes = new Dictionary<string, TypeDefinition>();
foreach (var h in game.TypeDefinitions)
{
    var t = game.GetTypeDefinition(h);
    gameTypes[TypeFullName(game, t)] = t;
}

string RefTypeName(TypeReference tr)
{
    var ns = mod.GetString(tr.Namespace);
    var n = mod.GetString(tr.Name);
    if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
        return RefTypeName(mod.GetTypeReference((TypeReferenceHandle)tr.ResolutionScope)) + "+" + n;
    return string.IsNullOrEmpty(ns) ? n : ns + "." + n;
}

var problems = new SortedSet<string>();
int checkedCount = 0;

foreach (var h in mod.MemberReferences)
{
    var mr = mod.GetMemberReference(h);
    if (mr.Parent.Kind != HandleKind.TypeReference) continue;
    var tr = mod.GetTypeReference((TypeReferenceHandle)mr.Parent);
    var typeName = RefTypeName(tr);
    if (!gameTypes.TryGetValue(typeName, out var def)) continue; // not a game type

    var name = mod.GetString(mr.Name);
    checkedCount++;

    bool found = false, accessible = false;
    string how = "";

    foreach (var fh in def.GetFields())
    {
        var f = game.GetFieldDefinition(fh);
        if (game.GetString(f.Name) != name) continue;
        found = true;
        var acc = f.Attributes & FieldAttributes.FieldAccessMask;
        // protected members are legal from subclasses (the mod only does that); private/internal are not
        if (acc == FieldAttributes.Public || acc == FieldAttributes.Family || acc == FieldAttributes.FamORAssem) accessible = true;
        how = acc.ToString();
    }
    foreach (var mh in def.GetMethods())
    {
        var m = game.GetMethodDefinition(mh);
        if (game.GetString(m.Name) != name) continue;
        found = true;
        var acc = m.Attributes & MethodAttributes.MemberAccessMask;
        // any overload that is public counts (signature matching is overkill for an audit)
        if (acc == MethodAttributes.Public || acc == MethodAttributes.Family || acc == MethodAttributes.FamORAssem) accessible = true;
        how = acc.ToString();
    }

    if (!found)
    {
        // could be an inherited member declared on a base type; check bases
        continue;
    }
    if (!accessible) problems.Add($"{typeName}::{name}  ({how} in the real assembly)");
}

Console.WriteLine($"checked {checkedCount} references into the game assembly");
if (problems.Count == 0) Console.WriteLine("OK: nothing non-public is referenced.");
else
{
    Console.WriteLine("NON-PUBLIC members referenced (will throw at runtime unless reached via reflection):");
    foreach (var p in problems) Console.WriteLine("  " + p);
}
