namespace YetiHunt.Debug
{
    /// <summary>
    /// Interface for diagnostic dump methods.
    /// </summary>
    public interface IDiagnosticsService
    {
        void DumpMapInfo();
        void DumpMapCoordinateDebug();
        void DumpPlayerInfo();
        void DumpSnowballAndPlayerInfo();
        void DumpYetiClassInfo();
        void DumpSnowballClassInfo();
        void DumpAllYetiAnimatorState();
        void RecordCornerCoordinate();
        void ShowRecordedCorners();
    }
}
