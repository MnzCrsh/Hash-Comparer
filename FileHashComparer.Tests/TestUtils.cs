namespace FileHashComparer.Tests;

public static class TestUtils
{
    
    private static readonly string BaseDirectory = Path.Combine(Environment.CurrentDirectory, "TopFolder");

    public static (string baseDir, int numberOfFiles) CreateTestEnvironment(int depth, string fileName, string fileContent)
    {
        CleanupTestEnvironment();
        var currentDirectory = BaseDirectory;
        Directory.CreateDirectory(currentDirectory);
        
        int numberOfFiles = 0;
        for (int i = 0; i < depth; i++)
        {
            Directory.CreateDirectory(currentDirectory);
            var filePath = Path.Combine(currentDirectory, $"{fileName}{i}.txt");
            File.WriteAllText(filePath, fileContent);
            numberOfFiles++;
            
            currentDirectory = Path.Combine(currentDirectory, $"subfolder{i}");
        }

        return (Environment.CurrentDirectory, numberOfFiles);
    }
    
    public static void CleanupTestEnvironment()
    {
        if (Directory.Exists(BaseDirectory))
        {
            Directory.Delete(BaseDirectory, recursive: true);
        }
    }
}