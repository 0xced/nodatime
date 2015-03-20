﻿// Copyright 2015 The Noda Time Authors. All rights reserved.
// Use of this source code is governed by the Apache License 2.0,
// as found in the LICENSE.txt file.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NodaTime.Benchmarks.Analyzer;

namespace NodaTime.Benchmarks.MultiVersionHtml
{
    /// <summary>
    /// Converts a directory of XML files containing "mostly the same" tests run at
    /// multiple versions of Noda Time into an HTML file.
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Usage: NodaTime.Benchmarks.MultiVersionHtml <directory> <output file>");
                return 1;
            }
            try
            {
                ProcessDirectory(args[0], args[1]);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
            return 0;
        }

        private static void ProcessDirectory(string directory, string outputFile)
        {
            var files = Directory.GetFiles(directory, "*.xml").Select(XDocument.Load).Select(BenchmarkFile.FromXDocument).ToList();

            var labels = files.Select(x => x.Label).OrderBy(l => l != "Current").ThenByDescending(l => l).ToList();

            var resultsByType = from file in files
                                from result in file.Results
                                orderby result.Namespace
                                group new {file.Label, Result = result} by result.FullType;

            var table = new XElement("table",
                new XElement("thead", new XElement("th", "Method"), labels.Select(l => new XElement("th", l))),
                new XElement("tbody"));
            var body = table.Element("tbody");
            foreach (var typeGroup in resultsByType)
            {
                body.Add(new XElement("tr", new XElement("td", new XAttribute("class", "method"), new XAttribute("colspan", labels.Count + 1), typeGroup.Key)));
                body.Add(
                    from labelAndResult in typeGroup
                    group labelAndResult by labelAndResult.Result.Method
                    into methodResult
                    where methodResult.Count() != 1 // Ignore methods where we only have a single result, usually for 2.0-only or benchmarks of internal members
                    select new XElement("tr",
                        new XElement("td", methodResult.Key),
                        from label in labels
                        select new XElement("td", new XAttribute("align", "right"), methodResult.SingleOrDefault(r => r.Label == label)?.Result.NanosecondsPerCall.ToString() ?? "-")));
            }

            var html = new XElement("html",
                new XElement("head", new XElement("title", "Multi-version results")),
                new XElement("body", table));
            html.Save(outputFile);
        }
    }
}
