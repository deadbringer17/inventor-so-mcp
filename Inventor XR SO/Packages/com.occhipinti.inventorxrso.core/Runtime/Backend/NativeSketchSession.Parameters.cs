using System;
using Newtonsoft.Json.Linq;

namespace InventorXrSo.Core.Backend
{
    public sealed partial class NativeSketchSession
    {
        private DocumentState _parameterBaseline;
        private bool IsParameterDraft => _parameterBaseline != null;
        public void SetParameterExpression(string name, string expression)
            => SetParameterDraft(NativeParameterOperations.SetExpression(name, expression));
        public void CreateParameter(string name, string expression, string unit)
            => SetParameterDraft(NativeParameterOperations.Create(name, expression, unit));
        public void RenameParameter(string name, string newName)
            => SetParameterDraft(NativeParameterOperations.Rename(name, newName));
        public void DeleteParameter(string name)
            => SetParameterDraft(NativeParameterOperations.Delete(name));

        // Closed builder vocabulary: no arbitrary batch command can enter this path.
        private void SetParameterDraft(JObject operation)
        {
            if (!CanEdit || HasDraft || Mutation.Status != DesignStatus.Empty)
                throw new InvalidOperationException("Completa o annulla la bozza corrente prima di modificare un parametro.");
            _draft = new JArray(operation);
            _parameterBaseline = Snapshot.State;
            Mutation.SetDraft(_draft); Error = null; Changed?.Invoke();
        }
    }
}
