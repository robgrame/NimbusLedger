using NimbusLedger.Core.Models;

namespace NimbusLedger.Core.Abstractions;

public interface ICleanupPlanner
{
    CleanupPlan CreatePlan(LedgerSnapshot snapshot);
}
