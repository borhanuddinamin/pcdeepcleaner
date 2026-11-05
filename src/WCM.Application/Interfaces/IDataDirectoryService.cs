namespace WCM.Application.Interfaces;

public interface IDataDirectoryService
{
    bool IsPortable { get; }

    bool IsFirstRun { get; }

    string DataRoot { get; }

    string LogsDirectory { get; }

    /// <exception cref="InvalidDataException">The data root is inside a protected location.</exception>
    string DatabasePath { get; }

    /// <summary>Saves the data root used from the next launch.</summary>
    /// <exception cref="ArgumentException">The path is relative or inside a protected location.</exception>
    void SaveDataRoot(string path);
}
