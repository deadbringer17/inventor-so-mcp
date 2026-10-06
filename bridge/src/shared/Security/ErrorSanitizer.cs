using System;
using System.Text.RegularExpressions;

namespace Bimwright.Ipt.Shared.Security;

public static class ErrorSanitizer
{
    // Quoted paths (drive or UNC prefix): the quotes delimit the path, spaces inside are fine.
    private static readonly Regex QuotedPath = new(
        "\"(?:[A-Za-z]:\\\\|\\\\\\\\)[^\"]*\"|'(?:[A-Za-z]:\\\\|\\\\\\\\)[^']*'",
        RegexOptions.Compiled);

    // Unquoted: drive/UNC prefix, then up to and including the first file extension that ends a path
    // segment (so "Progetti Software\pezzo.ipt" is masked whole and "v1.2\pezzo.ipt" is not cut at ".2");
    // when the text has no such extension, up to the next whitespace.
    private static readonly Regex BarePath = new(
        @"(?:[A-Za-z]:\\|\\\\[^\\\s]+\\)(?:[^""'<>|*?\r\n]*?\.\w{1,5}(?![\w\\/])|\S+)",
        RegexOptions.Compiled);

    public static string Sanitize(Exception ex)
        => Sanitize(ex.Message ?? ex.GetType().Name);

    public static string Sanitize(string? message)
    {
        var msg = (message ?? "").Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ").Trim();
        msg = QuotedPath.Replace(msg, "<path>");
        msg = BarePath.Replace(msg, "<path>");
        return SecretMasker.Mask(msg);
    }
}
