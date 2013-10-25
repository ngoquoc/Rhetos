﻿/*
    Copyright (C) 2013 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos.Utilities;
using Rhetos.TestCommon;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace CommonConcepts.Test
{
    [TestClass]
    public class LoggingTest
    {
        [TestMethod]
        public void DeleteIntegerStringDataTime()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                var repository = new Common.DomRepository(executionContext);

                executionContext.SqlExecuter.ExecuteSql(new[]
                    {
                        "DELETE FROM Common.Log;"
                    });

                var newItem = new TestLogging.Simple { ID = Guid.NewGuid(), Count = -2, Name = "abc", Created = DateTime.Now };
                repository.TestLogging.Simple.Insert(new[] { newItem });

                var logRecord = repository.Common.Log.Query().Where(log => log.ItemId == newItem.ID && log.Action == "Insert").SingleOrDefault();
                Assert.IsNotNull(logRecord, "There should be 'Insert' record in the log.");
                Assert.AreEqual("", logRecord.Description);

                repository.TestLogging.Simple.Delete(new[] { newItem });

                logRecord = repository.Common.Log.Query().Where(log => log.ItemId == newItem.ID && log.Action == "Delete").SingleOrDefault();
                Assert.IsNotNull(logRecord, "There should be 'Delete' record in the log.");

                Assert.AreEqual(SqlUtility.UserContextInfoText(executionContext.UserInfo), logRecord.ContextInfo);
                Assert.IsTrue(executionContext.UserInfo.IsUserRecognized);
                TestUtility.AssertContains(logRecord.ContextInfo, executionContext.UserInfo.UserName);
                TestUtility.AssertContains(logRecord.ContextInfo, executionContext.UserInfo.Workstation);

                var now = MsSqlUtility.GetDatabaseTime(executionContext.SqlExecuter);
                Assert.IsTrue(logRecord.Created.Value.Subtract(now).TotalSeconds < 5);

                Assert.AreEqual("TestLogging.Simple", logRecord.TableName);

                Assert.IsTrue(!string.IsNullOrWhiteSpace(logRecord.UserName));
                Assert.IsTrue(!string.IsNullOrWhiteSpace(logRecord.Workstation));

                // Description is XML:
                var xmlText = @"<?xml version=""1.0"" encoding=""UTF-16""?>" + Environment.NewLine + logRecord.Description;
                Console.WriteLine(xmlText);
                var xdoc = XDocument.Parse(xmlText);
                Console.WriteLine(string.Join(", ", xdoc.Root.Attributes().Select(a => a.Name + ":" + a.Value)));

                var logCount = int.Parse(xdoc.Root.Attribute("Count").Value);
                Assert.AreEqual(newItem.Count, logCount);

                var logName = xdoc.Root.Attribute("Name").Value;
                Assert.AreEqual(newItem.Name, logName);

                var logCreated = DateTime.Parse(xdoc.Root.Attribute("Created").Value);
                Assert.IsTrue(Math.Abs(newItem.Created.Value.Subtract(logCreated).TotalMilliseconds) <= 1000, "Error made by converting DataTime to XML should be less than a second.");
            }
        }

        [TestMethod]
        public void SpecialCharacters()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                var repository = new Common.DomRepository(executionContext);

                executionContext.SqlExecuter.ExecuteSql(new[]
                    {
                        "DELETE FROM Common.Log;"
                    });

                var newItem = new TestLogging.Simple { ID = Guid.NewGuid(), Name = @"<>'""&;[]\\//()čćšđžČĆŠĐŽ]]>" };
                repository.TestLogging.Simple.Insert(new[] { newItem });

                var logRecord = repository.Common.Log.Query().Where(log => log.ItemId == newItem.ID && log.Action == "Insert").SingleOrDefault();
                Assert.IsNotNull(logRecord, "There should be an 'Insert' record in the log.");
                Assert.AreEqual("", logRecord.Description);

                repository.TestLogging.Simple.Delete(new[] { newItem });

                logRecord = repository.Common.Log.Query().Where(log => log.ItemId == newItem.ID && log.Action == "Delete").SingleOrDefault();
                Assert.IsNotNull(logRecord, "There should be a 'Delete' record in the log.");

                var xmlText = @"<?xml version=""1.0"" encoding=""UTF-16""?>" + Environment.NewLine + logRecord.Description;
                Console.WriteLine(xmlText);
                var xdoc = XDocument.Parse(xmlText);
                Console.WriteLine(string.Join(", ", xdoc.Root.Attributes().Select(a => a.Name + ":" + a.Value)));

                var logName = xdoc.Root.Attribute("Name").Value;
                Assert.AreEqual(newItem.Name, logName);
            }
        }

        [TestMethod]
        public void UpdatedOldNullValues()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                var repository = new Common.DomRepository(executionContext);

                executionContext.SqlExecuter.ExecuteSql(new[]
                    {
                        "DELETE FROM Common.Log;"
                    });

                var newItem = new TestLogging.Simple { ID = Guid.NewGuid(), Count = null, Name = null, Created = null };
                repository.TestLogging.Simple.Insert(new[] { newItem });

                var logRecord = repository.Common.Log.Query().Where(log => log.ItemId == newItem.ID && log.Action == "Insert").SingleOrDefault();
                Assert.IsNotNull(logRecord, "There should be an 'Insert' record in the log.");
                Assert.AreEqual("", logRecord.Description);

                newItem.Count = 2;
                newItem.Name = "abc";
                newItem.Created = DateTime.Now;
                repository.TestLogging.Simple.Update(new[] { newItem });

                logRecord = repository.Common.Log.Query().Where(log => log.ItemId == newItem.ID && log.Action == "Update").SingleOrDefault();
                Assert.IsNotNull(logRecord, "There should be an 'Update' record in the log.");

                var xmlText = @"<?xml version=""1.0"" encoding=""UTF-16""?>" + Environment.NewLine + logRecord.Description;
                Console.WriteLine(xmlText);
                var xdoc = XDocument.Parse(xmlText);
                Console.WriteLine(string.Join(", ", xdoc.Root.Attributes().Select(a => a.Name + ":" + a.Value)));

                Assert.IsTrue(IsNullOrEmpty(xdoc.Root.Attribute("Count")));
                Assert.IsTrue(IsNullOrEmpty(xdoc.Root.Attribute("Name")));
                Assert.IsTrue(IsNullOrEmpty(xdoc.Root.Attribute("Created")));
            }
        }

        private static bool IsNullOrEmpty(XAttribute attribute)
        {
            // Attributes in XML cannot store null values
            return attribute == null || attribute.Value == "";
        }

        [TestMethod]
        public void DeleteOldNullValues()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                var repository = new Common.DomRepository(executionContext);

                executionContext.SqlExecuter.ExecuteSql(new[]
                    {
                        "DELETE FROM Common.Log;"
                    });

                var newItem = new TestLogging.Simple { ID = Guid.NewGuid(), Count = null, Name = null, Created = null };
                repository.TestLogging.Simple.Insert(new[] { newItem });

                var logRecord = repository.Common.Log.Query().Where(log => log.ItemId == newItem.ID && log.Action == "Insert").SingleOrDefault();
                Assert.IsNotNull(logRecord, "There should be an 'Insert' record in the log.");
                Assert.AreEqual("", logRecord.Description);

                newItem.Count = 2;
                newItem.Name = "abc";
                newItem.Created = DateTime.Now;
                repository.TestLogging.Simple.Delete(new[] { newItem });

                logRecord = repository.Common.Log.Query().Where(log => log.ItemId == newItem.ID && log.Action == "Delete").SingleOrDefault();
                Assert.IsNotNull(logRecord, "There should be a 'Delete' record in the log.");

                var xmlText = @"<?xml version=""1.0"" encoding=""UTF-16""?>" + Environment.NewLine + logRecord.Description;
                Console.WriteLine(xmlText);
                var xdoc = XDocument.Parse(xmlText);
                Console.WriteLine(string.Join(", ", xdoc.Root.Attributes().Select(a => a.Name + ":" + a.Value)));

                Assert.IsTrue(IsNullOrEmpty(xdoc.Root.Attribute("Count")));
                Assert.IsTrue(IsNullOrEmpty(xdoc.Root.Attribute("Name")));
                Assert.IsTrue(IsNullOrEmpty(xdoc.Root.Attribute("Created")));
            }
        }

        [TestMethod]
        public void SqlChangeID()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                var repository = new Common.DomRepository(executionContext);

                var id1 = new Guid("11111111-1111-1111-1111-111111111111");
                var id2 = new Guid("22222222-2222-2222-2222-222222222222");

                executionContext.SqlExecuter.ExecuteSql(new[]
                    {
                        "DELETE FROM TestLogging.Complex",
                        "DELETE FROM TestLogging.Simple",
                        "DELETE FROM Common.Log",
                        "INSERT INTO TestLogging.Simple (ID, Name) VALUES (" + SqlUtility.QuoteGuid(id1) + ", 'abc')",
                        "UPDATE TestLogging.Simple SET ID = " + SqlUtility.QuoteGuid(id2)
                    });

                var logRecords = repository.Common.Log.All();
                var report = TestUtility.DumpSorted(logRecords, log => log.ItemId.ToString() + " " + log.Action + " " + log.Description.Contains("abc"));

                Assert.AreEqual(
                    "11111111-1111-1111-1111-111111111111 Delete True, "
                        + "11111111-1111-1111-1111-111111111111 Insert False, "
                        + "22222222-2222-2222-2222-222222222222 Insert False",
                    report);
            }
        }

        [TestMethod]
        public void Complex()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                executionContext.SqlExecuter.ExecuteSql(new[]
                    {
                        "DELETE FROM TestLogging.Complex",
                        "DELETE FROM TestLogging.Simple",
                        "DELETE FROM Common.Log",
                    });

                var repository = new Common.DomRepository(executionContext);
                var id = Guid.NewGuid();

                var simple = new TestLogging.Simple { ID = Guid.NewGuid() };
                repository.TestLogging.Simple.Insert(new[] { simple });

                var complex = new TestLogging.Complex
                {
                    bi = new byte[] { 1, 2, 3 },
                    bo = true,
                    da = new DateTime(2001, 2, 3),
                    t = new DateTime(2001, 2, 3, 4, 5, 6),
                    de = 123.4567m,
                    g = Guid.NewGuid(),
                    ls = "abc",
                    m = 11.22m,
                    r = simple
                };
                repository.TestLogging.Complex.Insert(new[] { complex });
                complex.ls = "def";
                repository.TestLogging.Complex.Update(new[] { complex });
                repository.TestLogging.Complex.Delete(new[] { complex });

                var ins = repository.Common.Log.Query().Where(log => log.TableName == "TestLogging.Complex" && log.Action == "Insert").Single();
                var upd = repository.Common.Log.Query().Where(log => log.TableName == "TestLogging.Complex" && log.Action == "Update").Single();
                var del = repository.Common.Log.Query().Where(log => log.TableName == "TestLogging.Complex" && log.Action == "Delete").Single();

                Assert.AreEqual("", ins.Description);
                Assert.AreEqual(@"<PREVIOUS ls=""abc"" />", upd.Description);

                var description = del.Description.Split(' ');
                Assert.AreEqual(@"<PREVIOUS", description[0]);
                Assert.AreEqual(@"bi=""0x010203""", description[1]);
                Assert.AreEqual(@"bo=""1""", description[2]);
                Assert.AreEqual(@"da=""2001-02-03""", description[3]);
                Assert.IsTrue(new Regex(@"^t=""2001-02-03T04:05:06(.0+)?""$").IsMatch(description[4]));// optional millisconds
                Assert.IsTrue(new Regex(@"^de=""123\.45670*""$").IsMatch(description[5]));// optional additional zeros
                Assert.AreEqual(@"g=""" + SqlUtility.GuidToString(complex.g.Value) + @"""", description[6]);
                Assert.AreEqual(@"ls=""def""", description[7]);
                Assert.AreEqual(@"m=""11.2200""", description[8]);
                Assert.AreEqual(@"rID=""" + SqlUtility.GuidToString(simple.ID) + @"""", description[9]);
                Assert.AreEqual(@"/>", description[10]);
            }
        }

        [TestMethod]
        public void LogRelatedItems_Detail()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                executionContext.SqlExecuter.ExecuteSql(new[] { "DELETE FROM Common.Log" });

                var repository = new Common.DomRepository(executionContext);

                var parent1 = new TestLogging.Simple { ID = Guid.NewGuid(), Name = "p1" };
                var parent2 = new TestLogging.Simple { ID = Guid.NewGuid(), Name = "p2" };
                repository.TestLogging.Simple.Insert(new[] { parent1, parent2 });

                System.Threading.Thread.Sleep(100);

                var child1 = new TestLogging.Complex { ls = "c1", r = parent1 };
                var child2 = new TestLogging.Complex { ls = "c2", r = parent2 };
                repository.TestLogging.Complex.Insert(new[] { child1, child2 });

                System.Threading.Thread.Sleep(100);

                child1.r = parent2;
                repository.TestLogging.Complex.Update(new[] { child1 });

                var names = new Dictionary<Guid?, string> {
                    { parent1.ID, parent1.Name },
                    { parent2.ID, parent2.Name },
                    { child1.ID, child1.ls },
                    { child2.ID, child2.ls } };

                Assert.AreEqual(
                    "TestLogging.Complex c1 Update Detail, TestLogging.Complex c2 Insert Detail, TestLogging.Simple p2 Insert ",
                    TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Simple", ItemId = parent2.ID }),
                        log => log.TableName + " " + names[log.ItemId] + " " + log.Action + " " + log.Relation));

                Assert.AreEqual(
                    "TestLogging.Complex c1 Update Detail, TestLogging.Complex c1 Insert Detail, TestLogging.Simple p1 Insert ",
                    TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Simple", ItemId = parent1.ID }),
                        log => log.TableName + " " + names[log.ItemId] + " " + log.Action + " " + log.Relation));

                Assert.AreEqual(
                    "TestLogging.Complex c1 Update , TestLogging.Complex c1 Insert ",
                    TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Complex", ItemId = child1.ID }),
                        log => log.TableName + " " + names[log.ItemId] + " " + log.Action + " " + log.Relation));
            }
        }
        [TestMethod]
        public void LogRelatedItems_DetailMultiple()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                executionContext.SqlExecuter.ExecuteSql(new[] { "DELETE FROM Common.Log" });

                var repository = new Common.DomRepository(executionContext);

                var parent1 = new TestLogging.Simple { ID = Guid.NewGuid(), Name = "p1" };
                var parent2 = new TestLogging.Simple { ID = Guid.NewGuid(), Name = "p2" };
                repository.TestLogging.Simple.Insert(new[] { parent1, parent2 });

                System.Threading.Thread.Sleep(100);

                var child1 = new TestLogging.Complex { ls = "c1", r = parent1, r2 = parent1, r3 = parent1 };
                var child2 = new TestLogging.Complex { ls = "c2", r = parent2, r2 = parent2, r3 = parent2 };
                repository.TestLogging.Complex.Insert(new[] { child1, child2 });

                System.Threading.Thread.Sleep(100);

                child1.r = parent2;
                repository.TestLogging.Complex.Update(new[] { child1 });

                System.Threading.Thread.Sleep(100);

                child1.r2 = parent2;
                repository.TestLogging.Complex.Update(new[] { child1 });

                System.Threading.Thread.Sleep(100);

                child1.r3 = parent2;
                repository.TestLogging.Complex.Update(new[] { child1 });

                var names = new Dictionary<Guid?, string> {
                    { parent1.ID, parent1.Name },
                    { parent2.ID, parent2.Name },
                    { child1.ID, child1.ls },
                    { child2.ID, child2.ls } };

                // c1 has multiple references to it's parent, so it will show multiple times in its log: as a Detail (by r) and as a SomeReferece (by r2).
                Assert.AreEqual(
                    "\r\nc1 Update: r2ID. SomeReference, c1 Update: rID. Detail, c1 Update: rID. SomeReference, c1 Insert. Detail, c1 Insert. SomeReference, p1 Insert. ",
                    "\r\n" + TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Simple", ItemId = parent1.ID }),
                        log => names[log.ItemId] + " " + log.Summary + " " + log.Relation));

                Assert.AreEqual(
                    "\r\nc1 Update: r3ID. Detail, c1 Update: r3ID. SomeReference, c1 Update: r2ID. Detail, c1 Update: r2ID. SomeReference, c1 Update: rID. Detail, c2 Insert. Detail, c2 Insert. SomeReference, p2 Insert. ",
                    "\r\n" + TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Simple", ItemId = parent2.ID }),
                        log => names[log.ItemId] + " " + log.Summary + " " + log.Relation));

                Assert.AreEqual(
                    "\r\nc1 Update: r3ID. , c1 Update: r2ID. , c1 Update: rID. , c1 Insert. ",
                    "\r\n" + TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Complex", ItemId = child1.ID }),
                        log => names[log.ItemId] + " " + log.Summary + " " + log.Relation));
            }
        }

        [TestMethod]
        public void LogRelatedItems_Extension()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                executionContext.SqlExecuter.ExecuteSql(new[] { "DELETE FROM Common.Log" });

                var repository = new Common.DomRepository(executionContext);

                var b1 = new TestLogging.Base { ID = Guid.NewGuid(), Name = "b1" };
                var b2 = new TestLogging.Base { ID = Guid.NewGuid(), Name = "b2" };
                repository.TestLogging.Base.Insert(new[] { b1, b2 });

                System.Threading.Thread.Sleep(100);

                var e1 = new TestLogging.Extension { ID = b1.ID, Name = "e1" };
                var e2 = new TestLogging.Extension { ID = b2.ID, Name = "e2" };
                repository.TestLogging.Extension.Insert(new[] { e1, e2 });

                System.Threading.Thread.Sleep(100);

                e2.Name = e2.Name + "x";
                repository.TestLogging.Extension.Update(new[] { e2 });

                var names = new Dictionary<Guid?, string> {
                    { b1.ID, b1.Name },
                    { b2.ID, b2.Name } };

                Assert.AreEqual(
                    "TestLogging.Extension b2 Update Extension, TestLogging.Extension b2 Insert Extension, TestLogging.Base b2 Insert ",
                    TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Base", ItemId = b2.ID }),
                        log => log.TableName + " " + names[log.ItemId] + " " + log.Action + " " + log.Relation));

                Assert.AreEqual(
                    "TestLogging.Extension b1 Insert Extension, TestLogging.Base b1 Insert ",
                    TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Base", ItemId = b1.ID }),
                        log => log.TableName + " " + names[log.ItemId] + " " + log.Action + " " + log.Relation));

                Assert.AreEqual(
                    "TestLogging.Extension b2 Update , TestLogging.Extension b2 Insert ",
                    TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Extension", ItemId = e2.ID }),
                        log => log.TableName + " " + names[log.ItemId] + " " + log.Action + " " + log.Relation));
            }
        }

        [TestMethod]
        public void LogRelatedItems_Summary()
        {
            using (var executionContext = new CommonTestExecutionContext())
            {
                executionContext.SqlExecuter.ExecuteSql(new[] { "DELETE FROM Common.Log" });

                var repository = new Common.DomRepository(executionContext);

                var b1 = new TestLogging.Simple { ID = Guid.NewGuid(), Name = "b1", Count = 1 };
                repository.TestLogging.Simple.Insert(new[] { b1 });

                System.Threading.Thread.Sleep(100);

                b1.Name = "bb1";
                repository.TestLogging.Simple.Update(new[] { b1 });

                b1.Name = null;
                b1.Count = null;
                repository.TestLogging.Simple.Update(new[] { b1 });

                b1.Name = "bbbb1";
                b1.Count = 111;
                repository.TestLogging.Simple.Update(new[] { b1 });

                Assert.AreEqual(
                    "Update: Count, Name., Update: Count, Name., Update: Name., Insert.",
                    TestUtility.Dump(
                        repository.Common.RelatedEvents.Filter(new Common.LoggedItem { TableName = "TestLogging.Simple", ItemId = b1.ID }),
                        log => log.Summary));
            }
        }
    }
}
