﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GoogleTestAdapter.Common;
using GoogleTestAdapter.DiaResolver;
using GoogleTestAdapter.Helpers;
using GoogleTestAdapter.Model;
using GoogleTestAdapter.Runners;
using GoogleTestAdapter.Settings;

namespace GoogleTestAdapter.TestCases
{

    internal class TestCaseFactory
    {
        private readonly ILogger _logger;
        private readonly SettingsWrapper _settings;
        private readonly string _executable;
        private readonly IDiaResolverFactory _diaResolverFactory;
        private readonly MethodSignatureCreator _signatureCreator = new MethodSignatureCreator();

        public TestCaseFactory(string executable, ILogger logger, SettingsWrapper settings,
            IDiaResolverFactory diaResolverFactory)
        {
            _logger = logger;
            _settings = settings;
            _executable = executable;
            _diaResolverFactory = diaResolverFactory;
        }

        public IList<TestCase> CreateTestCases(Action<TestCase> reportTestCase = null)
        {
            List<string> standardOutput = new List<string>();
            if (_settings.UseNewTestExecutionFramework)
            {
                return NewCreateTestcases(reportTestCase, standardOutput);
            }

            try
            {
                var launcher = new ProcessLauncher(_logger, _settings.GetPathExtension(_executable), null);
                int processExitCode;
                standardOutput = launcher.GetOutputOfCommand("", _executable, GoogleTestConstants.ListTestsOption.Trim(),
                    false, false, out processExitCode);

                if (!CheckProcessExitCode(processExitCode, standardOutput))
                    return new List<TestCase>();
            }
            catch (Exception e)
            {
                SequentialTestRunner.LogExecutionError(_logger, _executable, Path.GetFullPath(""),
                    GoogleTestConstants.ListTestsOption.Trim(), e);
                return new List<TestCase>();
            }

            IList<TestCaseDescriptor> testCaseDescriptors = new ListTestsParser(_settings.TestNameSeparator).ParseListTestsOutput(standardOutput);
            if (_settings.ParseSymbolInformation)
            {
                var testCaseLocations = GetTestCaseLocations(testCaseDescriptors, _settings.GetPathExtension(_executable));
                return testCaseDescriptors.Select(descriptor => CreateTestCase(descriptor, testCaseLocations)).ToList();
            }

            return testCaseDescriptors.Select(CreateTestCase).ToList();
        }

        private IList<TestCase> NewCreateTestcases(Action<TestCase> reportTestCase, List<string> standardOutput)
        {
            var testCases = new List<TestCase>();

            var resolver = new NewTestCaseResolver(
                _executable,
                _settings.GetPathExtension(_executable),
                _diaResolverFactory,
                _settings.ParseSymbolInformation,
                _logger);

            var parser = new StreamingListTestsParser(_settings.TestNameSeparator);
            parser.TestCaseDescriptorCreated += (sender, args) =>
            {
                TestCase testCase;
                if (_settings.ParseSymbolInformation)
                {
                    TestCaseLocation testCaseLocation =
                        resolver.FindTestCaseLocation(
                            _signatureCreator.GetTestMethodSignatures(args.TestCaseDescriptor).ToList());
                    testCase = CreateTestCase(args.TestCaseDescriptor, testCaseLocation);
                }
                else
                {
                    testCase = CreateTestCase(args.TestCaseDescriptor);
                }
                reportTestCase?.Invoke(testCase);
                testCases.Add(testCase);
            };

            Action<string> lineAction = s =>
            {
                standardOutput.Add(s);
                parser.ReportLine(s);
            };

            try
            {
                var executor = new ProcessExecutor(null, _logger);
                int processExitCode = executor.ExecuteCommandBlocking(
                    _executable,
                    GoogleTestConstants.ListTestsOption.Trim(),
                    "",
                    _settings.GetPathExtension(_executable),
                    lineAction);

                if (!CheckProcessExitCode(processExitCode, standardOutput))
                    return new List<TestCase>();
            }
            catch (Exception e)
            {
                SequentialTestRunner.LogExecutionError(_logger, _executable, Path.GetFullPath(""),
                    GoogleTestConstants.ListTestsOption.Trim(), e);
                return new List<TestCase>();
            }
            return testCases;
        }

        private bool CheckProcessExitCode(int processExitCode, ICollection<string> standardOutput)
        {
            if (processExitCode != 0)
            {
                string messsage =
                    $"Could not list test cases of executable '{_executable}': executing process failed with return code {processExitCode}";
                messsage +=
                    $"\nCommand executed: '{_executable} {GoogleTestConstants.ListTestsOption.Trim()}', working directory: '{Path.GetDirectoryName(_executable)}'";
                if (standardOutput.Count(s => !string.IsNullOrEmpty(s)) > 0)
                    messsage += $"\nOutput of command:\n{string.Join("\n", standardOutput)}";
                else
                    messsage += "\nCommand produced no output";

                _logger.LogError(messsage);
                return false;
            }
            return true;
        }

        private Dictionary<string, TestCaseLocation> GetTestCaseLocations(IList<TestCaseDescriptor> testCaseDescriptors, string pathExtension)
        {
            var testMethodSignatures = new HashSet<string>();
            foreach (var descriptor in testCaseDescriptors)
            {
                foreach (var signature in _signatureCreator.GetTestMethodSignatures(descriptor))
                {
                    testMethodSignatures.Add(signature);
                }
            }

            string filterString = "*" + GoogleTestConstants.TestBodySignature;
            var resolver = new TestCaseResolver(_diaResolverFactory, _logger);
            return resolver.ResolveAllTestCases(_executable, testMethodSignatures, filterString, pathExtension);
        }

        private TestCase CreateTestCase(TestCaseDescriptor descriptor)
        {
            var testCase = new TestCase(
                descriptor.FullyQualifiedName, _executable, descriptor.DisplayName, "", 0);
            testCase.Traits.AddRange(GetFinalTraits(descriptor.DisplayName, new List<Trait>()));
            return testCase;
        }

        private TestCase CreateTestCase(TestCaseDescriptor descriptor, Dictionary<string, TestCaseLocation> testCaseLocations)
        {
            var signature = _signatureCreator.GetTestMethodSignatures(descriptor)
                .Select(StripTestSymbolNamespace)
                .FirstOrDefault(s => testCaseLocations.ContainsKey(s));
            TestCaseLocation location = null;
            if (signature != null)
                testCaseLocations.TryGetValue(signature, out location);

            return CreateTestCase(descriptor, location);
        }

        private TestCase CreateTestCase(TestCaseDescriptor descriptor, TestCaseLocation location)
        {
            if (location != null)
            {
                var testCase = new TestCase(
                    descriptor.FullyQualifiedName, _executable, descriptor.DisplayName, location.Sourcefile, (int)location.Line);
                testCase.Traits.AddRange(GetFinalTraits(descriptor.DisplayName, location.Traits));
                return testCase;
            }

            _logger.LogWarning($"Could not find source location for test {descriptor.FullyQualifiedName}");
            return new TestCase(
                descriptor.FullyQualifiedName, _executable, descriptor.DisplayName, "", 0);
        }

        internal static string StripTestSymbolNamespace(string symbol)
        {
            var suffixLength = GoogleTestConstants.TestBodySignature.Length;
            var namespaceEnd = symbol.LastIndexOf("::", symbol.Length - suffixLength - 1);
            var nameStart = namespaceEnd >= 0 ? namespaceEnd + 2 : 0;
            return symbol.Substring(nameStart);
        }

        private IList<Trait> GetFinalTraits(string displayName, List<Trait> traits)
        {
            var afterTraits =
                _settings.TraitsRegexesAfter
                    .Where(p => Regex.IsMatch(displayName, p.Regex))
                    .Select(p => p.Trait)
                    .ToArray();

            var namesOfAfterTraits = afterTraits
                .Select(t => t.Name)
                .Distinct()
                .ToArray();

            var namesOfTestAndAfterTraits = namesOfAfterTraits
                .Union(traits.Select(t => t.Name))
                .Distinct()
                .ToArray();

            var beforeTraits = _settings.TraitsRegexesBefore
                .Where(p =>
                    !namesOfTestAndAfterTraits.Contains(p.Trait.Name)
                    && Regex.IsMatch(displayName, p.Regex))
                .Select(p => p.Trait);

            var testTraits = traits
                .Where(t => !namesOfAfterTraits.Contains(t.Name));

            var finalTraits = new List<Trait>();
            finalTraits.AddRange(beforeTraits);
            finalTraits.AddRange(testTraits);
            finalTraits.AddRange(afterTraits);

            return finalTraits;
        }

    }

}
