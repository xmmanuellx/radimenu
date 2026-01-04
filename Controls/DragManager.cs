using System;
using System.Windows;
using Point = System.Windows.Point;

namespace RadiMenu.Controls;

/// <summary>
/// Centralized state machine for Drag & Drop operations.
/// Eliminates scattered flags and ensures consistent state transitions.
/// </summary>
public class DragManager
{
    public enum DragState
    {
        Idle,           // No drag in progress
        PendingDrag,    // Mouse down, waiting for threshold
        Dragging        // Actively dragging
    }

    public enum DragTarget
    {
        None,
        MainMenu,
        Submenu
    }

    // --- State ---
    public DragState State { get; private set; } = DragState.Idle;
    public DragTarget Target { get; private set; } = DragTarget.None;
    public int SourceIndex { get; private set; } = -1;
    public int InitialIndex { get; private set; } = -1;
    public int CurrentIndex { get; private set; } = -1;
    public Point StartPoint { get; private set; }

    // --- Configuration ---
    public double DistanceThreshold { get; set; } = 10.0;
    public long TimeThresholdMs { get; set; } = 300;

    // --- Events ---
    public event Action<int, DragTarget>? DragActivated;
    public event Action<int, int, DragTarget>? ItemsSwapped;
    public event Action<DragTarget>? DragFinished;
    public event Action<DragTarget>? DragCanceled;

    // --- Timestamp for time-based activation ---
    private long _startTimeMs;

    /// <summary>
    /// Called on MouseDown. Prepares for potential drag.
    /// </summary>
    public void BeginPotentialDrag(int index, Point mousePos, DragTarget target)
    {
        if (State != DragState.Idle) return;

        State = DragState.PendingDrag;
        Target = target;
        SourceIndex = index;
        InitialIndex = index;
        CurrentIndex = index;
        StartPoint = mousePos;
        _startTimeMs = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
    }

    /// <summary>
    /// Called on MouseMove. Checks if drag should activate.
    /// Returns true if drag was just activated this call.
    /// </summary>
    public bool TryActivateDrag(Point currentPos)
    {
        if (State == DragState.Idle) return false;
        if (State == DragState.Dragging) return false; // Already active

        double distMoved = (currentPos - StartPoint).Length;
        long elapsedMs = (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) - _startTimeMs;

        if (distMoved > DistanceThreshold || elapsedMs >= TimeThresholdMs)
        {
            State = DragState.Dragging;
            DragActivated?.Invoke(SourceIndex, Target);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Called during drag to update target position. Fires swap events.
    /// </summary>
    public void UpdateDragTarget(int newTargetIndex)
    {
        if (State != DragState.Dragging) return;
        if (newTargetIndex == CurrentIndex) return;
        if (newTargetIndex < 0) return;

        // "Undo-then-Swap" logic to prevent scrambling
        if (CurrentIndex != InitialIndex)
        {
            ItemsSwapped?.Invoke(CurrentIndex, InitialIndex, Target);
        }

        ItemsSwapped?.Invoke(InitialIndex, newTargetIndex, Target);
        CurrentIndex = newTargetIndex;
    }

    /// <summary>
    /// Called on MouseUp. Commits the drag.
    /// </summary>
    public void FinishDrag()
    {
        if (State == DragState.Idle) return;

        var target = Target;
        Reset();
        DragFinished?.Invoke(target);
    }

    /// <summary>
    /// Called on MouseLeave or escape. Reverts the drag.
    /// </summary>
    public void CancelDrag()
    {
        if (State == DragState.Idle) return;

        var target = Target;

        // Revert to initial position if we actually moved things
        if (State == DragState.Dragging && CurrentIndex != InitialIndex)
        {
            ItemsSwapped?.Invoke(CurrentIndex, InitialIndex, target);
        }

        Reset();
        DragCanceled?.Invoke(target);
    }

    /// <summary>
    /// Check if we moved significantly (for click suppression in MouseUp).
    /// </summary>
    public bool HasMovedSignificantly(Point currentPos)
    {
        return (currentPos - StartPoint).Length > DistanceThreshold;
    }

    /// <summary>
    /// Check if currently in any drag state (pending or active).
    /// </summary>
    public bool IsActive => State != DragState.Idle;

    /// <summary>
    /// Check if actively dragging.
    /// </summary>
    public bool IsDragging => State == DragState.Dragging;

    private void Reset()
    {
        State = DragState.Idle;
        Target = DragTarget.None;
        SourceIndex = -1;
        InitialIndex = -1;
        CurrentIndex = -1;
        StartPoint = default;
    }
}
