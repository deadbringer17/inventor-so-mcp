using InventorXrSo.Core.Experience;
using InventorXrSo.Unity.Ui;

namespace InventorXrSo.Xr
{
    public sealed partial class AppController
    {
        private bool ProtectedActivityWork => (_design?.BlocksActivityExit ?? false)
            || (_lamiera?.BlocksActivityExit ?? false) || (_assembly?.BlocksActivityExit ?? false)
            || (_capture?.BlocksActivityExit ?? false);

        private void ReportActivityExitBlocked()
        {
            if (_capture?.Active == true) _capture.ExplainBlockedExit();
            else _badge?.Flash("Concludi o annulla il comando prima di cambiare attività.", 5f);
        }

        private string ActivityTitle() => CurrentActivity == Activity.Design ? UiText.DesignActivity
            : CurrentActivity == Activity.Presentation ? UiText.PresentationActivity
            : CurrentActivity == Activity.Verification ? UiText.VerificationActivity : UiText.AppTitle;

        public bool RequestActivity(Activity target)
        {
            if (target == CurrentActivity && _inSession) return true;
            if (ProtectedActivityWork) { ReportActivityExitBlocked(); return false; }
            if (target == Activity.Home) { _activity.CancelPending(); LeaveSession(); return true; }
            if (target == Activity.Capture) { EnterRealityCapture(); return true; }
            if (_currentScene == null)
            {
                _activity.Request(target, false, needsConnection: true);
                if (_server == null) OpenConnection();
                else { RefreshHome(); _home.ShowMessage(ActivityTitle(), "Attendo un documento dal PC. L’attività scelta si aprirà quando sarà disponibile."); }
                return false;
            }
            ShowHome();
            _activity.Request(target, false);
            _inspect.SetActivity(target);
            EnterSession(environment.Mode);
            _badge?.Flash(target == Activity.Presentation ? UiText.PresentationActivityBody
                : target == Activity.Verification ? UiText.VerificationActivityBody : UiText.DesignActivityBody, 6f);
            return true;
        }

        private void OpenConnection()
        {
            if (_server == null) { ShowPairing(UiText.NotPaired, UiText.NotPairedBody); return; }
            _connectionPage = true;
            _home.gameObject.SetActive(true);
            _home.ShowMessage("Connessione", _server.Host + ":" + _server.Port + " · " + (_session?.Status.ToString() ?? "Offline"));
            _home.SetActions(("Torna", CloseConnection), (UiText.ForgetPc, Forget));
        }

        private void CloseConnection()
        {
            _connectionPage = false;
            if (_inSession) _home.gameObject.SetActive(false);
            else RefreshHome();
        }
    }
}
