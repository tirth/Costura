using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Mono.Cecil;

partial class ModuleWeaver : IDisposable
{
    private readonly List<Stream> _streams = new List<Stream>();
    private string _cachePath;

    private void EmbedResources(Configuration config)
    {
        if (ReferenceCopyLocalPaths == null)
            throw new WeavingException("ReferenceCopyLocalPaths is required you may need to update to the latest version of Fody.");

        _cachePath = Path.Combine(Path.GetDirectoryName(AssemblyFilePath), "Costura");
        if (!Directory.Exists(_cachePath))
            Directory.CreateDirectory(_cachePath);

        var binaries = ReferenceCopyLocalPaths.Where(x => x.EndsWith(".dll") || x.EndsWith(".exe")).ToArray();

        foreach (var dependency in GetFilteredReferences(binaries, config))
        {
            var fullPath = Path.GetFullPath(dependency);

            string resourceName;
            if (dependency.EndsWith(".resources.dll"))
            {
                resourceName = Embed($"costura.{Path.GetFileName(Path.GetDirectoryName(fullPath))}.", fullPath, !config.DisableCompression);

                if (config.CreateTemporaryAssemblies)
                    _checksums.Add(resourceName, CalculateChecksum(fullPath));

                continue;
            }

            resourceName = Embed("costura.", fullPath, !config.DisableCompression);
            if (config.CreateTemporaryAssemblies)
                _checksums.Add(resourceName, CalculateChecksum(fullPath));

            if (!config.IncludeDebugSymbols)
                continue;

            var pdbFullPath = Path.ChangeExtension(fullPath, "pdb");
            if (!File.Exists(pdbFullPath))
                continue;

            resourceName = Embed("costura.", pdbFullPath, !config.DisableCompression);
            if (config.CreateTemporaryAssemblies)
                _checksums.Add(resourceName, CalculateChecksum(pdbFullPath));
        }

        foreach (var dependency in binaries)
        {
            var prefix = "";

            if (config.Unmanaged32Assemblies.Any(x => x == Path.GetFileNameWithoutExtension(dependency)))
            {
                prefix = "costura32.";
                _hasUnmanaged = true;
            }
            if (config.Unmanaged64Assemblies.Any(x => x == Path.GetFileNameWithoutExtension(dependency)))
            {
                prefix = "costura64.";
                _hasUnmanaged = true;
            }

            if (string.IsNullOrEmpty(prefix))
                continue;

            var fullPath = Path.GetFullPath(dependency);
            var resourceName = Embed(prefix, fullPath, !config.DisableCompression);
            _checksums.Add(resourceName, CalculateChecksum(fullPath));

            if (!config.IncludeDebugSymbols)
                continue;

            var pdbFullPath = Path.ChangeExtension(fullPath, "pdb");
            if (!File.Exists(pdbFullPath))
                continue;

            resourceName = Embed(prefix, pdbFullPath, !config.DisableCompression);
            _checksums.Add(resourceName, CalculateChecksum(pdbFullPath));
        }
    }

    private IEnumerable<string> GetFilteredReferences(IEnumerable<string> onlyBinaries, Configuration config)
    {
        if (config.IncludeAssemblies.Any())
        {
            var skippedAssemblies = new List<string>(config.IncludeAssemblies);

            foreach (var file in onlyBinaries)
            {
                var assemblyName = Path.GetFileNameWithoutExtension(file);

                if (config.IncludeAssemblies.All(x => x != assemblyName) ||
                    config.Unmanaged32Assemblies.Any(x => x == assemblyName) ||
                    config.Unmanaged64Assemblies.Any(x => x == assemblyName))
                    continue;

                skippedAssemblies.Remove(assemblyName);
                yield return file;
            }

            if (skippedAssemblies.Count <= 0)
                yield break;

            if (References == null)
                throw new WeavingException(
                    "To embed references with CopyLocal='false', References is required - you may need to update to the latest version of Fody.");

            var splittedReferences = References.Split(';');

            foreach (var skippedAssembly in skippedAssemblies)
            {
                var fileName = splittedReferences.FirstOrDefault(
                    splittedReference => string.Equals(Path.GetFileNameWithoutExtension(splittedReference), skippedAssembly,
                        StringComparison.InvariantCultureIgnoreCase));

                if (string.IsNullOrEmpty(fileName))
                    LogError($"Assembly '{skippedAssembly}' cannot be found (not even as CopyLocal='false'), please update the configuration");

                yield return fileName;
            }

            yield break;
        }

        if (config.ExcludeAssemblies.Any())
        {
            foreach (var file in onlyBinaries.Except(config.Unmanaged32Assemblies).Except(config.Unmanaged64Assemblies))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(file);

                if (config.ExcludeAssemblies.Any(x => x == assemblyName) ||
                    config.Unmanaged32Assemblies.Any(x => x == assemblyName) ||
                    config.Unmanaged64Assemblies.Any(x => x == assemblyName))
                {
                    continue;
                }
                yield return file;
            }
            yield break;
        }

        if (!config.OptOut)
            yield break;

        foreach (var file in onlyBinaries)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(file);

            if (config.Unmanaged32Assemblies.Any(x => x == assemblyName) &&
                config.Unmanaged64Assemblies.Any(x => x == assemblyName))
                yield return file;
        }
    }

    private string Embed(string prefix, string fullPath, bool compress)
    {
        var resourceName = $"{prefix}{Path.GetFileName(fullPath).ToLowerInvariant()}";
        if (ModuleDefinition.Resources.Any(x => x.Name == resourceName))
        {
            LogInfo($"\tSkipping '{fullPath}' because it is already embedded");
            return resourceName;
        }

        if (compress)
            resourceName = $"{prefix}{Path.GetFileName(fullPath).ToLowerInvariant()}.zip";

        LogInfo($"\tEmbedding '{fullPath}'");

        var checksum = CalculateChecksum(fullPath);
        var cacheFile = Path.Combine(_cachePath, $"{checksum}.{resourceName}");
        var memoryStream = new MemoryStream();

        if (File.Exists(cacheFile))
            using (var fileStream = File.Open(cacheFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                fileStream.CopyTo(memoryStream);
        else
        {
            using (var cacheFileStream = File.Open(cacheFile, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                using (var fileStream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (compress)
                        using (var compressedStream = new DeflateStream(memoryStream, CompressionMode.Compress, true))
                            fileStream.CopyTo(compressedStream);
                    else
                        fileStream.CopyTo(memoryStream);
                }
                memoryStream.Position = 0;
                memoryStream.CopyTo(cacheFileStream);
            }
        }

        memoryStream.Position = 0;

        _streams.Add(memoryStream);

        var resource = new EmbeddedResource(resourceName, ManifestResourceAttributes.Private, memoryStream);
        ModuleDefinition.Resources.Add(resource);

        return resourceName;
    }

    public void Dispose()
    {
        if (_streams == null)
            return;

        foreach (var stream in _streams)
            stream.Dispose();
    }
}