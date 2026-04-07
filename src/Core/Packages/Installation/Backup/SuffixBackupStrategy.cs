namespace Core.Packages.Installation.Backup;

public class SuffixBackupStrategy : MoveFileBackupStrategy
{
    internal class Provider<TEventHandler> : IBackupStrategyProvider<TEventHandler>
        where TEventHandler : IBackupEventHandler
    {
        public IBackupStrategy BackupStrategy(TEventHandler? eventHandler) =>
            new SuffixBackupStrategy(eventHandler);
    }

    private class BackupFileNaming : IBackupFileNaming
    {
        private const string BackupSuffix = ".orig";

        public string ToBackup(string fullPath) => $"{fullPath}{BackupSuffix}";
        public bool IsBackup(string fullPath) => fullPath.EndsWith(BackupSuffix);
    }

    public SuffixBackupStrategy(IBackupEventHandler? eventHandler) :
        base(new BackupFileNaming(), eventHandler)
    {
    }
}
