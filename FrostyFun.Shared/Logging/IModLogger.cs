namespace FrostyFun.Shared.Logging
{
    public interface IModLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }
}
