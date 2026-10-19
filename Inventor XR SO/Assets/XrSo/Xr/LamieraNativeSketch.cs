using System;
using System.Linq;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Unity.Scene;
using InventorXrSo.Unity.Ui;

namespace InventorXrSo.Xr
{
    public sealed partial class LamieraWorkspace
    {
        private NativeSketchEditorUi _nativeEditor;
        private NativeSketchCheckpoint _nativeCheckpoint;
        private SketchSheetView _nativeSheet;
        public NativeSketchSession NativeEditor => _nativeEditor?.Session;
        private bool NativeEditorOpen => _nativeEditor?.IsOpen == true || _nativeCheckpoint?.SketchId != null;
        private bool NativeHostReady => Active && _online && _kind=="part" && !_busy && _designContext!=null
            && _pendingMutations==0 && _session?.CanEdit==true && _mode.CanWrite && SceneCurrent;
        private void SaveNativeEditor()
        {
            if (_nativeEditor == null) return;
            _nativeCheckpoint = _nativeEditor.IsOpen ? _nativeEditor.Session.Checkpoint() : null;
            _nativeEditor.Dispose(); _nativeEditor=null;
        }
        private void BindNativeEditor()
        {
            _geometry?.BindNativeImages(_backend as INativeSketchImageBackend);
            if (!(_backend is INativeSketchBackend native) || _session == null) return;
            _nativeEditor=new NativeSketchEditorUi(native,_session,()=>_shell?.Palette,()=>NativeHostReady,()=>_geometry,()=>_head,
                ()=> { Refresh(); ArmedFieldChanged?.Invoke(); },SetNotice,snapshot=>
                {
                    _screen="native"; _flatDisplay?.SetFlatView(false);
                    if (_nativeSheet==null) { _nativeSheet=gameObject.AddComponent<SketchSheetView>(); _nativeSheet.Bind(_view.transform); }
                    if (Active && _bench?.Frame!=null) _nativeSheet.Enter(snapshot.Frame,280,190,_bench.Frame);
                },()=>
                {
                    _nativeCheckpoint=null;
                    if (_nativeSheet!=null && _bench?.Frame!=null) _nativeSheet.ShowModel(_bench.PartPose);
                    ResetDraft();
                },_nativeCheckpoint,()=>Active);
            if (_backend is InventorBackend inventor)
                _nativeEditor.BindImportResources(NativeSketchEditorUi.CreateImportResourceBrowser(inventor));
            _nativeCheckpoint=null;
        }
        private void OpenNativePicker() => OpenPicker("Schizzo da modificare",_designContext.Sketches.Where(s=>s.NativeSketchId!=null)
            .Select(s=>new PickerItem(s.Name,()=>OpenNativeSketch(s.NativeSketchId))));
        private async void OpenNativeSketch(string id)
        {
            if (_nativeEditor==null) return;
            ResetDraft(); _screen="native";
            try { await _nativeEditor.OpenAsync(id); } catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
        }
        private async Task RefreshNativeSketch(DocumentState state,bool reviewed)
        {
            _state=state; _nativeEditor.SetEnvironment(state,_online,_kind=="part");
            _nativeEditor.Session.AcknowledgeMutationRefresh(state,reviewed);
            await _nativeEditor.OpenAsync(_nativeEditor.Session.SketchId);
            _screen="native"; _flat?.SetContext(state,_online); RefreshMode(); LoadContext();
        }
        private async void PreviewNative()
        {
            try { await _nativeEditor.Session.PreviewAsync(); } catch (Exception ex) { SetNotice(ex.Message); }
            Refresh();
        }
    }
}
