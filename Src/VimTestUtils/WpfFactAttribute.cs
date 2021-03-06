﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Extensions;
using Xunit.Sdk;

namespace Vim.UnitTest
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("Vim.UnitTest.Utilities.WpfFactDiscoverer", "Vim.UnitTest.Utils")]
    public class WpfFactAttribute : FactAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("Vim.UnitTest.Utilities.WpfTheoryDiscoverer", "Vim.UnitTest.Utils")]
    public class WpfTheoryAttribute : TheoryAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    [XunitTestCaseDiscoverer("Vim.UnitTest.Utilities.WpfFactDiscoverer", "Vim.UnitTest.Utils")]
    public class ConditionalWpfFactAttribute : FactAttribute
    {
        public bool Condition { get; }

        public override string Skip => Condition
            ? null
            : "Test not valid in this configuration";

        public ConditionalWpfFactAttribute(bool condition)
        {
            Condition = condition;
        }
    }
}
