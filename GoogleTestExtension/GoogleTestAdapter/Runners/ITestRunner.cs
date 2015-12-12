﻿using System.Collections.Generic;
using GoogleTestAdapter.Model;

namespace GoogleTestAdapter.Runners
{
    public interface ITestRunner
    {
        // TODO remove isBeingDebugged parameter (use debuggedLauncher != null)
        void RunTests(IEnumerable<TestCase> allTestCases, IEnumerable<TestCase> testCasesToRun,
            string userParameters, bool isBeingDebugged, IDebuggedProcessLauncher debuggedLauncher);

        void Cancel();
    }
}