using UnityEngine;

namespace DecoLimitLifter
{
    /// <summary>
    /// Shared classification for events that a modal ESU foreground must own.
    /// Keeping this pure prevents the three editor modes from drifting apart.
    /// </summary>
    internal static class EsuModalInputPolicy
    {
        internal static bool IsBlockingEventType(EventType eventType) =>
            eventType == EventType.MouseDown ||
            eventType == EventType.MouseUp ||
            eventType == EventType.MouseDrag ||
            eventType == EventType.ScrollWheel ||
            eventType == EventType.ContextClick ||
            eventType == EventType.KeyDown ||
            eventType == EventType.KeyUp;

        internal static EventType SuppressForDisabledBackground(Event current)
        {
            EventType originalType = current == null
                ? EventType.Ignore
                : current.type;
            if (current != null && IsBlockingEventType(originalType))
                current.type = EventType.Ignore;
            return originalType;
        }

        internal static void RestoreForForeground(
            Event current,
            EventType originalType)
        {
            if (current != null && IsBlockingEventType(originalType))
                current.type = originalType;
        }
    }
}
