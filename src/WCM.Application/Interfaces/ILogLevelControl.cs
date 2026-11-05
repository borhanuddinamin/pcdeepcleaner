namespace WCM.Application.Interfaces;

public interface ILogLevelControl
{
    /// <param name="level">Debug, Information, Warning or Error.</param>
    void Set(string level);
}
