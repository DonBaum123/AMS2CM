namespace Core.Packages.Installation.Backup;

public interface IBackupStrategyProvider<in TEventHandler>
{
    IBackupStrategy BackupStrategy(TEventHandler? eventHandler);
}
