#pragma warning disable CA1031 // Do not catch general exception types
#pragma warning disable CS0168 // Unused variable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;

namespace NugetUpdater
{
    public static class Program
    {
        // ReSharper disable once UnusedMember.Local
        private static void DeleteDuplicates(string path)
        {
            var files = new HashSet<string>();
            foreach (var file in Directory.EnumerateFiles(path, "*.nupkg"))
            {
                var fi = files.FirstOrDefault(f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(fi))
                {
                    File.Delete(file);
                }
                else files.Add(file);
            }
        }

        private static Arguments Args { get; set; }
        
        private static bool IsParsingErrors { get; set; }

        public static async Task Main(string[] args)
        {
            try
            {
                Parser.Default.ParseArguments<Arguments>(args).WithParsed(ParseArguments)
                    .WithNotParsed(_ => IsParsingErrors = true);
            }
            catch (Exception _)
            {
                return;
            }

            if (IsParsingErrors)
            {
                return;
            }
            
            // const string source = "/home/user/nupkgs";
            // // string source = "/home/user/Документы/Work/NugetUpdater/in";
            // const bool includePrerelease = false;

            // DeleteDuplicates(Args.StoragePath);
            
            await PackageInfo.AddAll(Args.StoragePath, Args.IsIncludePrerelease).ConfigureAwait(false);

            foreach (var packageInfo in PackageInfo.PackagesToDownload)
            {
                await packageInfo.DownloadPackage(Args.StoragePath).ConfigureAwait(false);
            }
        }

        private static void ParseArguments(Arguments args)
        {
            Args = args;
        }
    }
}