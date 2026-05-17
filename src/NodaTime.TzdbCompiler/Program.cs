// Copyright 2009 The Noda Time Authors. All rights reserved.
// Use of this source code is governed by the Apache License 2.0,
// as found in the LICENSE.txt file.

using CommandLine;
using NodaTime.TimeZones;
using NodaTime.TimeZones.Cldr;
using NodaTime.TzdbCompiler.Tzdb;
using NodaTime.Xml;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using NodaTime.Tools.Common;

namespace NodaTime.TzdbCompiler
{
    /// <summary>
    /// Main entry point for the time zone information compiler. In theory we could support
    /// multiple sources and formats but currently we only support one:
    /// https://www.iana.org/time-zones. This system refers to it as TZDB.
    /// This also requires a windowsZone.xml file from the Unicode CLDR repository, to
    /// map Windows time zone names to TZDB IDs.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Runs the compiler from the command line.
        /// </summary>
        /// <param name="arguments">The command line arguments. Each compiler defines its own.</param>
        /// <returns>0 for success, non-0 for error.</returns>
        private static async Task<int> Main(string[] arguments)
        {
            CompilerOptions options = new CompilerOptions();
            ICommandLineParser parser = new CommandLineParser(new CommandLineParserSettings(Console.Error) { MutuallyExclusive = true });
            if (!parser.ParseArguments(arguments, options))
            {
                return 1;
            }

            var tzdbCompiler = new TzdbZoneInfoCompiler();
            var tzdb = await tzdbCompiler.CompileAsync(options.SourceDirectoryName!);
            tzdb.LogCounts();
            if (options.ZoneId != null)
            {
                tzdb.GenerateDateTimeZone(options.ZoneId);
                return 0;
            }
            var windowsZones = await LoadWindowsZonesAsync(options, tzdb.Version);
            if (options.WindowsOverride != null)
            {
                var overrideFile = CldrWindowsZonesParser.Parse(options.WindowsOverride);
                windowsZones = MergeWindowsZones(windowsZones, overrideFile);
            }
            LogWindowsZonesSummary(windowsZones);
            var writer = new TzdbStreamWriter();
            using (var stream = CreateOutputStream(options))
            {
                writer.Write(tzdb, windowsZones, NameIdMappingSupport.StandardNameToIdMap, stream);
            }

            if (options.OutputFileName != null)
            {
                Console.WriteLine("Reading generated data and validating...");
                var source = Read(options);
                source.Validate();
            }

            if (options.XmlSchema is object)
            {
                Console.WriteLine($"Writing XML schema to {options.XmlSchema}");
                var source = Read(options);
                var provider = new DateTimeZoneCache(source);
                XmlSerializationSettings.DateTimeZoneProvider = provider;
                var settings = new XmlWriterSettings { Indent = true, NewLineChars = "\n", Encoding = new UTF8Encoding() };
                using var xmlWriter = XmlWriter.Create(options.XmlSchema, settings);
                XmlSchemaDefinition.NodaTimeXmlSchema.Write(xmlWriter);
            }
            return 0;
        }

        /// <summary>
        /// Loads the best windows zones file based on the options. If the WindowsMapping option is
        /// just a straight file, that's used. If it's a directory, this method loads all the XML files
        /// in the directory (expecting them all to be mapping files) and then picks the best one based
        /// on the version of TZDB we're targeting - basically, the most recent one before or equal to the
        /// target version.
        /// </summary>
        private static async Task<WindowsZones> LoadWindowsZonesAsync(CompilerOptions options, string targetTzdbVersion)
        {
            var mappingPath = options.WindowsMapping;

            if (File.Exists(mappingPath))
            {
                return CldrWindowsZonesParser.Parse(mappingPath);
            }

            if (Directory.Exists(mappingPath))
            {
                return ParseDirectory(mappingPath, targetTzdbVersion);
            }

            if (Uri.TryCreate(mappingPath, UriKind.Absolute, out var zipUri) && Path.GetExtension(zipUri.LocalPath) == ".zip")
            {
                return await ParseZipFileAsync(zipUri);
            }

            using var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Head, "https://unicode.org/Public/cldr/latest/");
            Console.WriteLine($"Retrieving the latest CLDR data from {request.RequestUri}");
            var response = await httpClient.SendAsync(request);
            if (response.Headers.Location != null)
            {
                var cldrVersion = new DirectoryInfo(response.Headers.Location.LocalPath).Name;
                return await ParseZipFileAsync(new Uri(response.Headers.Location, $"cldr-common-{cldrVersion}.zip"));
            }

            throw new Exception($"Expected {request.RequestUri} to redirect to the latest CLDR version, but no Location HTTP header was present in the response.");
        }

        private static async Task<WindowsZones> ParseZipFileAsync(Uri zipUri)
        {
            Console.WriteLine($"Downloading {zipUri}");
            await using var httpStream = await FileUtility.LoadFileOrUrlAsync(zipUri.AbsoluteUri);
            await using var zipArchive = new ZipArchive(httpStream);
            var entries = zipArchive.Entries.Where(e => e.Name == "windowsZones.xml").ToList();
            if (entries.Count != 1)
            {
                throw new Exception($"{zipUri} contains {entries.Count} entries named windowsZones.xml");
            }

            // Buffer in memory because DeflateStream is not seekable
            await using var stream = await entries[0].OpenAsync();
            var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);

            // Write to a local windowsZones.xml file
            memoryStream.Position = 0;
            await using var localFile = CreateWindowsZonesLocalFile(zipUri);
            await memoryStream.CopyToAsync(localFile);

            memoryStream.Position = 0;
            return CldrWindowsZonesParser.Parse(memoryStream, zipUri.LocalPath);
        }

        private static FileStream CreateWindowsZonesLocalFile(Uri zipUri, [CallerFilePath] string path = "")
        {
            var version = Path.GetFileNameWithoutExtension(zipUri.LocalPath).Replace("cldr-common-", "").Replace(".", "-");
            return File.Create(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "data", "cldr", $"windowsZones-{version}.xml"));
        }

        private static WindowsZones ParseDirectory(string mappingPath, string targetTzdbVersion)
        {
            var xmlFiles = Directory.GetFiles(mappingPath, "*.xml");
            if (xmlFiles.Length == 0)
            {
                throw new Exception($"{mappingPath} does not contain any XML files");
            }
            var allFiles = xmlFiles
                // Expect that we've ordered the files so that this gives "most recent first",
                // to handle consecutive CLDR versions that have the same TZDB version.
                .OrderByDescending(file => file, StringComparer.Ordinal)
                .Select(file => (file, zones: CldrWindowsZonesParser.Parse(file)))
                // Note: this is stable, so files with the same TZDB version will stay in reverse filename order.
                .OrderByDescending(pair => pair.zones.TzdbVersion)
                .ToList();

            var versions = string.Join(", ", allFiles.Select(pair => pair.zones.TzdbVersion).ToArray());

            var bestFile = allFiles.FirstOrDefault(pair => StringComparer.Ordinal.Compare(pair.zones.TzdbVersion, targetTzdbVersion) <= 0);

            if (bestFile.zones is null)
            {
                throw new Exception($"No zones files suitable for version {targetTzdbVersion}. Found versions targeting: [{versions}]");
            }
            Console.WriteLine($"Picked Windows Zones from '{Path.GetFileName(bestFile.file)}' with TZDB version {bestFile.zones.TzdbVersion} out of [{versions}] as best match for {targetTzdbVersion}");
            return bestFile.zones;
        }

        private static void LogWindowsZonesSummary(WindowsZones windowsZones)
        {
            Console.WriteLine("Windows Zones:");
            Console.WriteLine($"  Version: {windowsZones.Version}");
            Console.WriteLine($"  TZDB version: {windowsZones.TzdbVersion}");
            Console.WriteLine($"  Windows version: {windowsZones.WindowsVersion}");
            Console.WriteLine($"  {windowsZones.MapZones.Count} MapZones");
            Console.WriteLine($"  {windowsZones.PrimaryMapping.Count} primary mappings");
        }

        private static Stream CreateOutputStream(CompilerOptions options)
        {
            // If we don't have an actual file, just write to an empty stream.
            // That way, while debugging, we still get to see all the data written etc.
            if (options.OutputFileName is null)
            {
                return new MemoryStream();
            }
            string file = Path.ChangeExtension(options.OutputFileName, "nzd");
            return File.Create(file);
        }

        private static TzdbDateTimeZoneSource Read(CompilerOptions options)
        {
            string file = Path.ChangeExtension(options.OutputFileName!, "nzd");
            using (var stream = File.OpenRead(file))
            {
                return TzdbDateTimeZoneSource.FromStream(stream);
            }
        }

        /// <summary>
        /// Merge two WindowsZones objects together. The result has versions present in override,
        /// but falling back to the original for versions absent in the override. The set of MapZones
        /// in the result is the union of those in the original and override, but any ID/Territory
        /// pair present in both results in the override taking priority, unless the override has an
        /// empty "type" entry, in which case the entry is removed entirely.
        ///
        /// While this method could reasonably be in WindowsZones class, it's only needed in
        /// TzdbCompiler - and here is as good a place as any.
        ///
        /// The resulting MapZones will be ordered by Windows ID followed by territory.
        /// </summary>
        /// <param name="windowsZones">The original WindowsZones</param>
        /// <param name="overrideFile">The WindowsZones to override entries in the original</param>
        /// <returns>A merged zones object.</returns>
        internal static WindowsZones MergeWindowsZones(WindowsZones originalZones, WindowsZones overrideZones)
        {
            var version = overrideZones.Version == "" ? originalZones.Version : overrideZones.Version;
            var tzdbVersion = overrideZones.TzdbVersion == "" ? originalZones.TzdbVersion : overrideZones.TzdbVersion;
            var windowsVersion = overrideZones.WindowsVersion == "" ? originalZones.WindowsVersion : overrideZones.WindowsVersion;

            // Work everything out using dictionaries, and then sort.
            var mapZones = originalZones.MapZones.ToDictionary(mz => new { mz.WindowsId, mz.Territory });
            foreach (var overrideMapZone in overrideZones.MapZones)
            {
                var key = new { overrideMapZone.WindowsId, overrideMapZone.Territory };
                if (overrideMapZone.TzdbIds.Count == 0)
                {
                    mapZones.Remove(key);
                }
                else
                {
                    mapZones[key] = overrideMapZone;
                }
            }
            var mapZoneList = mapZones
                .OrderBy(pair => pair.Key.WindowsId)
                .ThenBy(pair => pair.Key.Territory)
                .Select(pair => pair.Value)
                .ToList();
            return new WindowsZones(version, tzdbVersion, windowsVersion, mapZoneList);
        }
    }
}
