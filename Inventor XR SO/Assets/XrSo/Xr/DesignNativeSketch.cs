using System;
using System.Linq;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using InventorXrSo.Unity.Ui;

namespace InventorXrSo.Xr
{
    public sealed partial class DesignWorkspace
    {
        private NativeSketchEditorUi _nativeEditor;
        private NativeSketchCheckpoint _nativeCheckpoint;
        public NativeSketchSession NativeEditor => _nativeEditor?.Session;
        private bool NativeEditorOpen => _nativeEditor?.IsOpen == true || _nativeCheckpoint?.SketchId != null;
        private bool NativeHostReady => Active && _online && _kind == "part" && !_busy && _context != null
            && _pendingMutations == 0 && _session?.CanEdit == true;
        private void SaveNativeEditor()
        {
            if (_nativeEditor == null) return;
            _nativeCheckpoint = _nativeEditor.IsOpen ? _nativeEditor.Session.Checkpoint() : null;
            _nativeEditor.Dispose(); _nativeEditor = null;
        }
        private void BindNativeEditor()
        {
            _geometry?.BindNativeImages(_backend as INativeSketchImageBackend);
            if (!(_backend is INativeSketchBackend native) || _session == null) return;
            _nativeEditor = new NativeSketchEditorUi(native,_session,()=>_shell?.Palette,()=>NativeHostReady,()=>_geometry,()=>_head,
                Refresh,SetNotice,snapshot=>
                {
                    _screen="native";
                    if (Active && _sheet != null && _bench?.Frame != null) _sheet.Enter(snapshot.Frame,280,190,_bench.Frame);
                },()=> { _nativeCheckpoint=null; ResetDraft(); },_nativeCheckpoint,()=>Active);
            if (_backend is InventorBackend inventor)
                _nativeEditor.BindImportResources(NativeSketchEditorUi.CreateImportResourceBrowser(inventor));
            _nativeCheckpoint = null;
        }
        private void OpenNativePicker()
        {
            OpenPicker("Schizzo da modificare",_context.Sketches.Where(s=>s.NativeSketchId!=null)
                .Select(s=>new PickerItem(s.Name,()=>OpenNativeSketch(s.NativeSketchId))));
        }
        private async void OpenNativeSketch(string id)
        {
            if (_nativeEditor == null) return;
            ResetDraft(); _screen="native";
            try { await _nativeEditor.OpenAsync(id); }
            catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
        }
        private async Task RefreshNativeSketch(DocumentState state,bool reviewed)
        {
            _state=state; _nativeEditor.SetEnvironment(state,_online,_kind=="part");
            _nativeEditor.Session.AcknowledgeMutationRefresh(state,reviewed);
            await _nativeEditor.OpenAsync(_nativeEditor.Session.SketchId);
            _screen="native"; LoadContext();
        }
    }
}
