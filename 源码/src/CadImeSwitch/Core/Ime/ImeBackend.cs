using System;

namespace CadImeSwitch.Core.Ime
{
    public enum ImeState
    {
        Unknown = 0,
        English = 1,
        Chinese = 2
    }

    public interface IImeBackend : IDisposable
    {
        string Name { get; }
        bool IsAvailable { get; }
        bool SetChinese();
        bool SetEnglish();
        ImeState GetState();
    }
}
