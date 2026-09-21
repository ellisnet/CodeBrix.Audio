using System;
using System.IO;

namespace SunoInstrumentSwap;

/// <summary>
/// The entry point: read the command line, keep the output out of the repository, and run.
/// </summary>
internal static class Program
{
    /// <summary>Runs the sample.</summary>
    /// <param name="args">The command line; <c>--help</c> prints what it takes.</param>
    /// <returns>Zero when everything asked for was written.</returns>
    internal static int Main(string[] args)
    {
        var options = SwapOptions.Parse(args, out var problem);

        if (options == null)
        {
            Console.Error.WriteLine(problem);
            Console.Error.WriteLine();
            Console.Error.WriteLine(SwapOptions.Usage());
            return 2;
        }

        if (options.WantsHelp)
        {
            Console.WriteLine(SwapOptions.Usage());
            return 0;
        }

        if (!CheckOutputIsOutsideTheRepository(options))
        {
            return 2;
        }

        Directory.CreateDirectory(options.OutputFolder);
        Directory.CreateDirectory(options.CacheFolder);
        UseTemporaryFolder(options.CacheFolder);

        try
        {
            return new SwapRunner(options).Run();
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or NotSupportedException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static bool CheckOutputIsOutsideTheRepository(SwapOptions options)
    {
        var root = RepositoryGuard.FindRepositoryRoot();

        foreach (var (what, path) in new[]
                 {
                     ("--output", options.OutputFolder),
                     ("--cache", options.CacheFolder),
                 })
        {
            if (RepositoryGuard.IsInsideRepository(path))
            {
                Console.Error.WriteLine(
                    $"{what} is '{path}', which is inside the repository at '{root}'. Renders and " +
                    "extraction caches are hundreds of megabytes and do not belong in source " +
                    "control; point this somewhere else.");
                return false;
            }
        }

        return true;
    }

    // A stems zip is tens to hundreds of megabytes and is extracted through the system temporary
    // folder, which on some machines is a small in-memory file system. The extraction cache is
    // where the big files go, so the temporary folder goes there too - and this has to happen
    // before anything calls into the library.
    private static void UseTemporaryFolder(string folder)
    {
        Environment.SetEnvironmentVariable("TMPDIR", folder);
        Environment.SetEnvironmentVariable("TEMP", folder);
        Environment.SetEnvironmentVariable("TMP", folder);
    }
}
