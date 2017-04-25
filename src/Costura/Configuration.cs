using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

public class Configuration
{
    public bool OptOut { get; }
    public bool IncludeDebugSymbols { get; private set; }
    public bool DisableCompression { get; private set; }
    public bool CreateTemporaryAssemblies { get; private set; }
    public List<string> IncludeAssemblies { get; }
    public List<string> ExcludeAssemblies { get; }
    public List<string> Unmanaged32Assemblies { get; }
    public List<string> Unmanaged64Assemblies { get; }
    public List<string> PreloadOrder { get; }

    public Configuration(XElement config)
    {
        // Defaults
        OptOut = true;
        IncludeDebugSymbols = true;
        DisableCompression = false;
        CreateTemporaryAssemblies = false;
        IncludeAssemblies = new List<string>();
        ExcludeAssemblies = new List<string>();
        Unmanaged32Assemblies = new List<string>();
        Unmanaged64Assemblies = new List<string>();
        PreloadOrder = new List<string>();

        if (config == null)
            return;

        if (config.Attribute("IncludeAssemblies") != null || config.Element("IncludeAssemblies") != null)
            OptOut = false;

        ReadBool(config, "IncludeDebugSymbols", b => IncludeDebugSymbols = b);
        ReadBool(config, "DisableCompression", b => DisableCompression = b);
        ReadBool(config, "CreateTemporaryAssemblies", b => CreateTemporaryAssemblies = b);

        ReadList(config, "ExcludeAssemblies", ExcludeAssemblies);
        ReadList(config, "IncludeAssemblies", IncludeAssemblies);
        ReadList(config, "Unmanaged32Assemblies", Unmanaged32Assemblies);
        ReadList(config, "Unmanaged64Assemblies", Unmanaged64Assemblies);
        ReadList(config, "PreloadOrder", PreloadOrder);

        if (IncludeAssemblies.Any() && ExcludeAssemblies.Any())
            throw new WeavingException("Either configure IncludeAssemblies OR ExcludeAssemblies, not both.");
    }

    public static void ReadBool(XElement config, string nodeName, Action<bool> setter)
    {
        var attribute = config.Attribute(nodeName);
        if (attribute == null)
            return;

        if (bool.TryParse(attribute.Value, out bool value))
            setter(value);
        else
            throw new WeavingException($"Could not parse '{nodeName}' from '{attribute.Value}'.");
    }

    public static void ReadList(XElement config, string nodeName, List<string> list)
    {
        var attribute = config.Attribute(nodeName);
        if (attribute != null)
            list.AddRange(attribute.Value.Split('|').NonEmpty());

        var element = config.Element(nodeName);
        if (element != null)
            list.AddRange(element.Value.Split(new[] {"\r\n", "\n"}, StringSplitOptions.RemoveEmptyEntries).NonEmpty());
    }
}