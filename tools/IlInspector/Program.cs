using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

var assembly = AssemblyDefinition.ReadAssembly(args[0]);
foreach (var spec in args.Skip(1))
{
    if (spec.StartsWith("^"))
    {
        var query = spec[1..];
        foreach (var searchType in assembly.MainModule.Types.SelectMany(AllTypes))
            foreach (var method in searchType.Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i =>
                         i.Operand?.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)))
                Console.WriteLine($"REFERENCE {method.FullName}");
        continue;
    }
    if (spec.StartsWith("?"))
    {
        var query = spec[1..];
        foreach (var searchType in assembly.MainModule.Types.SelectMany(AllTypes))
        {
            if (searchType.FullName.Contains(query, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"TYPE {searchType.FullName}");
            foreach (var field in searchType.Fields.Where(f => f.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"FIELD {field.FullName}");
            foreach (var property in searchType.Properties.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"PROPERTY {property.FullName}");
            foreach (var method in searchType.Methods.Where(m => m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"METHOD {method.FullName}");
        }
        continue;
    }
    if (spec.StartsWith("@"))
    {
        var wanted = spec[1..];
        var found = assembly.MainModule.Types.SelectMany(AllTypes).FirstOrDefault(t => t.FullName == wanted);
        if (found == null)
        {
            Console.Error.WriteLine($"TYPE NOT FOUND: {wanted}");
            continue;
        }
        Console.WriteLine($"\n=== TYPE {found.FullName} : {found.BaseType} ===");
        foreach (var field in found.Fields) Console.WriteLine($"FIELD {field.FullName}");
        foreach (var property in found.Properties) Console.WriteLine($"PROPERTY {property.FullName}");
        foreach (var method in found.Methods) Console.WriteLine($"METHOD {method.FullName}");
        continue;
    }
    var separator = spec.LastIndexOf("::", StringComparison.Ordinal);
    var typeName = spec[..separator];
    var methodName = spec[(separator + 2)..];
    var type = assembly.MainModule.Types.SelectMany(AllTypes).FirstOrDefault(t => t.FullName == typeName);
    if (type == null)
    {
        Console.Error.WriteLine($"TYPE NOT FOUND: {typeName}");
        continue;
    }
    foreach (var method in type.Methods.Where(m => m.Name == methodName))
    {
        Console.WriteLine($"\n=== {method.FullName} ===");
        if (!method.HasBody) continue;
        foreach (var instruction in method.Body.Instructions)
            Console.WriteLine($"{instruction.Offset:X4}: {instruction.OpCode,-12} {Format(instruction.Operand)}");
    }
}

static System.Collections.Generic.IEnumerable<TypeDefinition> AllTypes(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes.SelectMany(AllTypes)) yield return nested;
}

static string Format(object? operand) => operand switch
{
    null => string.Empty,
    Instruction target => $"IL_{target.Offset:X4}",
    Instruction[] targets => string.Join(", ", targets.Select(t => $"IL_{t.Offset:X4}")),
    _ => operand.ToString() ?? string.Empty
};
