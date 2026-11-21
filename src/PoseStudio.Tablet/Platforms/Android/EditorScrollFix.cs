using Android.Views;
using Microsoft.Maui.Handlers;
using AView = Android.Views.View;

namespace PoseStudio.Tablet;

/// <summary>Mehrzeilige Textfelder in der scrollbaren Einstellungsspalte: Solange das Feld selbst noch scrollen kann,
/// darf die äußere Spalte (ScrollView, Pull-to-Refresh) die Wischgeste nicht abfangen – sonst ruckelt bzw. springt das Scrollen im Feld.</summary>
public static class EditorScrollFix
{
    public static void Register() =>
        EditorHandler.Mapper.AppendToMapping("NestedScrollFix", (h, _) =>
        {
            h.PlatformView.VerticalScrollBarEnabled = true;
            h.PlatformView.SetOnTouchListener(new Listener());
        });

    private sealed class Listener : Java.Lang.Object, AView.IOnTouchListener
    {
        public bool OnTouch(AView? v, MotionEvent? e)
        {
            if (v == null || e == null) return false;
            bool scrollable = v.CanScrollVertically(1) || v.CanScrollVertically(-1);
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                case MotionEventActions.Move:
                    if (scrollable) v.Parent?.RequestDisallowInterceptTouchEvent(true);
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    v.Parent?.RequestDisallowInterceptTouchEvent(false);
                    break;
            }
            return false;   // Editor verarbeitet die Geste weiter selbst (Cursor, Auswahl, Scrollen)
        }
    }
}
