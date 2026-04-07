using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Utils;

namespace Core.Packages.Installation;

public class Updater<TEventHandler> : IInstallation where TEventHandler : IBackupEventHandler
{
    private readonly IInstaller? inner;
    private readonly PackageInstallationState previousInstallationState;
    private ISet<string> leftFromPreviousInstallation;
    private readonly IBackupStrategy backupStrategy;

    public Updater(string packageName, IInstaller? inner, PackageInstallationState installationState,
        IBackupStrategyProvider<TEventHandler> backupStrategyProvider, TEventHandler? eventHandler)
    {
        this.inner = inner;
        previousInstallationState = installationState;
        leftFromPreviousInstallation = (installationState?.Files ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseBackupStrategy = backupStrategyProvider.BackupStrategy(eventHandler);
        backupStrategy = new SkipUpdatedBackupStrategy(baseBackupStrategy, installationState?.Time, eventHandler);
    }

    public string PackageName => inner.PackageName; // Validate that it is the same if both not null

    // These should change depending if the previous packave has been uninstalled or not
    public int? PackageFsHash => inner.PackageFsHash;
    public IReadOnlySet<string> PackageDependencies => inner.PackageDependencies;
    public IReadOnlySet<RootedPath> InstalledFiles => inner.InstalledFiles;
    public IInstallation.State Installed => inner.Installed;

    public void Update(string destDir, ProcessingCallbacks<RootedPath> callbacks)
    {
        if (previousInstallationState?.FsHash != inner.PackageFsHash)
        {
            foreach (var relativePath in previousInstallationState?.Files ?? Enumerable.Empty<string>())
            {
                var gamePath = new RootedPath(destDir, relativePath);
                if (!callbacks.Accept(gamePath))
                {
                    continue;
                }
                backupStrategy.RestoreBackup(gamePath);
                leftFromPreviousInstallation.Remove(gamePath.Relative);
            }
            DeleteEmptyDirectories(destDir, previousInstallationState?.Files ?? Enumerable.Empty<string>());
        }

        inner.Install(InstallTo(destDir), backupStrategy, callbacks);

        // TODO restore backup on file removal
        // ~~...perform backup before install~~  that is not done here!

        // TODO installation state set to
        // - NotInstalled after uninstall if needed
        // - PartiallyInstalled if there was an error

        // NOTE files can be removed by mod installation... how to handle that?!
    }

    /*
                    filesLeft.Count == 0 ?
                        null :
                        packageInstallationState with
                        {
                            Partial = error,
                            Files = filesLeft
                        }
                    );
     */

    private static IInstaller.Destination InstallTo(string destDir) =>
        relativePath => new RootedPath(destDir, relativePath);

    private static void DeleteEmptyDirectories(string dstRootPath, IEnumerable<string> filePaths)
    {
        var dirs = filePaths
            .Select(file => Path.Combine(dstRootPath, file))
            .SelectMany(dstFilePath => AncestorsUpTo(dstRootPath, dstFilePath))
            .Distinct()
            .OrderByDescending(name => name.Length);
        foreach (var dir in dirs)
        {
            // Some packages have duplicate entries, so files might have been removed already
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
    }

    private static List<string> AncestorsUpTo(string root, string path)
    {
        var ancestors = new List<string>();
        for (var dir = Directory.GetParent(path); dir is not null && dir.FullName != root; dir = dir.Parent)
        {
            ancestors.Add(dir.FullName);
        }
        return ancestors;
    }

}
