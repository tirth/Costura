using System;
using System.Linq;
using Mono.Cecil;

partial class ModuleWeaver
{
    private TypeReference _voidTypeReference;
    private MethodReference _compilerGeneratedAttributeCtor;
    private MethodReference _dictionaryOfStringOfStringAdd;
    private MethodReference _listOfStringAdd;

    private void FindMsCoreReferences()
    {
        var msCoreLibDefinition = AssemblyResolver.Resolve(new AssemblyNameReference("mscorlib", new Version(4, 0)));
        var msCoreTypes = msCoreLibDefinition.MainModule.Types;

        var objectDefinition = msCoreTypes.FirstOrDefault(x => x.Name == "Object");
        if (objectDefinition == null)
        {
            throw new WeavingException("Only compat with desktop .net");
        }

        var voidDefinition = msCoreTypes.First(x => x.Name == "Void");
        _voidTypeReference = ModuleDefinition.ImportReference(voidDefinition);

        var dictionary = msCoreTypes.First(x => x.Name == "Dictionary`2");
        var dictionaryOfStringOfString = ModuleDefinition.ImportReference(dictionary);
        _dictionaryOfStringOfStringAdd = ModuleDefinition.ImportReference(dictionaryOfStringOfString.Resolve().Methods.First(m => m.Name == "Add"))
            .MakeHostInstanceGeneric(ModuleDefinition.TypeSystem.String, ModuleDefinition.TypeSystem.String);

        var list = msCoreTypes.First(x => x.Name == "List`1");
        var listOfString = ModuleDefinition.ImportReference(list);
        _listOfStringAdd = ModuleDefinition.ImportReference(listOfString.Resolve().Methods.First(m => m.Name == "Add"))
            .MakeHostInstanceGeneric(ModuleDefinition.TypeSystem.String);

        var compilerGeneratedAttribute = msCoreTypes.First(x => x.Name == "CompilerGeneratedAttribute");
        _compilerGeneratedAttributeCtor = ModuleDefinition.ImportReference(compilerGeneratedAttribute.Methods.First(x => x.IsConstructor));
    }
}