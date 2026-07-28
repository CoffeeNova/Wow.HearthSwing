namespace HearthSwing.Services;

public interface ILegacyDataCleanupService
{
    LegacyDataCleanupSummary Discover();

    LegacyDataCleanupSummary Cleanup();
}