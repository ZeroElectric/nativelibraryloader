﻿using Microsoft.Extensions.DependencyModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace NativeLibraryLoader;

/// <summary>
/// Enumerates possible library load targets.
/// </summary>
public abstract class PathResolver
{
    /// <summary>
    /// Returns an enumerator which yields possible library load targets, in priority order.
    /// </summary>
    /// <param name="name">The name of the library to load.</param>
    /// <returns>An enumerator yielding load targets.</returns>
    public abstract IEnumerable<string> EnumeratePossibleLibraryLoadTargets(string name);

    /// <summary>
    /// Gets a default path resolver.
    /// </summary>
    public static PathResolver Default { get; } = new DefaultPathResolver();
}

/// <summary>
/// Enumerates possible library load targets. This default implementation returns the following load targets:
/// First: The library contained in the applications base folder.
/// Second: The simple name, unchanged.
/// Third: The library as resolved via the default DependencyContext, in the default nuget package cache folder.
/// </summary>
public class DefaultPathResolver : PathResolver
{
    /// <summary>
    /// Returns an enumerator which yields possible library load targets, in priority order.
    /// </summary>
    /// <param name="name">The name of the library to load.</param>
    /// <returns>An enumerator yielding load targets.</returns>
    public override IEnumerable<string> EnumeratePossibleLibraryLoadTargets(string name)
    {
        if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
        {
            yield return Path.Combine(AppContext.BaseDirectory, name);
        }
        yield return name;
        if (TryLocateNativeAssetFromDeps(name, out string appLocalNativePath, out string depsResolvedPath))
        {
            yield return appLocalNativePath;
            yield return depsResolvedPath;
        }
    }

    private static bool TryLocateNativeAssetFromDeps(string name, out string appLocalNativePath, out string depsResolvedPath)
    {
        DependencyContext defaultContext = DependencyContext.Default;
     
        if (defaultContext is null)
        {
            appLocalNativePath = null;
            depsResolvedPath = null;
            return false;
        }

        List<string> allRIDs = [];

        string current_RID_OS = null;

        string current_RID_Arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => throw new NotSupportedException(),
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            current_RID_OS = "win";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            current_RID_OS = "linux";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            current_RID_OS = "osx";
        }

        string currentRID = $"{current_RID_OS}-{current_RID_Arch}";

        allRIDs.Add(currentRID);

        if (!AddFallbacks(allRIDs, currentRID, defaultContext.RuntimeGraph))
        {
            string guessedFallbackRID = GuessFallbackRID(currentRID);
            if (guessedFallbackRID is null)
            {
                allRIDs.Add(guessedFallbackRID);
                AddFallbacks(allRIDs, guessedFallbackRID, defaultContext.RuntimeGraph);
            }
        }

        foreach (string rid in allRIDs)
        {
            foreach (var runtimeLib in defaultContext.RuntimeLibraries)
            {
                foreach (var nativeAsset in runtimeLib.GetRuntimeNativeAssets(defaultContext, rid))
                {
                    if (Path.GetFileName(nativeAsset) == name || Path.GetFileNameWithoutExtension(nativeAsset) == name)
                    {
                        appLocalNativePath = Path.Combine(
                            AppContext.BaseDirectory,
                            nativeAsset);
                        appLocalNativePath = Path.GetFullPath(appLocalNativePath);

                        depsResolvedPath = Path.Combine(
                            GetNugetPackagesRootDirectory(),
                            runtimeLib.Name.ToLowerInvariant(),
                            runtimeLib.Version,
                            nativeAsset);
                        depsResolvedPath = Path.GetFullPath(depsResolvedPath);

                        return true;
                    }
                }
            }
        }

        appLocalNativePath = null;
        depsResolvedPath = null;
        return false;
    }

    private static string GuessFallbackRID(string actualRuntimeIdentifier)
    {
        if (actualRuntimeIdentifier == "osx.10.13-x64")
        {
            return "osx.10.12-x64";
        }
        else if (actualRuntimeIdentifier.StartsWith("osx"))
        {
            return "osx-x64";
        }

        return null;
    }

    private static bool AddFallbacks(List<string> fallbacks, string rid, IReadOnlyList<RuntimeFallbacks> allFallbacks)
    {
        foreach (RuntimeFallbacks fb in allFallbacks)
        {
            if (fb.Runtime == rid)
            {
                fallbacks.AddRange(fb.Fallbacks);
                return true;
            }
        }

        return false;
    }

    // TODO: Handle alternative package directories, if they are configured.
    private static string GetNugetPackagesRootDirectory() =>
        Path.Combine(GetUserDirectory(), ".nuget", "packages");

    private static string GetUserDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Environment.GetEnvironmentVariable("USERPROFILE");
        }
        else
        {
            return Environment.GetEnvironmentVariable("HOME");
        }
    }
}
