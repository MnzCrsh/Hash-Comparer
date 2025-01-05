namespace FileHashComparer.Tests;

public static class TestUtils
{
    
    /// <summary>
    /// Top level directory where subfolders and files will be created.
    /// </summary>
    private static readonly string BaseDirectory = Path.Combine(Environment.CurrentDirectory, "TopFolder");
    
    private static readonly int MaxLimitOfFiles = Environment.ProcessorCount;

    /// <summary>
    /// Creates the environment with the folder structure for recursive search.
    /// </summary>
    /// <param name="depth">Depth level of subfolders</param>
    /// <param name="fileName">Name to create file with</param>
    /// <param name="fileContent">File content</param>
    /// <returns>Starting directory and number of created files (first one is the original and the rest are copies)</returns>
    public static (string baseDir, int numberOfFiles) CreateTestEnvironment(int depth, string fileName, string fileContent)
    {
        CleanupTestEnvironment();
        Directory.CreateDirectory(BaseDirectory);

        int totalNumberOfFiles = 0;

        CreateTreeStructure(BaseDirectory, depth, fileName, fileContent, ref totalNumberOfFiles);
        
        return (BaseDirectory, totalNumberOfFiles);
    }
    
    /// <summary>
    /// Recursively creates a tree structure of directories and files up to the specified depth.
    /// </summary>
    private static void CreateTreeStructure(string currentDirectory,
        int depth,
        string fileName,
        string fileContent,
        ref int totalNumberOfFiles)
    {
        if (depth <= 0) return;

        for (int subDirIndex = 1; subDirIndex <= MaxLimitOfFiles; subDirIndex++)
        {
            string subdirectoryPath = Path.Combine(currentDirectory, $"{Path.GetFileName(currentDirectory)}.{subDirIndex}");
            Directory.CreateDirectory(subdirectoryPath);

            totalNumberOfFiles = GenerateFiles(fileName, fileContent, subdirectoryPath, subDirIndex, totalNumberOfFiles);
            
            CreateTreeStructure(subdirectoryPath, depth - 1, fileName, fileContent, ref totalNumberOfFiles);
        }
    }

    private static int GenerateFiles(string fileName, string fileContent, string subdirectoryPath,
        int subDirIndex, int totalNumberOfFiles)
    {
        for (int fileIndex = 0; fileIndex <= MaxLimitOfFiles; fileIndex++)
        {
            string filePath = Path.Combine(subdirectoryPath, $"{fileName}_{subDirIndex}.{fileIndex}.txt");
            File.WriteAllText(filePath, fileContent);
            totalNumberOfFiles++;
        }

        return totalNumberOfFiles;
    }

    /// <summary>
    /// Deletes created file structure.
    /// </summary>
    public static void CleanupTestEnvironment()
    {
        if (Directory.Exists(BaseDirectory))
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }
}