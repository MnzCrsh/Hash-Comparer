using FakeItEasy;
using Microsoft.Extensions.Logging;

namespace FileHashComparer.Tests;

public class RecursiveFileComparerTests
{
    private const int FolderDepth = 100; 
    
    [Fact]
    private async Task SearchDuplicateFilesAsync_ShouldFindDuplicateFiles()
    {
        // Arrange
        var loggerMock = A.Fake<ILogger<RecursiveFileComparer>>();
        var comparor = new RecursiveFileComparer(loggerMock);
        var (baseDir, numberOfFiles) = 
            TestUtils.CreateTestEnvironment(FolderDepth, "fileName :D", new string('a', 255));
        
        // Act
        var fileCopies = await comparor.SearchDuplicateFilesAsync(baseDir, CancellationToken.None);
        TestUtils.CleanupTestEnvironment();

        // Assert
        Assert.True(fileCopies.Count() == numberOfFiles - 1);
    }
}