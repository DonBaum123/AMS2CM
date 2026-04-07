using System.Collections.Immutable;
using Core.Packages.Installation.Backup;
using Core.Packages.Installation.Installers;
using Core.Packages.Repository;
using Core.Utils;

namespace Core.Packages.Installation;

public class PackagesUpdater<TEventHandler> : IPackagesUpdater<TEventHandler>
    where TEventHandler : PackagesUpdater.IEventHandler
{
    private readonly IInstallerFactory installerFactory;
    private readonly IBackupStrategyProvider<TEventHandler> backupStrategyProvider;
    private readonly TimeProvider timeProvider;

    public PackagesUpdater(
        IInstallerFactory installerFactory,
        IBackupStrategyProvider<TEventHandler>  backupStrategyProvider,
        TimeProvider timeProvider)
    {
        this.installerFactory = installerFactory;
        this.backupStrategyProvider = backupStrategyProvider;
        this.timeProvider = timeProvider;
    }

    public void Apply(
        IReadOnlyDictionary<string, PackageInstallationState> previousState,
        IEnumerable<Package> packages,
        string installDir,
        Action<IReadOnlyDictionary<string, PackageInstallationState>> afterInstall,
        TEventHandler eventHandler,
        CancellationToken cancellationToken)
    {
        var installers = packages.Select(installerFactory.PackageInstaller).ToImmutableArray();

        var currentState = new Dictionary<string, PackageInstallationState>(previousState);
        try
        {
            Apply(
                previousState,
                installers,
                installDir,
                (packageName, state) =>
                {
                    if (state is null)
                    {
                        currentState.Remove(packageName);
                    }
                    else
                    {
                        currentState[packageName] = state;
                    }
                },
                eventHandler,
                cancellationToken);
        }
        finally
        {
            afterInstall(currentState);
        }
    }

    protected virtual void Apply(
        IReadOnlyDictionary<string, PackageInstallationState> currentState,
        IReadOnlyCollection<IInstaller> installers,
        string installDir,
        Action<string, PackageInstallationState?> updatePackageState,
        TEventHandler eventHandler,
        CancellationToken cancellationToken)
    {
        var progress = new PercentOfTotal(installers.Count);

        // TODO

        var allInstalledFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var installer in installers.TakeWhile(_ => !cancellationToken.IsCancellationRequested))
        {
            eventHandler.ProgressUpdate(progress.IncrementDone());
            eventHandler.ProcessingPackage(installer);

            var updater = new Updater<TEventHandler>(installer, currentState.GetValueOrDefault(installer.PackageName),
                backupStrategyProvider, eventHandler);

            var shadowedBy = new HashSet<string>();
            var installCallbacks = new ProcessingCallbacks<RootedPath>
            {
                Accept = gamePath =>
                {
                    var overridingPackageName = allInstalledFiles.GetValueOrDefault(gamePath.Relative);
                    if (overridingPackageName is null)
                    {
                        return true;
                    }
                    if (overridingPackageName != installer.PackageName)
                    {
                        shadowedBy.Add(overridingPackageName);
                    }
                    return false;
                },
                Before = gamePath => allInstalledFiles.Add(gamePath.Relative, installer.PackageName)
            };
            try
            {
                updater.Update(installDir, installCallbacks);
            }
            finally
            {
                var packageInstalledFiles = installer.InstalledFiles
                    .Where(rp => rp.Root == installDir)
                    .Select(rp => rp.Relative)
                    .ToImmutableList();
                updatePackageState(installer.PackageName,
                    packageInstalledFiles.IsEmpty
                        ? null
                        : new PackageInstallationState(
                            Time: timeProvider.GetUtcNow().DateTime,
                            FsHash: installer.PackageFsHash,
                            Partial: installer.Installed == IInstallation.State.PartiallyInstalled,
                            Dependencies: installer.PackageDependencies,
                            ShadowedBy: shadowedBy,
                            Files: packageInstalledFiles
                    ));
            }
        }

        eventHandler.ProgressUpdate(progress.DoneAll());
    }
}

public static class PackagesUpdater
{
    public interface IEventHandler : IProgress, IBackupEventHandler
    {
        void ProcessingPackage(IPackageInfo package);
    }

    public interface IProgress
    {
        public void ProgressUpdate(IPercent? progress);
    }
}
