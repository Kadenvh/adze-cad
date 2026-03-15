using System;
using System.Collections.Generic;
using NUnit.Framework;
using Adze.Trace.Memory;

namespace Adze.Tests.Trace;

[TestFixture]
public class DocumentMemoryTests
{
    [Test]
    public void DocumentMemory_DefaultsToEmptyCollections()
    {
        var memory = new DocumentMemory();
        Assert.AreEqual(string.Empty, memory.DocumentKey);
        Assert.AreEqual(0, memory.SessionCount);
        Assert.AreEqual(0, memory.CommonWorkflows.Count);
        Assert.AreEqual(0, memory.KnownIssues.Count);
        Assert.AreEqual(0, memory.KeyDimensions.Count);
        Assert.AreEqual(0, memory.RecentIntents.Count);
    }

    [Test]
    public void UserPreferenceMemory_DefaultsToBriefConcise()
    {
        var prefs = new UserPreferenceMemory();
        Assert.AreEqual("brief", prefs.PreferredAnswerMode);
        Assert.AreEqual("concise", prefs.PreferredVerbosity);
        Assert.IsFalse(prefs.AutoIncludeDiagnostics);
    }
}

[TestFixture]
public class MemoryStoreTests
{
    private MemoryStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new MemoryStore();
    }

    [Test]
    public void ComputeDocumentKey_DeterministicForSamePath()
    {
        string key1 = MemoryStore.ComputeDocumentKey(@"C:\test\Part1.SLDPRT");
        string key2 = MemoryStore.ComputeDocumentKey(@"C:\test\Part1.SLDPRT");
        Assert.AreEqual(key1, key2);
    }

    [Test]
    public void ComputeDocumentKey_CaseInsensitive()
    {
        string key1 = MemoryStore.ComputeDocumentKey(@"C:\Test\Part1.SLDPRT");
        string key2 = MemoryStore.ComputeDocumentKey(@"c:\test\part1.sldprt");
        Assert.AreEqual(key1, key2);
    }

    [Test]
    public void ComputeDocumentKey_DifferentPathsDifferentKeys()
    {
        string key1 = MemoryStore.ComputeDocumentKey(@"C:\test\Part1.SLDPRT");
        string key2 = MemoryStore.ComputeDocumentKey(@"C:\test\Part2.SLDPRT");
        Assert.AreNotEqual(key1, key2);
    }

    [Test]
    public void LoadDocumentMemory_EmptyPath_ReturnsNull()
    {
        var result = _store.LoadDocumentMemory("");
        Assert.IsNull(result);
    }

    [Test]
    public void LoadDocumentMemory_NonexistentDocument_ReturnsNull()
    {
        var result = _store.LoadDocumentMemory(@"C:\nonexistent\path_" + Guid.NewGuid() + ".SLDPRT");
        Assert.IsNull(result);
    }

    [Test]
    public void SaveAndLoadDocumentMemory_RoundTrips()
    {
        string testPath = @"C:\test\RoundTrip_" + Guid.NewGuid().ToString("N") + ".SLDPRT";
        string key = MemoryStore.ComputeDocumentKey(testPath);

        var memory = new DocumentMemory
        {
            DocumentKey = key,
            DocumentPath = testPath,
            DocumentTitle = "Test Part",
            SessionCount = 5,
            RecentIntents = new List<string> { "inspect dimensions", "check properties" }
        };

        _store.SaveDocumentMemory(memory);
        var loaded = _store.LoadDocumentMemory(testPath);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(key, loaded!.DocumentKey);
        Assert.AreEqual(testPath, loaded.DocumentPath);
        Assert.AreEqual(5, loaded.SessionCount);
    }

    [Test]
    public void SaveAndLoadUserPreferences_RoundTrips()
    {
        var prefs = new UserPreferenceMemory
        {
            UserId = "testuser_" + Guid.NewGuid().ToString("N"),
            PreferredAnswerMode = "detailed",
            PreferredVerbosity = "verbose",
            AutoIncludeDiagnostics = true
        };

        _store.SaveUserPreferences(prefs);
        var loaded = _store.LoadUserPreferences(prefs.UserId);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("detailed", loaded!.PreferredAnswerMode);
        Assert.AreEqual("verbose", loaded.PreferredVerbosity);
        Assert.IsTrue(loaded.AutoIncludeDiagnostics);
    }

    [Test]
    public void RecordIntent_IncrementsSessionCount()
    {
        string testPath = @"C:\test\IntentTest_" + Guid.NewGuid().ToString("N") + ".SLDPRT";

        _store.RecordIntent(testPath, "inspect dimensions");
        _store.RecordIntent(testPath, "check properties");

        var loaded = _store.LoadDocumentMemory(testPath);
        Assert.IsNotNull(loaded);
        Assert.AreEqual(2, loaded!.SessionCount);
        Assert.That(loaded.RecentIntents, Does.Contain("inspect dimensions"));
        Assert.That(loaded.RecentIntents, Does.Contain("check properties"));
    }

    [Test]
    public void RecordIntent_EmptyPath_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _store.RecordIntent("", "test"));
    }

    [Test]
    public void LoadUserPreferences_EmptyUserId_ReturnsNull()
    {
        var result = _store.LoadUserPreferences("");
        Assert.IsNull(result);
    }
}
