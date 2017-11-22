// This file has been modified by Microsoft on 7/2017.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.DiaResolver;
using GoogleTestAdapter.Helpers;
using GoogleTestAdapter.Model;

namespace GoogleTestAdapter.TestCases
{

    public class TestCaseResolver
    {
        // see GTA_Traits.h
        private const string TraitAppendix = "_GTA_TRAIT";

        private readonly IDiaResolverFactory _diaResolverFactory;
        private readonly ILogger _logger;

        public TestCaseResolver(IDiaResolverFactory diaResolverFactory, ILogger logger)
        {
            _diaResolverFactory = diaResolverFactory;
            _logger = logger;
        }

        public IDictionary<string, TestCaseLocation> ResolveAllTestCases(string executable, HashSet<string> testMethodSignatures, string symbolFilterString, string pathExtension, IEnumerable<string> additionalPdbs)
        {
            var testCaseLocationsFound = FindTestCaseLocationsInBinary(executable, testMethodSignatures, symbolFilterString, pathExtension);
            if (testCaseLocationsFound.Count == 0)
            {
                testCaseLocationsFound.AddRange(ResolveTestCasesFromAdditionalPdbs(executable, additionalPdbs, testMethodSignatures, symbolFilterString));
                testCaseLocationsFound.AddRange(ResolveTestCasesFromImports(executable, pathExtension, testMethodSignatures, symbolFilterString));
            }
            return testCaseLocationsFound;
        }

        private IDictionary<string, TestCaseLocation> FindTestCaseLocationsInBinary(
            string binary, HashSet<string> testMethodSignatures, string symbolFilterString, string pathExtension)
        {
            string pdb = PdbLocator.FindPdbFile(binary, pathExtension, _logger);
            if (pdb == null)
            {
                _logger.LogWarning($"Couldn't find the .pdb file of file '{binary}'. You might not get source locations for some or all of your tests.");
                return new Dictionary<string, TestCaseLocation>();
            }

            return FindTestCaseLocations(binary, pdb, testMethodSignatures, symbolFilterString);
        }

        private IDictionary<string, TestCaseLocation> ResolveTestCasesFromAdditionalPdbs(string executable,
            IEnumerable<string> additionalPdbs, HashSet<string> testMethodSignatures, string symbolFilterString)
        {
            var testCaseLocationsFound = new Dictionary<string, TestCaseLocation>();
            foreach (string pattern in additionalPdbs)
            {
                bool anyPdbFileFound = false;
                var filesConsidered = new List<FileSystemInfo>();
                foreach (var pdbCandidate in Utils.GetMatchingFiles(pattern, _logger))
                {
                    if (pdbCandidate.Attributes.HasFlag(FileAttributes.Directory))
                    {
                        continue;
                    }

                    filesConsidered.Add(pdbCandidate);
                    var testCaseLocations = FindTestCaseLocations(executable, pdbCandidate.FullName, testMethodSignatures, symbolFilterString);
                    if (testCaseLocations.Any())
                    {
                        anyPdbFileFound = true;
                    }
                    testCaseLocationsFound.AddRange(testCaseLocations);
                }
                if (!anyPdbFileFound)
                {
                    string message = $"No test case locations found for additional PDB pattern {pattern}. ";
                    if (filesConsidered.Any())
                    {
                        message += $"Files considered: {string.Join(", ", filesConsidered.Select(fsi => fsi.FullName))}";
                    }
                    else
                    {
                        message += "No files matched the pattern.";
                    }
                    _logger.DebugWarning(message);
                }
            }
            return testCaseLocationsFound;
        }

        private IDictionary<string, TestCaseLocation> ResolveTestCasesFromImports(string executable, string pathExtension,
            HashSet<string> testMethodSignatures, string symbolFilterString)
        {
            var testCaseLocationsFound = new Dictionary<string, TestCaseLocation>();
            List<string> imports = PeParser.ParseImports(executable, _logger);

            string moduleDirectory = Path.GetDirectoryName(executable);

            foreach (string import in imports)
            {
                // ReSharper disable once AssignNullToNotNullAttribute
                string importedBinary = Path.Combine(moduleDirectory, import);
                if (File.Exists(importedBinary))
                {
                    foreach (var testCaseLocation in FindTestCaseLocationsInBinary(importedBinary, testMethodSignatures,
                        symbolFilterString, pathExtension))
                    {
                        testCaseLocationsFound.Add(testCaseLocation.Key, testCaseLocation.Value);
                    }
                }
            }
            return testCaseLocationsFound;
        }

        private Dictionary<string, TestCaseLocation> FindTestCaseLocations(string binary, string pdb, HashSet<string> testMethodSignatures,
            string symbolFilterString)
        {
            using (IDiaResolver diaResolver = _diaResolverFactory.Create(binary, pdb, _logger))
            {
                try
                {
                    IList<SourceFileLocation> allTestMethodSymbols = diaResolver.GetFunctions(symbolFilterString);
                    IList<SourceFileLocation> allTraitSymbols = diaResolver.GetFunctions("*" + TraitAppendix);
                    _logger.DebugInfo(
                        $"Found {allTestMethodSymbols.Count} test method symbols and {allTraitSymbols.Count} trait symbols in binary {binary}");

                    return allTestMethodSymbols
                        .Where(nsfl => testMethodSignatures.Contains(TestCaseFactory.StripTestSymbolNamespace(nsfl.Symbol)))
                        .Select(nsfl => ToTestCaseLocation(nsfl, allTraitSymbols))
                        .ToDictionary(nsfl => TestCaseFactory.StripTestSymbolNamespace(nsfl.Symbol));
                }
                catch (Exception e)
                {
                    _logger.DebugError($"Exception while resolving test locations and traits in {binary}\n{e}");
                    return new Dictionary<string, TestCaseLocation>();
                }
            }
        }

        private TestCaseLocation ToTestCaseLocation(SourceFileLocation location, IEnumerable<SourceFileLocation> allTraitSymbols)
        {
            List<Trait> traits = NewTestCaseResolver.GetTraits(location, allTraitSymbols);
            var testCaseLocation = new TestCaseLocation(location.Symbol, location.Sourcefile, location.Line);
            testCaseLocation.Traits.AddRange(traits);
            return testCaseLocation;
        }

    }

}
