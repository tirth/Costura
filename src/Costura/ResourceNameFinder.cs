using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

partial class ModuleWeaver
{
    private void BuildUpNameDictionary(bool createTemporaryAssemblies, List<string> preloadOrder)
    {
        var orderedResources = preloadOrder
            .Join(ModuleDefinition.Resources, p => p.ToLowerInvariant(), r => GetNameAndExt(r.Name.Split('.')).name, (s, r) => r)
            .Union(ModuleDefinition.Resources.OrderBy(r => r.Name))
            .Where(r => r.Name.StartsWith("costura"))
            .Select(r => r.Name);

        foreach (var resource in orderedResources)
        {
            var parts = resource.Split('.');

            switch (parts[0])
            {
                case "costura":
                    if (createTemporaryAssemblies)
                        AddToList(_preloadListField, resource);
                    else
                    {
                        var (name, ext) = GetNameAndExt(parts);
                        AddToDictionary(ext == "pdb" ? _symbolNamesField : _assemblyNamesField, name, resource);
                    }

                    break;
                    
                case "costura32":
                    AddToList(_preload32ListField, resource);
                    break;

                case "costura64":
                    AddToList(_preload64ListField, resource);
                    break;
            }
        }
    }

    private static (string name, string ext) GetNameAndExt(IReadOnlyList<string> parts)
    {
        var isZip = parts.Last() == "zip";

        var ext = parts[parts.Count - (isZip ? 2 : 1)];

        var name = string.Join(".", parts.Skip(1).Take(parts.Count - (isZip ? 3 : 2)));

        return (name, ext);
    }

    private void AddToDictionary(FieldReference field, string key, string name)
    {
        var retIndex = _loaderCctor.Body.Instructions.Count - 1;
        _loaderCctor.Body.Instructions.InsertBefore(retIndex, 
            Instruction.Create(OpCodes.Ldsfld, field), 
            Instruction.Create(OpCodes.Ldstr, key), 
            Instruction.Create(OpCodes.Ldstr, name), 
            Instruction.Create(OpCodes.Callvirt, _dictionaryOfStringOfStringAdd));
    }

    private void AddToList(FieldReference field, string name)
    {
        var retIndex = _loaderCctor.Body.Instructions.Count - 1;
        _loaderCctor.Body.Instructions.InsertBefore(retIndex, 
            Instruction.Create(OpCodes.Ldsfld, field),
            Instruction.Create(OpCodes.Ldstr, name),
            Instruction.Create(OpCodes.Callvirt, _listOfStringAdd));
    }
}