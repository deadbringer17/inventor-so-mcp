using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventorXrSo.Core.Backend;
using InventorXrSo.Core.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace InventorXrSo.Unity.Ui
{
    public sealed partial class NativeSketchEditorUi
    {
        public const string ImportResourcesTab = "_sketch.import.resources";
        private IImportResourceClient _imports;
        private ImportResourcePage _importPage;
        private ImportResourceDescriptor _importSelection;
        private CancellationTokenSource _importRequest;
        private NativeSketchSnapshot _importSnapshot;
        private int _importGeneration;
        private Texture2D _importTexture;
        private GameObject _importPanel;
        public bool ImportResourceBusy => _importRequest != null;
        public ImportResourceDescriptor SelectedImportResource => _importSelection;

        public void BindImportResources(IImportResourceClient client)
        { ResetImportResources(); _imports = client; }

        /// <summary>Explicit client safety ceilings for BMP browsing. These are not measured Quest
        /// performance budgets or Inventor defaults; native import remains unavailable.</summary>
        public static IImportResourceClient CreateImportResourceBrowser(InventorBackend backend)
        {
            var metadata = new SketchImportResourcePolicy(32, 0, 80, 32L * 1024 * 1024,
                32L * 1024 * 1024, 4L * 1024 * 1024, 1, 1,
                new[] { new SketchImportFormat(SketchImportResourceKind.Image, "bmp") },
                Array.Empty<KeyValuePair<string, double>>());
            return backend.CreateImportResourceClient(new ImportResourceClientPolicy(metadata,
                1024 * 1024, 8L * 1024 * 1024, 32, 32, TimeSpan.FromSeconds(30), TimeSpan.FromHours(24)),
                () => DateTimeOffset.UtcNow);
        }

        private bool ImportCurrent => !_disposed && _imports != null && CanCreate;
        private bool ImportContextCurrent(NativeSketchSnapshot snapshot, int generation) =>
            ImportCurrent && generation == _importGeneration && ReferenceEquals(snapshot, Session.Snapshot);

        private void ClearImportPreview()
        {
            if (_importPanel != null) { _importPanel.SetActive(false); DestroyImportObject(_importPanel); }
            if (_importTexture != null) DestroyImportObject(_importTexture);
            _importPanel = null; _importTexture = null; _importSelection = null;
        }
        private static void DestroyImportObject(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }

        private void ResetImportResources()
        {
            _importGeneration++;
            var request = _importRequest; _importRequest = null;
            request?.Cancel(); request?.Dispose();
            _importPage = null; _importSnapshot = null; ClearImportPreview();
        }
        private void RefreshImportContext()
        {
            if (!ImportCurrent || (_importSnapshot != null && !ReferenceEquals(_importSnapshot, Session.Snapshot)))
                ResetImportResources();
            if (_importPanel != null)
                _importPanel.SetActive(_presented() && _palette()?.CurrentTab == ImportResourcesTab
                    && _importSelection != null && _importSelection.ExpiresUtc > DateTimeOffset.UtcNow);
        }

        public async Task LoadImportResourcesAsync(int offset = 0)
        {
            if (!ImportCurrent || ImportResourceBusy) return;
            ClearImportPreview();
            var snapshot = Session.Snapshot; _importSnapshot = snapshot;
            int generation = ++_importGeneration;
            var request = new CancellationTokenSource(); _importRequest = request;
            _changed();
            try
            {
                var page = await _imports.ListAsync(offset, request.Token);
                if (!ImportContextCurrent(snapshot, generation)) return;
                _importPage = page;
                _palette()?.ShowTabGroup(new[] { ImportResourcesTab }, true);
                _notice(page.Resources.Count == 0 ? "Nessuna risorsa disponibile sul server." : "Seleziona una risorsa. L'inserimento CAD non è ancora disponibile.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (ImportContextCurrent(snapshot, generation)) _notice("Risorse non disponibili: " + ex.Message); }
            finally
            {
                if (ReferenceEquals(_importRequest, request)) { _importRequest = null; request.Dispose(); }
                if (!_disposed) _changed();
            }
        }

        public async Task PreviewImportResourceAsync(ImportResourceDescriptor resource)
        {
            if (!ImportCurrent || ImportResourceBusy || resource == null || _importPage == null
                || !_importPage.Resources.Contains(resource)) return;
            ClearImportPreview();
            var snapshot = Session.Snapshot; int generation = ++_importGeneration;
            var request = new CancellationTokenSource(); _importRequest = request;
            _changed();
            try
            {
                var current = await _imports.DescribeAsync(resource.Resource.AssetId, request.Token);
                if (!ImportContextCurrent(snapshot, generation)) return;
                if (!current.PreviewAvailable) { _notice("Preview della risorsa non disponibile sul server."); return; }
                var preview = await _imports.DownloadPreviewAsync(current, request.Token);
                if (!ImportContextCurrent(snapshot, generation)) return;
                var palette = _palette();
                if (palette == null) return;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!ImageConversion.LoadImage(texture, preview.PngBytes, true)
                        || texture.width != current.Resource.Image.PixelWidth || texture.height != current.Resource.Image.PixelHeight)
                        throw new InvalidOperationException("Immagine della risorsa non decodificabile.");
                    var panel = UiFactory.Panel(palette.Canvas.transform, "Preview risorsa", UiFactory.Background);
                    _importPanel = panel.gameObject;
                    panel.sizeDelta = new Vector2(160, 142);
                    panel.anchoredPosition = new Vector2((float)palette.Layout.Width / 2 + 86, 0);
                    var label = UiFactory.Text(panel, "Preview risorsa · nessuna modifica CAD", 7);
                    label.rectTransform.sizeDelta = new Vector2(152, 22);
                    label.rectTransform.anchoredPosition = new Vector2(0, 57);
                    var image = new GameObject("Immagine", typeof(RectTransform), typeof(RawImage));
                    image.transform.SetParent(panel, false);
                    var rect = (RectTransform)image.transform;
                    float scale = Mathf.Min(150f / texture.width, 108f / texture.height);
                    rect.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);
                    rect.anchoredPosition = new Vector2(0, -10);
                    var raw = image.GetComponent<RawImage>(); raw.texture = texture; raw.raycastTarget = false;
                    _importTexture = texture; _importSelection = current;
                    var lifetime = _importPanel.AddComponent<ImportResourcePreviewLifetime>();
                    lifetime.Expired = () => current.ExpiresUtc <= DateTimeOffset.UtcNow;
                    lifetime.Release = () => { ResetImportResources(); if (!_disposed) _changed(); };
                    texture = null;
                    RefreshImportContext();
                    _notice("Preview risorsa " + current.Resource.Image.PixelWidth + " × " + current.Resource.Image.PixelHeight + " pixel. Inserimento CAD ancora non disponibile.");
                }
                finally { if (texture != null) DestroyImportObject(texture); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (ImportContextCurrent(snapshot, generation)) { ClearImportPreview(); _notice("Preview risorsa non disponibile: " + ex.Message); } }
            finally
            {
                if (ReferenceEquals(_importRequest, request)) { _importRequest = null; request.Dispose(); }
                if (!_disposed) _changed();
            }
        }

        private IEnumerable<XrAction> ImportResourceActions()
        {
            string reason() => _imports == null ? "Servizio risorse non disponibile." : "Apri uno schizzo aggiornato e completa l'operazione corrente.";
            yield return new XrAction("sketch.import.resources", "Risorse sul server ▸", SketchCommandCatalog.TabPrefix + "Inserisci",
                () => ImportCurrent && !ImportResourceBusy, () => { _ = LoadImportResourcesAsync(); }, reason);
            yield return new XrAction("sketch.import.reload", "Aggiorna risorse", ImportResourcesTab,
                () => ImportCurrent && !ImportResourceBusy, () => { _ = LoadImportResourcesAsync(); }, reason);
            yield return new XrAction("sketch.import.cancel", "Chiudi preview / annulla caricamento", ImportResourcesTab,
                () => !_disposed, () => { ResetImportResources(); _changed(); }, reason);
            var page = _importPage;
            if (page?.NextOffset != null)
                yield return new XrAction("sketch.import.next", "Risorse successive", ImportResourcesTab,
                    () => ImportCurrent && !ImportResourceBusy && ReferenceEquals(page, _importPage),
                    () => { _ = LoadImportResourcesAsync(page.NextOffset.Value); }, reason);
            foreach (var item in page?.Resources ?? Array.Empty<ImportResourceDescriptor>())
            {
                var resource = item;
                yield return new XrAction("sketch.import.pick." + resource.Resource.AssetId,
                    resource.Resource.Format.ToUpperInvariant() + " · " + resource.Resource.AssetId.Substring(2, 12), ImportResourcesTab,
                    () => ImportCurrent && !ImportResourceBusy && ReferenceEquals(page, _importPage)
                        && resource.ExpiresUtc > DateTimeOffset.UtcNow && resource.PreviewAvailable,
                    () => { _ = PreviewImportResourceAsync(resource); },
                    () => resource.PreviewAvailable ? "Risorsa scaduta o contesto non disponibile: aggiorna l'elenco." : "Preview della risorsa non disponibile sul server.");
            }
        }
    }

    // The resource TTL can expire while the user keeps a static palette open.
    internal sealed class ImportResourcePreviewLifetime : MonoBehaviour
    {
        internal Func<bool> Expired;
        internal Action Release;
        private void Update() { if (Expired?.Invoke() == true) { var release = Release; Release = null; release?.Invoke(); } }
    }
}
