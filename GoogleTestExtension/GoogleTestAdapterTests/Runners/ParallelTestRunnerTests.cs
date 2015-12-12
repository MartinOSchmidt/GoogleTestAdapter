﻿using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GoogleTestAdapter.Helpers;
using System.Collections.Generic;
using GoogleTestAdapterVSIX.TestFrameworkIntegration;

namespace GoogleTestAdapter.Runners
{
    [TestClass]
    public class ParallelTestRunnerTests : AbstractGoogleTestExtensionTests
    {

        [TestMethod]
        public void ParallelTestExecutionSpeedsUpTestExecution()
        {
            Stopwatch stopwatch = new Stopwatch();
            GoogleTestExecutor executor = new GoogleTestExecutor(TestEnvironment);
            IEnumerable<string> testsToRun = SampleTests.Yield();
            stopwatch.Start();
            executor.RunTests(testsToRun, MockRunContext.Object, MockFrameworkHandle.Object);
            stopwatch.Stop();
            long sequentialDuration = stopwatch.ElapsedMilliseconds;

            MockOptions.Setup(o => o.ParallelTestExecution).Returns(true);
            MockOptions.Setup(o => o.MaxNrOfThreads).Returns(Environment.ProcessorCount);

            executor = new GoogleTestExecutor(TestEnvironment);
            testsToRun = SampleTests.Yield();
            stopwatch.Restart();
            executor.RunTests(testsToRun, MockRunContext.Object, MockFrameworkHandle.Object);
            stopwatch.Stop();
            long parallelDuration = stopwatch.ElapsedMilliseconds;

            Assert.IsTrue(sequentialDuration > 4000, sequentialDuration.ToString()); // 2 long tests, 2 seconds per test
            Assert.IsTrue(parallelDuration > 2000, parallelDuration.ToString());
            Assert.IsTrue(parallelDuration < 3500, parallelDuration.ToString()); // 2 seconds per long test + some time for the rest
        }

    }

}