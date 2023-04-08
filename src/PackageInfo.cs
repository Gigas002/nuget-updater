using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace NugetUpdater
{
    public class PackageInfo
    {
        #region Properties
        
        public string Id { get; }

        public NuGetVersion Version { get; }
        
        public bool IsPrerelease { get; }
        
        public string FileName { get; }
        
        private static SourceRepository Source => Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        
        private static SourceCacheContext Cache => new();
        
        private static CancellationToken Token => CancellationToken.None;
        
        private static ILogger Logger => NullLogger.Instance;
        
        public static HashSet<PackageInfo> PackagesToDownload { get; } = new();

        #endregion

        #region Ctors

        public PackageInfo(string packagePath)
        {
            using FileStream inputStream = File.OpenRead(packagePath);
            using PackageArchiveReader reader = new(inputStream);
            NuspecReader nuspec = reader.NuspecReader;

            Id = nuspec.GetId();
            Version = nuspec.GetVersion();
            IsPrerelease = Version.IsPrerelease;
            FileName = Path.GetFileName(packagePath);
        }

        public PackageInfo(string id, NuGetVersion version)
        {
            Id = id;
            Version = version;
            IsPrerelease = Version.IsPrerelease;
            FileName = Path.GetFileName($"{Id}.{Version}.nupkg");
        }

        private PackageInfo(PackageDependency package)
        {
            Id = package.Id;

            Version = package.VersionRange.HasUpperBound
                ? package.VersionRange.MaxVersion
                : package.VersionRange.MinVersion;
            
            IsPrerelease = Version.IsPrerelease;
            
            FileName = Path.GetFileName($"{Id}.{Version}.nupkg");
        }
        
        #endregion

        #region Methods
        
        public async Task DownloadPackage(string outPath)
        {
            FindPackageByIdResource resource = Source.GetResource<FindPackageByIdResource>();

            var fi = GetFileInfo(outPath);
            if (fi.Exists) return;
            
            await using var stream = fi.OpenWrite();
            await resource.CopyNupkgToStreamAsync(Id, Version, stream, Cache, Logger, Token).ConfigureAwait(false);
        }

        private IEnumerable<PackageDependencyGroup> GetDependencyGroups()
        {
            FindPackageByIdResource resource = Source.GetResource<FindPackageByIdResource>();
            FindPackageByIdDependencyInfo depInfo = resource?.GetDependencyInfoAsync(Id, Version, Cache, Logger, Token)?.Result;
            
            return depInfo?.DependencyGroups;
        }

        public static IEnumerable<PackageInfo> GetDependencies(IEnumerable<PackageDependencyGroup> dependencyGroups)
        {
            if (dependencyGroups == null) yield break;
            
            foreach (var dependencyGroup in dependencyGroups)
            {
                foreach (var package in dependencyGroup.Packages)
                {
                    yield return new PackageInfo(package);
                }
            }
        }

        private FileInfo GetFileInfo(string outPath) => new(Path.Combine(outPath, FileName));

        public async Task<PackageInfo> GetUpdatedPackageInfo(bool includePrerelease)
         {
             FindPackageByIdResource resource = await Source.GetResourceAsync<FindPackageByIdResource>(Token).ConfigureAwait(false);
             
             HashSet<NuGetVersion> versions = (await resource.GetAllVersionsAsync(Id, Cache, Logger, Token).ConfigureAwait(false))?.ToHashSet();

             // Get max non-prereleasse version
             NuGetVersion max = versions?.Where(v => !v.IsPrerelease).Max();

             // Get max prerelease version
             NuGetVersion maxPrerelease = versions?.Where(v => v.IsPrerelease).Max();

             // Compare with prerelease and set to the latest
             NuGetVersion latest = includePrerelease ? max > maxPrerelease ? max : maxPrerelease : max;
             
             return Version < latest ? new PackageInfo(Id, latest) : null;
         }
        
        public static IEnumerable<PackageInfo> GetPackages(string sourcePath)
         {
             var packages = new HashSet<PackageInfo>();

             foreach (var file in Directory.EnumerateFiles(sourcePath, "*.nupkg", SearchOption.TopDirectoryOnly))
             {
                 var pi = new PackageInfo(file);
                 
                 // if (pi.IsPrerelease && !includePrerelease) continue;

                 var pack = packages.FirstOrDefault(p => p.Id == pi.Id);

                 // If package exists in collection
                 if (pack != null)
                 {
                     // And it's version if higher already, then skip
                     if (pack.Version >= pi.Version) continue;

                     // Else remove
                     packages.Remove(pack);
                 }

                 // And add to the collection
                 packages.Add(pi);
             }

             return packages;
         }

        public static async Task AddAll(string source, bool includePrerelease)
        {
            IEnumerable<PackageInfo> packages = GetPackages(source);

            var newPackages = new HashSet<PackageInfo>();

            foreach (var packageInfo in packages)
            {
                var newPackage = await packageInfo.GetUpdatedPackageInfo(includePrerelease).ConfigureAwait(false);

                if (newPackage != null) newPackages.Add(newPackage);
            }

            foreach (var package in newPackages)
            {
                AddRecursively(package);
            }
        }

        public IEnumerable<PackageInfo> GetDependencies()
        {
            IEnumerable<PackageDependencyGroup> groups = GetDependencyGroups();
            
            // Don't download unneeded frameworks
            var gnet = groups?.Where(g => g.TargetFramework == NuGetFramework.AgnosticFramework ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard10 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard11 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard12 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard13 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard14 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard15 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard16 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard20 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard21 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetStandard ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetCoreApp10 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetCoreApp11 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetCoreApp20 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetCoreApp21 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetCoreApp22 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetCoreApp30 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.NetCoreApp31 ||
                                          g.TargetFramework == FrameworkConstants.CommonFrameworks.Net50);
            
            return GetDependencies(gnet);
        }

        private static void AddRecursively(PackageInfo pi)
        {
            if (TryAddPackageToDownload(pi)) return;
            
            IEnumerable<PackageInfo> deps = pi.GetDependencies();
            
            if (deps == null) return;

            foreach (var packageInfo in deps)
            {
                AddRecursively(packageInfo);
            }
        }

        private static bool TryAddPackageToDownload(PackageInfo pi)
        {
            if (pi == null) return true;
            
            // check if package is already exists in collection to download
            var existing = PackagesToDownload.FirstOrDefault(p => p.Id == pi.Id && p.Version == pi.Version);

            // if already exists
            if (existing != null) return true;

            PackagesToDownload.Add(pi);

            return false;
        }
        
        #endregion
    }
}
