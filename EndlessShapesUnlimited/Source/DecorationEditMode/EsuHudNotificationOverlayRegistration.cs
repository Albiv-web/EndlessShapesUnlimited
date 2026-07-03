using System;
using DecoLimitLifter.SmartBuildMode;
using UnityEngine;

namespace DecoLimitLifter.DecorationEditMode
{
    internal static class EsuHudNotificationOverlayRegistration
    {
        private const string BehaviourName = "EndlessShapesUnlimited.GlobalNotifications";
        private static GameObject _host;
        private static EsuHudNotificationOverlayBehaviour _behaviour;
        private static bool _registered;

        internal static void Register()
        {
            if (_registered)
                return;

            _host = new GameObject(BehaviourName)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _behaviour = _host.AddComponent<EsuHudNotificationOverlayBehaviour>();
            _registered = true;
        }

        internal static void Unregister()
        {
            Exception failure = null;
            try
            {
                if (_host != null)
                    UnityEngine.Object.Destroy(_host);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            _behaviour = null;
            _host = null;
            _registered = false;
            if (failure != null)
                throw failure;
        }
    }

    internal sealed class EsuHudNotificationOverlayBehaviour : MonoBehaviour
    {
        private const float BaseWidth = 520f;
        private const float MinWidth = 360f;
        private const float TopOffset = 74f;
        private const float RightMargin = 18f;
        private const float SideMargin = 12f;
        private const float Height = 40f;

        private void OnGUI()
        {
            if (DecorationEditorInputScope.Active ||
                SmartBuildInputScope.Active)
            {
                return;
            }

            if (!EsuHudNotifications.HasMessage &&
                !EsuConsoleWindow.IsOpen)
            {
                return;
            }

            try
            {
                if (EsuHudNotifications.HasMessage)
                    DrawNotificationSlot();

                EsuHudNotifications.DrawExpandedPopup();
                EsuConsoleWindow.Draw();
            }
            catch (Exception exception)
            {
                try
                {
                    EsuRuntimeLog.Exception(
                        "ESU HUD",
                        exception,
                        "Global notification overlay failed.");
                }
                catch
                {
                    // GUI diagnostics must never break vanilla UI drawing.
                }
            }
        }

        private static void DrawNotificationSlot()
        {
            Rect rect = NotificationRect();
            GUILayout.BeginArea(rect);
            EsuHudNotifications.DrawToolbarSlot(
                new Rect(0f, 0f, rect.width, rect.height),
                rect.width,
                null,
                rect.position);
            GUILayout.EndArea();
        }

        private static Rect NotificationRect()
        {
            float margin = EsuHudLayout.Scale(SideMargin);
            float width = Mathf.Min(
                EsuHudLayout.Scale(BaseWidth),
                Mathf.Max(EsuHudLayout.Scale(MinWidth), Screen.width - margin * 2f));
            float x = Mathf.Clamp(
                Screen.width - width - EsuHudLayout.Scale(RightMargin),
                margin,
                Mathf.Max(margin, Screen.width - width - margin));
            return new Rect(
                x,
                EsuHudLayout.Scale(TopOffset),
                width,
                EsuHudLayout.Scale(Height));
        }
    }
}
