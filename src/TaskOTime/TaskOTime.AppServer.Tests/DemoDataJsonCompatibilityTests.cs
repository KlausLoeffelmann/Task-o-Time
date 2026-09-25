using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.Cli;

namespace TaskOTime.AppServer.Tests
{
    [TestClass]
    public class DemoDataJsonCompatibilityTests
    {
        [TestMethod]
        public void ScalarConversionsRetainLegacyRoundingDefaultsAndCaseSensitivity()
        {
            Assert.AreEqual(2, DemoDataConfigLoader.LoadJson("{\"projects\":2.5}").Projects);
            Assert.AreEqual(4, DemoDataConfigLoader.LoadJson("{\"projects\":3.5}").Projects);
            Assert.AreEqual(3, DemoDataConfigLoader.LoadJson("{\"projects\":2.5000000000000001}").Projects);
            Assert.AreEqual(2, DemoDataConfigLoader.LoadJson("{\"projects\":2.5000000000000001e0}").Projects);
            Assert.AreEqual(7, DemoDataConfigLoader.LoadJson("{\"projects\":\"7\"}").Projects);
            Assert.AreEqual(1, DemoDataConfigLoader.LoadJson("{\"tenants\":true}").Tenants);
            Assert.AreEqual(5, DemoDataConfigLoader.LoadJson("{\"Projects\":7,\"projects\":null}").Projects);
            Assert.AreEqual(8, DemoDataConfigLoader.LoadJson("{\"projects\":2,\"projects\":8}").Projects);
            Assert.AreEqual(6, DemoDataConfigLoader.LoadJson("{'projects':6}").Projects);
            Assert.AreEqual(6, DemoDataConfigLoader.LoadJson("{projects:6}").Projects);
            Assert.AreEqual(10, DemoDataConfigLoader.LoadJson("{\r\n\"projects\":1e1\r\n}").Projects);
            Assert.ThrowsException<OverflowException>(() => DemoDataConfigLoader.LoadJson("{\"projects\":2147483648}"));
            Assert.ThrowsException<OverflowException>(() => DemoDataConfigLoader.LoadJson("{\"projects\":NaN}"));
        }

        [TestMethod]
        public void NestedArraysAndStringsKeepTheirExistingShapes()
        {
            var options = DemoDataConfigLoader.LoadJson(
                "{\"users\":[{\"handle\":\"A/*not-comment*/,}\",\"password\":\"secret\",\"isAdmin\":\"true\"}]," +
                "\"timeItemDates\":{\"days\":\"30\",\"includeToday\":\"true\"},\"text\":{\"style\":\"Mixed\"}}");
            Assert.AreEqual("A/*not-comment*/,}", options.Users[0].Handle);
            Assert.IsTrue(options.Users[0].IsAdmin);
            Assert.AreEqual(30, options.TimeItemDates.Days);
            Assert.IsTrue(options.TimeItemDates.IncludeToday);
            Assert.AreEqual(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString(),
                DemoDataConfigLoader.LoadJson("{\"users\":[{\"firstName\":\"\\/Date(0)\\/\"}]}").Users[0].FirstName);
            Assert.AreEqual("/Date(0)/",
                DemoDataConfigLoader.LoadJson("{\"users\":[{\"firstName\":\"/Date(0)/\"}]}").Users[0].FirstName);
            Assert.AreEqual("2026-01-01",
                DemoDataConfigLoader.LoadJson("{\"users\":[{\"firstName\":\"2026-01-01\"}]}").Users[0].FirstName);
            Assert.ThrowsException<InvalidOperationException>(() => DemoDataConfigLoader.LoadJson("{\"users\":{}}"));
            Assert.ThrowsException<InvalidOperationException>(() => DemoDataConfigLoader.LoadJson("{\"users\":[1]}"));
            Assert.ThrowsException<InvalidOperationException>(() => DemoDataConfigLoader.LoadJson("{\"tasks\":[]}"));
        }

        [TestMethod]
        public void InvalidSyntaxRootShapeAndLimitsStillFail()
        {
            foreach (var json in new[] { "{", "{\"projects\":1,}", "{/*comment*/\"projects\":1}", "{} {}", "{\"projects\":1e400}", "{\"projects\":undefined}" })
                Assert.ThrowsException<ArgumentException>(() => DemoDataConfigLoader.LoadJson(json), json);
            foreach (var json in new[] { "[]", "null", "1", "\"text\"", "", " " })
                Assert.ThrowsException<InvalidOperationException>(() => DemoDataConfigLoader.LoadJson(json), json);
            Assert.ThrowsException<ArgumentNullException>(() => DemoDataConfigLoader.LoadJson(null));
            Assert.ThrowsException<ArgumentException>(() => DemoDataConfigLoader.LoadJson(new string(' ', 2097153)));
        }
    }
}
