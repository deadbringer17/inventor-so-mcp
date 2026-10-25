using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Bimwright.Ipt.Server.RealityCapture;

/// <summary>HTTP host supplies token-bound capture control. No Inventor add-in dependency and no user-provided host paths.</summary>
public interface ICaptureControl
{
    Task<JObject> InvokeAsync(string operation,JObject arguments,CancellationToken ct);
}
