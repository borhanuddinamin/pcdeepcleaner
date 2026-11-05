namespace WCM.Application.Interfaces;

public interface IDataDirectoryService
{
    bool IsPortable { get; }

    bool IsFirstRun { get; }

    string DataRoot { get; }

    string LogsDirectory { get; }

    /// <summary>Saves the data root used from the next launch.</summary>
    /// <exception cref="ArgumentException">The path is relative or inside a protected location.</exception>
    void SaveDataRoot(string path);
}
