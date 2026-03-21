using System;
using System.Collections.Generic;
using System.IO;
using Adze.Contracts.Models;
using Adze.Index;
using NUnit.Framework;

namespace Adze.Tests.Index;

[TestFixture]
public sealed class OleIndexerTests
{
    [Test]
    public void IsSupportedFile_SldprtExtension_ReturnsTrue()
    {
        Assert.That(OlePropertyReader.IsSupportedFile("C:\\test\\Part1.SLDPRT"), Is.True);
        Assert.That(OlePropertyReader.IsSupportedFile("C:\\test\\assembly.sldasm"), Is.True);
        Assert.That(OlePropertyReader.IsSupportedFile("C:\\test\\drawing.SLDDRW"), Is.True);
    }

    [Test]
    public void IsSupportedFile_NonSolidWorksExtension_ReturnsFalse()
    {
        Assert.That(OlePropertyReader.IsSupportedFile("C:\\test\\file.txt"), Is.False);
        Assert.That(OlePropertyReader.IsSupportedFile("C:\\test\\file.step"), Is.False);
        Assert.That(OlePropertyReader.IsSupportedFile(""), Is.False);
    }

    [Test]
    public void GetFileType_ReturnsCorrectType()
    {
        Assert.That(OlePropertyReader.GetFileType("Part1.sldprt"), Is.EqualTo("part"));
        Assert.That(OlePropertyReader.GetFileType("Asm.SLDASM"), Is.EqualTo("assembly"));
        Assert.That(OlePropertyReader.GetFileType("Drw.slddrw"), Is.EqualTo("drawing"));
        Assert.That(OlePropertyReader.GetFileType("file.txt"), Is.EqualTo("unknown"));
    }

    [Test]
    public void ReadFile_NonexistentFile_ReturnsNull()
    {
        ClosedFileRecord? record = OlePropertyReader.ReadFile("C:\\nonexistent\\file.sldprt");
        Assert.That(record, Is.Null);
    }

    [Test]
    public void ReadFile_UnsupportedExtension_ReturnsNull()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            ClosedFileRecord? record = OlePropertyReader.ReadFile(tempFile);
            Assert.That(record, Is.Null);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void BuildIndex_NonexistentDirectory_ReturnsError()
    {
        var indexer = new ClosedFileIndexer();
        IndexRunResult result = indexer.BuildIndex("C:\\nonexistent\\directory\\path");

        Assert.That(result.Errors, Is.GreaterThan(0));
        Assert.That(result.ErrorMessages, Is.Not.Empty);
    }

    [Test]
    public void BuildIndex_EmptyDirectory_ReturnsZeroFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "adze_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var indexer = new ClosedFileIndexer();
            IndexRunResult result = indexer.BuildIndex(tempDir);

            Assert.That(result.FilesScanned, Is.EqualTo(0));
            Assert.That(result.FilesIndexed, Is.EqualTo(0));
            Assert.That(result.Errors, Is.EqualTo(0));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void SearchService_EmptyIndex_ReturnsEmpty()
    {
        var indexer = new ClosedFileIndexer();
        var searchService = new ClosedFileSearchService(indexer);
        var query = new ClosedFileSearchQuery { Keyword = "test" };

        List<ClosedFileSearchResult> results = searchService.Search("C:\\nonexistent", query);
        Assert.That(results, Is.Empty);
    }

    [Test]
    public void OlePropertySetParser_EmptyData_ReturnsEmpty()
    {
        var result = OlePropertySetParser.ParsePropertySet(new byte[0]);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void OlePropertySetParser_NullData_ReturnsEmpty()
    {
        var result = OlePropertySetParser.ParsePropertySet(null!);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void OlePropertySetParser_TooSmallData_ReturnsEmpty()
    {
        var result = OlePropertySetParser.ParsePropertySet(new byte[10]);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void IndexRunResult_DefaultValues_AreZero()
    {
        var result = new IndexRunResult();
        Assert.That(result.FilesScanned, Is.EqualTo(0));
        Assert.That(result.FilesIndexed, Is.EqualTo(0));
        Assert.That(result.Errors, Is.EqualTo(0));
    }

    [Test]
    public void ClosedFileSearchQuery_DefaultMaxResults_IsFifty()
    {
        var query = new ClosedFileSearchQuery();
        Assert.That(query.MaxResults, Is.EqualTo(50));
    }
}
