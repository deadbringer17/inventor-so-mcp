using System;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    /// <summary>
    /// Existing parameter batch vocabulary. Expressions remain native Inventor
    /// expressions; these builders neither evaluate them nor commit a document.
    /// Document/revision binding and preview/apply belong to DesignSession.
    /// </summary>
    public static class NativeParameterOperations
    {
        // Client input budgets, not claims about Inventor's grammar or limits.
        public const int MaxNameLength = 128;
        public const int MaxExpressionLength = 4096;
        public const int MaxUnitLength = 128;

        public static JObject SetExpression(string name, string expression) => Step("set_parameter", new JObject
        {
            ["name"] = Text(name, MaxNameLength, false),
            // The existing wire argument is named value even when it contains an expression.
            ["value"] = Text(expression, MaxExpressionLength, true)
        });

        public static JObject Create(string name, string expression, string unit) => Step("create_parameter", new JObject
        {
            ["name"] = Text(name, MaxNameLength, false),
            ["expression"] = Text(expression, MaxExpressionLength, true),
            ["unit"] = Text(unit, MaxUnitLength, false)
        });

        public static JObject Rename(string name, string newName) => Step("rename_parameter", new JObject
        {
            ["name"] = Text(name, MaxNameLength, false),
            ["new_name"] = Text(newName, MaxNameLength, false)
        });

        public static JObject Delete(string name) => Step("delete_parameter", new JObject
        { ["name"] = Text(name, MaxNameLength, false) });

        private static JObject Step(string command, JObject arguments) => new JObject
        { ["command"] = command, ["arguments"] = arguments };

        private static string Text(string value, int maximum, bool expression)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
                throw new ArgumentException("Testo del parametro mancante o oltre il limite.");
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsControl(current) && !(expression && (current == '\r' || current == '\n' || current == '\t')))
                    throw new ArgumentException("Carattere di controllo non ammesso nel parametro.");
                if (char.IsHighSurrogate(current))
                {
                    if (++i >= value.Length || !char.IsLowSurrogate(value[i]))
                        throw new ArgumentException("Testo Unicode del parametro non valido.");
                }
                else if (char.IsLowSurrogate(current))
                    throw new ArgumentException("Testo Unicode del parametro non valido.");
            }
            return value;
        }
    }
}
