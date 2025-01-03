using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace FileHashComparer;

/// <summary>
/// Provides recursive search in file system.
/// </summary>
public class RecursiveFileComparer(ILogger<RecursiveFileComparer> logger)
{
    /// <summary>
    /// Unique traversed directories.
    /// </summary>
    private readonly HashSet<string> _visitedDirectories = [];

    /// <summary>
    /// Unique hashes to determine duplicates.
    /// </summary>
    private readonly HashSet<string> _uniqueFiles = [];

    /// <summary>
    /// Duplicates contained in file system.
    /// </summary>
    private readonly List<string> _fileDuplicatesPaths = [];

    private readonly Lock _folderLockObject = new();

    /// <summary>
    /// Semaphore to limit the number of concurrent directories processing tasks based on the number of available processors.
    /// The semaphore allows up to the number of logical processors (cores) to process directories concurrently.
    /// </summary>
    private readonly SemaphoreSlim _folderSemaphore = new(Environment.ProcessorCount,
        Environment.ProcessorCount);

    /// <summary>
    /// Semaphore to limit the number of concurrent file processing tasks based on the number of available processors.
    /// The semaphore allows up to the number of logical processors (cores) to process files concurrently.
    /// </summary>
    private readonly SemaphoreSlim _fileSemaphore = new(Environment.ProcessorCount,
        Environment.ProcessorCount);

    /// <summary>
    /// Recursively traverses file system to locate duplicate files
    /// </summary>
    /// <param name="newDirectory">Directory to traverse</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>List of duplicate files location</returns>
    public async Task<IEnumerable<string>> SearchDuplicateFilesRecursive(string newDirectory, CancellationToken token)
    {
        if (!Directory.Exists(newDirectory))
        {
            throw new ArgumentException($"Directory {newDirectory} does not exist");
        }

        CheckDirectoryUniqueness(newDirectory);

        var (filePaths, fileHashes) = await GetFilePathsAndHashes(newDirectory, token);

        ProcessFileHashes(filePaths, fileHashes);

        var directoriesToTraverse = 
            Directory.GetDirectories(newDirectory, "*", SearchOption.TopDirectoryOnly);
            
        var tasks = directoriesToTraverse.Select(dir => Task.Run(async () =>
            {
                await _folderSemaphore.WaitAsync(token);
                try
                {
                    // Recursive call
                    await SearchDuplicateFilesRecursive(dir, token);
                }
                finally
                {
                    _folderSemaphore.Release();
                }
            }, token))
            .ToList();
        await Task.WhenAll(tasks);

        return _fileDuplicatesPaths;
    }

    /// <summary>
    /// Adds unique hashes to hashset, moving duplicates to list
    /// </summary>
    /// <param name="filePaths">File locations</param>
    /// <param name="fileHashes">Computed hashes</param>
    private void ProcessFileHashes(string[] filePaths, string[] fileHashes)
    {
        foreach (var (filePath, fileHash) in filePaths.Zip(fileHashes, (path, hash) => (path, hash)))
        {
            var fileIsAdded = _uniqueFiles.Add(fileHash);
            if (!fileIsAdded)
            {
                _fileDuplicatesPaths.Add(filePath);
            }
        }
    }

    /// <summary>
    /// Gets computed hashes and file locations
    /// </summary>
    /// <param name="directory">Directory to explore</param>
    /// <param name="token">Cancellation token</param>
    private async Task<(string[] filePaths, string[] fileHashes)> GetFilePathsAndHashes(string directory,
        CancellationToken token)
    {
        var filePaths = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);

        var fileHashes = await GetFileHashesAsync(filePaths, token);
        return (filePaths, fileHashes);
    }

    /// <summary>
    /// Checks if directory already been traversed. 
    /// </summary>
    /// <param name="newDirectory"></param>
    /// <exception cref="InvalidOperationException">If already traversed, that means we are in a loop,
    /// therefore process needs to be ended</exception>
    private void CheckDirectoryUniqueness(string newDirectory)
    {
        bool uniqueDir;
        lock (_folderLockObject)
        {
            uniqueDir = _visitedDirectories.Add(newDirectory);
        }

        if (uniqueDir)
        {
            return;
        }

        logger.LogCritical("Directory loop detected via recursive search. Exiting execution.");
        throw new InvalidOperationException("Directory loop detected via recursive search.");
    }

    /// <summary>
    /// Concurrently computes files hashes inside directory
    /// </summary>
    /// <param name="filePaths">Paths to files</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>Awaitable Task with array of file hashes</returns>
    private async Task<string[]> GetFileHashesAsync(string[] filePaths, CancellationToken token)
    {
        var hashes = new ConcurrentBag<string>();
        var tasks = filePaths.Select(async file =>
        {
            if (!File.Exists(file))
            {
                logger.LogWarning("File {file} does not exist.", file);
                return;
            }

            await _fileSemaphore.WaitAsync(token);
            try
            {
                using var sha256 = SHA256.Create();
                await using var fileStream = File.OpenRead(file);
                var hash = await sha256.ComputeHashAsync(fileStream, token);

                hashes.Add(Convert.ToHexStringLower(hash));
            }
            catch (Exception ex)
            {
                logger.LogError("File {file} could not be hashed. Error: {errorMessage}", file, ex.Message);
            }
            finally
            {
                _fileSemaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return hashes.ToArray();
    }
}