using System.Threading;
using System.Threading.Tasks;

namespace DnsSelector.Services;

public interface IPowerShellRunner
{
    Task<string> RunAsync(string script, CancellationToken ct = default);
}
