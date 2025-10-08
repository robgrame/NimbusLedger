using NimbusLedger.Core.Models;

namespace NimbusLedger.Core.Abstractions;

public interface IActiveDirectoryClient
{
    Task<IReadOnlyList<ComputerAccount>> GetComputersAsync(CancellationToken cancellationToken);
}
