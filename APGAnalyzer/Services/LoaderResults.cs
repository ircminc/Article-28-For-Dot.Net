namespace APGAnalyzer.Services;

public class WeightsHistoryLoadResult
{
    public int ApgWeightRows { get; set; }
    public int PxBasedWeightRows { get; set; }
    public int FeeScheduleRows { get; set; }
    public string? FileName { get; set; }
    public TimeSpan Elapsed { get; set; }
}

public class BaseRatesLoadResult
{
    public int RowsDeleted { get; set; }
    public int RowsInserted { get; set; }
    public int DistinctPeerGroups { get; set; }
    public int DistinctEffectiveDates { get; set; }
    public DateOnly? MostRecentEffectiveDate { get; set; }
    public int ProviderCountyRows { get; set; }   // populated by PMTAC loader (county sheet)
    public string? FileName { get; set; }
    public TimeSpan Elapsed { get; set; }
}

public class MasterResetResult
{
    public int RowsDeletedTotal { get; set; }
    public Dictionary<string, int> ByTable { get; set; } = new();
    public string[] PreservedTables { get; set; } = Array.Empty<string>();
    public TimeSpan Elapsed { get; set; }
}
