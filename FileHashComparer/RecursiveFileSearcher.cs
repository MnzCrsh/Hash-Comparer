using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace FileHashComparer;

/// <summary>
/// Provides recursive search in file system.
/// </summary>
public class RecursiveFileComparer(ILogger<RecursiveFileComparer> logger)
{
    #region Collections

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

    #endregion

    #region Sync objects

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
    
    private readonly Lock _folderLockObject = new();
    private readonly Lock _fileLockObject = new();
    
    private const int MaxRecursionDepth = 1000;
    private static readonly AsyncLocal<int> CurrentRecursionDepth = new();

    #endregion

    /// <summary>
    /// Recursively traverses file system to locate duplicate files
    /// </summary>
    /// <param name="newDirectory">Directory to traverse</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>List of duplicate files location</returns>
    public async Task<IEnumerable<string>> SearchDuplicateFilesAsync(string newDirectory, CancellationToken token)
    {
        CurrentRecursionDepth.Value += 1;
        
        CheckDirectoryExists(newDirectory);
        CheckDirectoryUniqueness(newDirectory);

        await AnalyzeFilesForDuplicates(newDirectory, token);

        var tasks = GetDirectories(newDirectory)
            .Select( async dir=>
            {
                await _folderSemaphore.WaitAsync(token);
                try
                {
                    CheckRecursionDepth();

                    // Recursive call
                    await SearchDuplicateFilesAsync(dir, token);
                }
                catch (Exception e)
                {
                    logger.LogCritical("Recursive search error: {error}", e.Message);
                }
                finally
                {
                    CurrentRecursionDepth.Value -= 1;
                    _folderSemaphore.Release();
                }
            })
            .ToList();
        
        await Task.WhenAll(tasks);
        
        lock (_fileLockObject)
        {
            return _fileDuplicatesPaths.ToList();
        }
    }

    /// <summary>
    /// Checks if recursion depth limit is reached.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    private static void CheckRecursionDepth()
    {
        if (CurrentRecursionDepth.Value > MaxRecursionDepth)
        {
            throw new InvalidOperationException(
                $"Maximum recursion depth of {MaxRecursionDepth} exceeded.");
        }
    }


    /// <summary>
    /// Compares files with cached hashes. If there is collision by hash, then file will be added to duplicates list.
    /// </summary>
    /// <param name="directory">Current file directory</param>
    /// <param name="token">Cancellation token</param>
    private async Task AnalyzeFilesForDuplicates(string directory, CancellationToken token)
    {
        var (filePaths, fileHashes) = 
            await GetFilePathsAndHashesAsync(directory, token);

        ProcessFileHashes(filePaths, fileHashes);
    }
    
    /// <summary>
    /// Gets list of subfolders in current directory.
    /// </summary>
    /// <param name="directory">Current directory</param>
    /// <returns>Subfolders</returns>
    private static string[] GetDirectories(string directory)
    {
        var directoriesToTraverse = 
            Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly);
        return directoriesToTraverse;
    }

    /// <summary>
    /// Checks if directory path is valid. 
    /// </summary>
    /// <param name="directory">Directory to check</param>
    /// <exception cref="ArgumentException">Throws if directory is not valid</exception>
    private static void CheckDirectoryExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new ArgumentException($"Directory {directory} does not exist");
        }
    }

    /// <summary>
    /// Adds unique hashes to hashset, moving duplicates to list.
    /// </summary>
    /// <param name="filePaths">File locations</param>
    /// <param name="fileHashes">Computed hashes</param>
    private void ProcessFileHashes(string[] filePaths, string[] fileHashes)
    {
        foreach (var (filePath, fileHash) in filePaths.Zip(fileHashes, (path, hash) => (path, hash)))
        {
            lock (_fileLockObject)
            {
                var fileIsAdded = _uniqueFiles.Add(fileHash);
                if (fileIsAdded)
                {
                    continue;
                }
                _fileDuplicatesPaths.Add(filePath);
            }
        }
    }

    /// <summary>
    /// Gets computed hashes and file locations.
    /// </summary>
    /// <param name="directory">Directory to explore</param>
    /// <param name="token">Cancellation token</param>
    private async Task<(string[] filePaths, string[] fileHashes)> GetFilePathsAndHashesAsync(string directory,
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
    /// Concurrently computes files hashes inside directory.
    /// </summary>
    /// <param name="filePaths">Paths to files</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>Awaitable Task with array of file hashes</returns>
    private async Task<string[]> GetFileHashesAsync(string[] filePaths, CancellationToken token)
    {
        var hashes = new ConcurrentBag<string>();
        var tasks = filePaths.Where(File.Exists).Select(async file =>
        {
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
                logger.LogError("Failed to compute hash for file {file}. Error: {errorMessage}", file, ex.Message);
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