using System;
using ProjectorDash;

internal static class InputTests
{
    private static int _passed;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _passed++;
    }
    public static int Main()
    {
        int taps = 0, wheel = 0, downs = 0, ups = 0;
        double x = 0, y = 0;
        TrackpadGesture gesture = new TrackpadGesture();
        gesture.Tap = delegate { taps++; };
        gesture.MovePointer = delegate(double dx, double dy) { x += dx; y += dy; };
        gesture.Scroll = delegate(int amount) { wheel += amount; };
        gesture.Drag = delegate(bool down) { if (down) downs++; else ups++; };

        gesture.Down(1, 10, 10, 0); gesture.Up(1, 100);
        Check(taps == 1, "A brief touch clicks once");
        gesture.Down(1, 10, 10, 150); gesture.Up(1, 220);
        Check(taps == 2, "Two taps produce two clicks for Windows double-click recognition");
        gesture.Down(1, 0, 0, 1000); gesture.Move(1, 30, 20); gesture.Up(1, 1100);
        Check(x == 30 && y == 20 && taps == 2, "Movement updates pointer without clicking");
        gesture.Down(1, 0, 0, 2000); gesture.Move(1, 20, 0); gesture.Move(1, 0, 0); gesture.Up(1, 2100);
        Check(taps == 2, "Returning to the start does not turn a swipe into a tap");
        gesture.Down(1, 0, 0, 3000); gesture.Up(1, 4000);
        Check(taps == 2, "Resting a finger does not click on release");

        x = y = 0;
        gesture.Down(1, 0, 0, 5000); gesture.Down(2, 20, 0, 5001);
        gesture.Move(1, 0, 36); gesture.Move(2, 20, 36);
        Check(wheel == 240 && x == 0 && y == 0, "Two fingers scroll by centroid without moving pointer");
        gesture.Up(2, 5100); gesture.Move(1, 10, 50); gesture.Up(1, 5150);
        Check(taps == 2 && x == 0 && y == 0, "Lifting one finger does not jump or click");
        gesture.Down(1, 0, 0, 5200); gesture.Down(2, 20, 0, 5201);
        gesture.Up(1, 5250); gesture.Up(2, 5260);
        Check(taps == 2, "Two-finger tap never becomes a left click");
        gesture.Down(1, 0, 0, 5300); gesture.Down(2, 0, 0, 5300);
        gesture.Move(1, 0, -18); gesture.Move(2, 0, -18);
        Check(wheel == 120, "Scrolling supports both directions");
        gesture.Cancel();

        gesture.DragNextTouch = true;
        gesture.Down(1, 0, 0, 6000); gesture.Move(1, 10, 10); gesture.Up(1, 6100);
        Check(downs == 1 && ups == 1 && taps == 2, "Drag releases on lift with no trailing click");
        gesture.Down(1, 0, 0, 6200); gesture.Up(1, 6250);
        Check(taps == 3 && downs == 1, "Drag mode is one-shot");
        gesture.DragNextTouch = true; gesture.Down(1, 0, 0, 7000);
        gesture.Cancel(); gesture.Cancel(); gesture.Up(1, 7100);
        Check(downs == 2 && ups == 2 && gesture.ContactCount == 0, "Capture loss cancels and releases exactly once");
        gesture.DragNextTouch = true; gesture.Down(1, 0, 0, 8000); gesture.Down(2, 10, 0, 8001);
        Check(downs == 3 && ups == 3, "Adding a second finger safely ends a drag");
        gesture.Up(1, 8050); gesture.Up(2, 8100);
        Check(taps == 3, "Interrupted drag never clicks");
        gesture.DragNextTouch = true; gesture.Cancel();
        Check(!gesture.DragNextTouch, "Hiding a pad clears armed drag mode");
        gesture.Move(99, 10, 10); gesture.Up(99, 9000);
        Check(gesture.ContactCount == 0 && taps == 3, "Stale events are ignored");
        gesture.Down(1, 0, 0, 10000); gesture.Down(1, 0, 0, 10000);
        Check(gesture.ContactCount == 1, "Duplicate contact IDs are ignored");
        gesture.Cancel();

        Check(PointerMath.Normalize(-1920, -1920, 3840) == 0, "Negative-origin desktop left edge");
        Check(PointerMath.Normalize(1919, -1920, 3840) == 65535, "Virtual desktop right edge");
        Check(PointerMath.Normalize(-4000, -1920, 3840) == 0, "Out of bounds coordinates clamp left");
        Check(PointerMath.Normalize(8000, -1920, 3840) == 65535, "Out of bounds coordinates clamp right");
        Check(PointerMath.Normalize(500, 500, 1) == 0, "One-pixel extent cannot divide by zero");
        Check(PointerMath.Clamp(-4000, -1920, 1920) == -1920, "Pointer stays on selected left monitor");
        Check(PointerMath.Clamp(500, -1920, 1920) == -1, "Pointer cannot cross from projector to tablet");
        Check(PointerMath.Normalize(-1080, -1080, 2160) == 0, "Vertical negative origins also normalize");
        Console.WriteLine("Passed " + _passed + " native trackpad checks.");
        return 0;
    }
}
