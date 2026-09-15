using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Bimwright.Ipt.Shared.Contracts;

namespace Bimwright.Ipt.Shared.Infrastructure;

/// <summary>
/// A failure a caller can branch on: <see cref="Code"/> is a real <see cref="InventorErrorCodes"/>
/// member, <see cref="Exception.Message"/> is human-readable guidance, and <see cref="Details"/>
/// carries the machine-readable specifics — the same split <see cref="CadBatchException"/> uses.
/// <para>
/// Throw this (or turn it into a result with <c>HandlerBase.Fail</c>) instead of spelling the code
/// into the message. A code that travels as a prose prefix inside an <c>API_ERROR</c>, or as a
/// <c>Fail</c> message under a generic code, forces every caller to parse the sentence back apart to
/// tell a stale plan from an Inventor crash. <see cref="CommandDispatcher"/> reports this exception
/// under its own code rather than sanitizing it into <c>API_ERROR</c>.
/// </para>
/// </summary>
public sealed class CodedFailureException : Exception
{
    public CodedFailureException(string code, string message, JObject? details = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Details = details;
    }

    /// <summary>Canonical <see cref="InventorErrorCodes"/> member for this failure.</summary>
    public string Code { get; }

    /// <summary>Machine-readable specifics, so the message never has to be parsed.</summary>
    public JObject? Details { get; }

    /// <summary>
    /// The code of a failure, whether it is carried structurally or still spelled as the
    /// <c>"CODE: message"</c> prefix the prose-tagged throw sites use. Null when there is none.
    /// </summary>
    public static string? LiftCode(Exception? error)
    {
        if (error == null) return null;
        if (error is CodedFailureException coded) return coded.Code;
        if (error is CadBatchException batch) return batch.StepCode ?? batch.Code;
        string message = error.Message ?? "";
        int colon = message.IndexOf(':');
        if (colon <= 0) return null;
        string candidate = message.Substring(0, colon);
        return candidate.Length <= 40 && candidate.All(c => char.IsUpper(c) || c == '_') ? candidate : null;
    }

    /// <summary>
    /// <c>{"reason": "&lt;lifted code&gt;"}</c> for a wrapped failure, so a caller that sees
    /// <c>ROLLED_BACK</c> can still tell an interference rejection from a rebuild failure without
    /// reading the sentence. Null when the inner failure carries no code to lift.
    /// </summary>
    public static JObject? ReasonOf(Exception? error)
    {
        string? reason = LiftCode(error);
        return reason == null ? null : new JObject { ["reason"] = reason };
    }
}
