// <copyright file="ProgramTest.cs">Copyright ©  2014</copyright>
using System;
using Microsoft.Pex.Framework;
using Microsoft.Pex.Framework.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using R_Monitor;

namespace R_Monitor.Tests
{
    /// <summary>This class contains parameterized unit tests for Program</summary>
    [PexClass(typeof(Program))]
    [PexAllowedExceptionFromTypeUnderTest(typeof(InvalidOperationException))]
    [PexAllowedExceptionFromTypeUnderTest(typeof(ArgumentException), AcceptExceptionSubtypes = true)]
    [TestClass]
    public partial class ProgramTest
    {
        [TestMethod]
        public void HasRow_should_return_true_when_empty()
        {
            var actual = Program.GetHasRow("Data Source=R-Monitor;Initial Catalog=R-Monitor;user id=R-Monitor;password=R-Monitor,select top 10 * from RMonitor");
            var expected = true;
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void HasRow_should_return_true_when_value_is_true()
        {
            var actual = Program.GetHasRow("Data Source=R-Monitor;Initial Catalog=R-Monitor;user id=R-Monitor;password=R-Monitor,select top 10 * from RMonitor,HasRows=True");
            var expected = true;
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void HasRow_should_return_true_when_value_is_false()
        {
            var actual = Program.GetHasRow("Data Source=R-Monitor;Initial Catalog=R-Monitor;user id=R-Monitor;password=R-Monitor,select top 10 * from RMonitor,HasRows=False");
            var expected = false;
            Assert.AreEqual(expected, actual);
        }
    }
}
